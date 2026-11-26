// Package allow reads the egress allowlist: which names, and which of the deployment's own services, a pod may
// reach. Every other part of the image (the rules, the resolver, the proxy and the check) reads it from here, so
// they cannot disagree about what is allowed.
package allow

import (
	"bufio"
	"errors"
	"fmt"
	"net/netip"
	"os"
	"sort"
	"strconv"
	"strings"
)

// Kind says how an entry is enforced.
type Kind int

const (
	// TLS is a public name reached over TLS. The proxy reads the name the client asks for (its SNI), refuses any
	// name not on the list, resolves the name itself and connects to what it resolves to, never to the address the
	// client dialled. Only public addresses count: a name that resolves into a private or link-local range is
	// refused.
	TLS Kind = iota
	// TCP is one of the deployment's own services (its database or broker), or a fixed address. Plain TCP to the
	// port is allowed when the address dialled is one the name resolves to now, private ranges included.
	TCP
)

// DefaultTLSPort is the port a TLS entry without one gets.
const DefaultTLSPort = 443

// MaxEntries bounds the list. It is a deployment's handful of domains, not a blocklist.
const MaxEntries = 256

// Entry is one allowed destination.
type Entry struct {
	Kind Kind
	// Host is a lower-case name, or empty when Addr is set.
	Host string
	// Addr is set for a TCP entry that names an address rather than a name.
	Addr netip.Addr
	Port int
}

// String is the entry as it is written in the list.
func (e Entry) String() string {
	host := e.Host
	if e.Addr.IsValid() {
		host = e.Addr.String()
		if e.Addr.Is6() {
			host = "[" + host + "]"
		}
	}
	if e.Kind == TCP {
		return fmt.Sprintf("tcp:%s:%d", host, e.Port)
	}
	return fmt.Sprintf("%s:%d", host, e.Port)
}

// List is a parsed allowlist.
type List struct {
	Entries []Entry
}

// Names is every name an entry holds, sorted, each once. The resolver answers these and nothing else.
func (l List) Names() []string {
	seen := map[string]bool{}
	for _, e := range l.Entries {
		if e.Host != "" {
			seen[e.Host] = true
		}
	}
	names := make([]string, 0, len(seen))
	for n := range seen {
		names = append(names, n)
	}
	sort.Strings(names)
	return names
}

// TLSFor is the TLS entry for name on port, if there is one.
func (l List) TLSFor(name string, port int) (Entry, bool) {
	for _, e := range l.Entries {
		if e.Kind == TLS && e.Host == name && e.Port == port {
			return e, true
		}
	}
	return Entry{}, false
}

// TCPFor is every TCP entry on port.
func (l List) TCPFor(port int) []Entry {
	var out []Entry
	for _, e := range l.Entries {
		if e.Kind == TCP && e.Port == port {
			out = append(out, e)
		}
	}
	return out
}

// Pinned is every TCP entry that names an address, which the rules let the proxy reach even inside the ranges it
// is otherwise kept out of (link-local, for the EKS Pod Identity agent).
func (l List) Pinned() []Entry {
	var out []Entry
	for _, e := range l.Entries {
		if e.Kind == TCP && e.Addr.IsValid() {
			out = append(out, e)
		}
	}
	return out
}

// Environment names the image reads.
const (
	// EnvAllow holds entries separated by spaces, commas or new lines.
	EnvAllow = "TRAX_EGRESS_ALLOW"
	// EnvAllowFile names a file of entries, one a line, with # comments.
	EnvAllowFile = "TRAX_EGRESS_ALLOW_FILE"
	// EnvPostgres holds an Npgsql connection string whose hosts become TCP entries: the database, wherever the
	// deployment keeps it, without its address being written anywhere but the Secret that already holds it.
	EnvPostgres = "TRAX_EGRESS_POSTGRES"
)

// FromEnvironment reads the list from the environment, through getenv and readFile so tests need neither.
func FromEnvironment(getenv func(string) string, readFile func(string) ([]byte, error)) (List, error) {
	var texts []string
	if v := getenv(EnvAllow); v != "" {
		texts = append(texts, v)
	}
	if path := getenv(EnvAllowFile); path != "" {
		data, err := readFile(path)
		if err != nil {
			return List{}, fmt.Errorf("%s: %w", EnvAllowFile, err)
		}
		texts = append(texts, stripComments(string(data)))
	}
	list, err := Parse(strings.Join(texts, "\n"))
	if err != nil {
		return List{}, err
	}
	if cs := getenv(EnvPostgres); cs != "" {
		hosts, err := PostgresHosts(cs)
		if err != nil {
			return List{}, fmt.Errorf("%s: %w", EnvPostgres, err)
		}
		list, err = list.with(hosts)
		if err != nil {
			return List{}, err
		}
	}
	return list, nil
}

// Read is FromEnvironment over the process's own environment and files.
func Read() (List, error) { return FromEnvironment(os.Getenv, os.ReadFile) }

func stripComments(text string) string {
	var b strings.Builder
	sc := bufio.NewScanner(strings.NewReader(text))
	for sc.Scan() {
		line := sc.Text()
		if i := strings.IndexByte(line, '#'); i >= 0 {
			line = line[:i]
		}
		b.WriteString(line)
		b.WriteByte('\n')
	}
	return b.String()
}

