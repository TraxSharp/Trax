package allow

import (
	"errors"
	"net/netip"
	"strconv"
	"strings"
	"testing"
)

func TestParseEntry(t *testing.T) {
	cases := []struct {
		in   string
		want Entry
	}{
		{"api.anthropic.com", Entry{Kind: TLS, Host: "api.anthropic.com", Port: 443}},
		{"api.anthropic.com.", Entry{Kind: TLS, Host: "api.anthropic.com", Port: 443}},
		{"registry.npmjs.org:8443", Entry{Kind: TLS, Host: "registry.npmjs.org", Port: 8443}},
		{"tcp:database:5432", Entry{Kind: TCP, Host: "database", Port: 5432}},
		{"tcp:db.abc.us-east-1.rds.amazonaws.com:5432", Entry{Kind: TCP, Host: "db.abc.us-east-1.rds.amazonaws.com", Port: 5432}},
		{"tcp:169.254.170.23:80", Entry{Kind: TCP, Addr: netip.MustParseAddr("169.254.170.23"), Port: 80}},
		{"tcp:[fd00:ec2::23]:80", Entry{Kind: TCP, Addr: netip.MustParseAddr("fd00:ec2::23"), Port: 80}},
	}
	for _, c := range cases {
		got, err := ParseEntry(c.in)
		if err != nil {
			t.Errorf("%q: %v", c.in, err)
			continue
		}
		if got != c.want {
			t.Errorf("%q: got %+v, want %+v", c.in, got, c.want)
		}
	}
}

func TestParseEntryRefuses(t *testing.T) {
	for _, in := range []string{
		"*.anthropic.com",           // wildcard
		"https://api.anthropic.com", // scheme
		"api.anthropic.com/v1",      // path
		"API.Anthropic.com",         // upper case
		"localhost",                 // one label
		"1.1.1.1",                   // address as a name
		"1.1.1.1:443",               // address as a name
		"[2606:4700::1111]:443",     // address as a name
		"2606:4700::1111",           // address as a name
		"api.anthropic.com:0",       // port
		"api.anthropic.com:65536",   // port
		"api.anthropic.com:44x",     // port
		"api.anthropic.com:0443",    // port written oddly
		"-bad.example.com",          // label starts with a dash
		"bad-.example.com",          // label ends with a dash
		"a..b",                      // empty label
		"exa_mple.com",              // underscore
		"user@example.com",          // not a host
		"tcp:database",              // tcp needs a port
		"tcp:0.0.0.0:80",            // unspecified
		"tcp:224.0.0.1:80",          // multicast
		"tcp:fe80::1%eth0:80",       // zone
		strings.Repeat("a", 64) + ".com",
		strings.Repeat("abcdefghi.", 26) + "com",
	} {
		if _, err := ParseEntry(in); err == nil {
			t.Errorf("%q was accepted", in)
		}
	}
}

func TestParseSplitsAndDeduplicates(t *testing.T) {
	l, err := Parse("api.anthropic.com, registry.npmjs.org\n tcp:database:5432 api.anthropic.com:443")
	if err != nil {
		t.Fatal(err)
	}
	if len(l.Entries) != 3 {
		t.Fatalf("got %d entries: %+v", len(l.Entries), l.Entries)
	}
	if got := strings.Join(l.Names(), " "); got != "api.anthropic.com database registry.npmjs.org" {
		t.Errorf("names %q", got)
	}
}

func TestEmptyListIsDenyAll(t *testing.T) {
	l, err := Parse("  \n ")
	if err != nil || len(l.Entries) != 0 {
		t.Fatalf("got %+v, %v", l, err)
	}
}

func TestTooManyEntries(t *testing.T) {
	var b strings.Builder
	for i := 0; i <= MaxEntries; i++ {
		b.WriteString("tcp:10.0." + strconv.Itoa(i/256) + "." + strconv.Itoa(i%256) + ":80 ")
	}
	if _, err := Parse(b.String()); err == nil {
		t.Fatal("accepted more than MaxEntries")
	}
}

func TestFromEnvironmentMergesEverySource(t *testing.T) {
	env := map[string]string{
		EnvAllow:     "api.anthropic.com",
		EnvAllowFile: "/allow",
		EnvPostgres:  "Host=db.internal,replica:6432;Port=5433;Password=secret;Username=app",
	}
	files := map[string]string{"/allow": "# models\nregistry.npmjs.org # packages\n\n"}
	l, err := FromEnvironment(func(k string) string { return env[k] }, func(p string) ([]byte, error) {
		if s, ok := files[p]; ok {
			return []byte(s), nil
		}
		return nil, errors.New("no such file")
	})
	if err != nil {
		t.Fatal(err)
	}
	var got []string
	for _, e := range l.Entries {
		got = append(got, e.String())
	}
	want := "api.anthropic.com:443 registry.npmjs.org:443 tcp:db.internal:5433 tcp:replica:6432"
	if strings.Join(got, " ") != want {
		t.Errorf("got %q, want %q", strings.Join(got, " "), want)
	}
	for _, e := range l.Entries {
		if strings.Contains(e.String(), "secret") {
			t.Error("the password reached the list")
		}
	}
}

func TestFromEnvironmentRefusesAMissingFile(t *testing.T) {
	_, err := FromEnvironment(func(k string) string {
		if k == EnvAllowFile {
			return "/nope"
		}
		return ""
	}, func(string) ([]byte, error) { return nil, errors.New("no such file") })
	if err == nil {
		t.Fatal("a missing allow file was ignored")
	}
}

func TestPostgresHosts(t *testing.T) {
	cases := map[string]string{
		"Host=database;Database=app;Username=app": "tcp:database:5432",
		"Server=DB.Example.com;Port=6000":         "tcp:db.example.com:6000",
		"host = \"10.0.0.5\" ; port = 5433":       "tcp:10.0.0.5:5433",
		"Host=/var/run/postgresql,db:5440":        "tcp:db:5440",
		"Host=[fd00::5]:5432":                     "tcp:[fd00::5]:5432",
		"Host=fd00::6;Port=5432":                  "tcp:[fd00::6]:5432",
	}
	for in, want := range cases {
		entries, err := PostgresHosts(in)
		if err != nil {
			t.Errorf("%q: %v", in, err)
			continue
		}
		var got []string
		for _, e := range entries {
			got = append(got, e.String())
		}
		if strings.Join(got, " ") != want {
			t.Errorf("%q: got %q, want %q", in, strings.Join(got, " "), want)
		}
	}
	for _, in := range []string{"Database=app", "Host=/tmp", "Host=db;Port=99999", "Host=db;Port=x", "Host=bad_name"} {
		if _, err := PostgresHosts(in); err == nil {
			t.Errorf("%q was accepted", in)
		}
	}
}

func TestPinnedAndLookups(t *testing.T) {
	l, err := Parse("api.anthropic.com tcp:169.254.170.23:80 tcp:database:5432")
	if err != nil {
		t.Fatal(err)
	}
	if p := l.Pinned(); len(p) != 1 || p[0].Port != 80 {
		t.Errorf("pinned %+v", p)
	}
	if _, ok := l.TLSFor("api.anthropic.com", 443); !ok {
		t.Error("TLSFor missed the entry")
	}
	if _, ok := l.TLSFor("api.anthropic.com", 8443); ok {
		t.Error("TLSFor matched another port")
	}
	if got := l.TCPFor(5432); len(got) != 1 || got[0].Host != "database" {
		t.Errorf("TCPFor %+v", got)
	}
}
