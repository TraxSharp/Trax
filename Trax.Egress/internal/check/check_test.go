package check

import (
	"bytes"
	"context"
	"net"
	"os"
	"strings"
	"syscall"
	"testing"

	"github.com/TraxSharp/Trax/Trax.Egress/internal/allow"
)

func TestProbesCoverTheListAndTheDefaults(t *testing.T) {
	l, err := allow.Parse("api.anthropic.com tcp:database:5432 tcp:169.254.170.23:80")
	if err != nil {
		t.Fatal(err)
	}
	probes := Probes(l, []string{"203.0.113.9:443"})
	var lines []string
	for _, p := range probes {
		lines = append(lines, p.Kind+" "+p.Target+" "+p.Want)
	}
	all := strings.Join(lines, "\n")
	for _, want := range []string{
		"control 192.0.2.1:9 intercepted",
		"tls api.anthropic.com:443 open",
		"tcp database:5432 open",
		"tcp 169.254.170.23:80 open",
		"dns example.com blocked",
		"dns example.com @1.1.1.1 blocked",
		"tls example.com via 1.1.1.1:443 blocked",
		"tcp 169.254.169.254:80 blocked",
		"tcp 10.0.0.1:80 blocked",
		"tcp 192.168.1.1:80 blocked",
		"tcp 100.100.100.100:80 blocked",
		"tcp 203.0.113.9:443 blocked",
		"nft flush ruleset blocked",
	} {
		if !strings.Contains(all, want) {
			t.Errorf("no probe %q in\n%s", want, all)
		}
	}
	// What the list allows is not also probed as blocked.
	if strings.Contains(all, "tcp 169.254.170.23:80 blocked") {
		t.Error("a pinned address is probed as blocked")
	}
}

func TestAListedExampleNameIsNotTheDNSProbe(t *testing.T) {
	l, _ := allow.Parse("example.com")
	for _, p := range Probes(l, nil) {
		if p.Kind == "dns" && strings.HasPrefix(p.Target, "example.com") {
			t.Fatalf("probed a listed name as blocked: %s", p.Target)
		}
	}
}

func TestReportFailsOnAnyMismatch(t *testing.T) {
	results := []Result{
		{Probe{"tcp", "a", Blocked, nil}, Blocked},
		{Probe{"tcp", "b", Blocked, nil}, Open},
	}
	var out bytes.Buffer
	if Report(&out, results) {
		t.Fatal("reported success")
	}
	if !strings.Contains(out.String(), "FAIL tcp") || !strings.Contains(out.String(), "NOT as intended") {
		t.Fatalf("got:\n%s", out.String())
	}
	if !Report(&out, results[:1]) {
		t.Fatal("reported failure")
	}
}

func TestRunRunsEveryProbe(t *testing.T) {
	n := 0
	probe := Probe{"x", "y", Open, func(context.Context) string { n++; return Open }}
	if got := Run(context.Background(), []Probe{probe, probe}); len(got) != 2 || n != 2 || !got[0].OK() {
		t.Fatalf("%+v", got)
	}
}

func TestParseDeny(t *testing.T) {
	got, err := ParseDeny("203.0.113.9:443, [fd7a::1]:22 peer.tailnet.example:80")
	if err != nil || len(got) != 3 {
		t.Fatalf("%v %v", got, err)
	}
	for _, bad := range []string{"203.0.113.9", ":80", "host:0", "host:x"} {
		if _, err := ParseDeny(bad); err == nil {
			t.Errorf("%q was accepted", bad)
		}
	}
}

func TestTheControlCountsAResetAsIntercepted(t *testing.T) {
	cases := map[string]struct {
		err  error
		want string
	}{
		"connected":    {nil, Intercepted},
		"reset":        {&net.OpError{Op: "dial", Err: os.NewSyscallError("connect", syscall.ECONNRESET)}, Intercepted},
		"timeout":      {context.DeadlineExceeded, "not " + Intercepted},
		"refused":      {&net.OpError{Op: "dial", Err: os.NewSyscallError("connect", syscall.ECONNREFUSED)}, "not " + Intercepted},
		"unreachable":  {&net.OpError{Op: "dial", Err: os.NewSyscallError("connect", syscall.ENETUNREACH)}, "not " + Intercepted},
		"host unreach": {&net.OpError{Op: "dial", Err: os.NewSyscallError("connect", syscall.EHOSTUNREACH)}, "not " + Intercepted},
	}
	for name, c := range cases {
		if got := Control(c.err); !strings.HasPrefix(got, c.want) || (c.want == Intercepted && got != c.want) {
			t.Errorf("%s: got %q, want %q", name, got, c.want)
		}
	}
}