// Parse reads entries separated by spaces, commas or new lines. An empty text is an empty list: nothing leaves
// but what the deployment's own services need, which the caller adds as TCP entries.
func Parse(text string) (List, error) {
	fields := strings.FieldsFunc(text, func(r rune) bool {
		return r == ',' || r == ' ' || r == '\t' || r == '\n' || r == '\r'
	})
	var list List
	for _, f := range fields {
		e, err := ParseEntry(f)
		if err != nil {
			return List{}, err
		}
		list, err = list.with([]Entry{e})
		if err != nil {
			return List{}, err
		}
	}
	return list, nil
}

func (l List) with(entries []Entry) (List, error) {
	out := List{Entries: append([]Entry(nil), l.Entries...)}
	for _, e := range entries {
		dup := false
		for _, have := range out.Entries {
			if have == e {
				dup = true
				break
			}
		}
		if dup {
			continue
		}
		if len(out.Entries) >= MaxEntries {
			return List{}, fmt.Errorf("more than %d entries", MaxEntries)
		}
		out.Entries = append(out.Entries, e)
	}
	return out, nil
}

// ParseEntry reads one entry: name[:port] for a public name over TLS (443 when no port is given), or
// tcp:host:port for one of the deployment's own services or a fixed address.
func ParseEntry(text string) (Entry, error) {
	if text == "" {
		return Entry{}, errors.New("empty entry")
	}
	if strings.ContainsAny(text, "*/@?=") {
		return Entry{}, fmt.Errorf("%q: an entry is a name, with a port if it is not 443: no wildcard, scheme or path", text)
	}
	if rest, ok := strings.CutPrefix(text, "tcp:"); ok {
		host, port, err := splitHostPort(rest, 0)
		if err != nil {
			return Entry{}, fmt.Errorf("%q: %w", text, err)
		}
		if addr, err := netip.ParseAddr(host); err == nil {
			if addr.Zone() != "" || addr.IsUnspecified() || addr.IsMulticast() {
				return Entry{}, fmt.Errorf("%q: not an address a service listens on", text)
			}
			return Entry{Kind: TCP, Addr: addr.Unmap(), Port: port}, nil
		}
		if !ValidName(host, 1) {
			return Entry{}, fmt.Errorf("%q: %q is not a host name", text, host)
		}
		return Entry{Kind: TCP, Host: host, Port: port}, nil
	}
	if strings.Contains(text, ":") && strings.Count(text, ":") > 1 {
		return Entry{}, fmt.Errorf("%q: an address is not a name; egress allows names, so the proxy can check what the client asks for", text)
	}
	host, port, err := splitHostPort(text, DefaultTLSPort)
	if err != nil {
		return Entry{}, fmt.Errorf("%q: %w", text, err)
	}
	if _, err := netip.ParseAddr(host); err == nil {
		return Entry{}, fmt.Errorf("%q: an address is not a name; egress allows names, so the proxy can check what the client asks for", text)
	}
	if !ValidName(host, 2) {
		return Entry{}, fmt.Errorf("%q: %q is not a domain name (lower-case labels of letters, digits and dashes, at least two of them)", text, host)
	}
	return Entry{Kind: TLS, Host: host, Port: port}, nil
}

func splitHostPort(text string, defaultPort int) (string, int, error) {
	host, portText := text, ""
	if strings.HasPrefix(text, "[") {
		end := strings.IndexByte(text, ']')
		if end < 0 {
			return "", 0, errors.New("unclosed [")
		}
		host = text[1:end]
		rest := text[end+1:]
		if rest != "" {
			if !strings.HasPrefix(rest, ":") {
				return "", 0, errors.New("text after ]")
			}
			portText = rest[1:]
		}
	} else if i := strings.LastIndexByte(text, ':'); i >= 0 {
		host, portText = text[:i], text[i+1:]
	}
	if portText == "" {
		if defaultPort == 0 {
			return "", 0, errors.New("no port")
		}
		return strings.TrimSuffix(host, "."), defaultPort, nil
	}
	port, err := strconv.Atoi(portText)
	if err != nil || port < 1 || port > 65535 || strconv.Itoa(port) != portText {
		return "", 0, fmt.Errorf("port %q is not a TCP port", portText)
	}
	return strings.TrimSuffix(host, "."), port, nil
}

// ValidName says whether name is a lower-case host name of at least minLabels labels: letters, digits and dashes,
// no label longer than 63 or starting or ending with a dash, at most 253 in all, and a last label that is not all
// digits (so an address is never read as a name).
func ValidName(name string, minLabels int) bool {
	if name == "" || len(name) > 253 {
		return false
	}
	labels := strings.Split(name, ".")
	if len(labels) < minLabels {
		return false
	}
	for _, l := range labels {
		if l == "" || len(l) > 63 || l[0] == '-' || l[len(l)-1] == '-' {
			return false
		}
		for i := 0; i < len(l); i++ {
			c := l[i]
			if !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-') {
				return false
			}
		}
	}
	last := labels[len(labels)-1]
	allDigits := true
	for i := 0; i < len(last); i++ {
		if last[i] < '0' || last[i] > '9' {
			allDigits = false
			break
		}
	}
	return !allDigits
}
