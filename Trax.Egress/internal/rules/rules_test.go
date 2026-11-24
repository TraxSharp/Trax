package rules

import (
	"net/netip"
	"strings"
	"testing"

	"github.com/TraxSharp/Trax/Trax.Egress/internal/allow"
)

func render(t *testing.T) string {
	t.Helper()
	pin, err := allow.ParseEntry("tcp:169.254.170.23:80")
	if err != nil {
		t.Fatal(err)
	}
	s, err := Render(Config{
		UID: 61000, DNSPort: 15053, ProxyPort: 15001,
		Nameservers: []netip.Addr{netip.MustParseAddr("10.96.0.10"), netip.MustParseAddr("fd00::10")},
		Pinned:      []allow.Entry{pin},
	})
	if err != nil {
		t.Fatal(err)
	}
	return s
}

func TestTheTableIsReplacedWhole(t *testing.T) {
	s := render(t)
	if !strings.HasPrefix(s, "table inet trax_egress\ndelete table inet trax_egress\ntable inet trax_egress {") {
		t.Fatalf("does not replace the table in one file:\n%s", s)
	}
}

func TestEveryoneButTheEgressIsRedirected(t *testing.T) {
	s := render(t)
	nat := section(s, "chain nat_output")
	order := []string{
		"priority -150",
		"meta skuid 61000 return",
		"udp dport 53 redirect to :15053",
		"tcp dport 53 redirect to :15053",
		"oif \"lo\" return",
		"meta nfproto ipv4 meta l4proto tcp redirect to :15001",
	}
	at := 0
	for _, want := range order {
		i := strings.Index(nat[at:], want)
		if i < 0 {
			t.Fatalf("nat_output lacks %q after position %d:\n%s", want, at, nat)
		}
		at += i
	}
}

func TestTheFilterDropsByDefaultAndOnlyLetsTheEgressOut(t *testing.T) {
	s := render(t)
	filter := section(s, "chain filter_output")
	for _, want := range []string{
		"policy drop;",
		"    ip daddr 127.0.0.1 accept",
		"    ip6 daddr ::1 accept",
		"oif \"lo\" ip daddr != 127.0.0.0/8 accept",
		"meta skuid 61000 jump egress",
		"counter reject",
	} {
		if !strings.Contains(filter, want) {
			t.Errorf("filter_output lacks %q", want)
		}
	}
	if strings.Contains(filter, "oif \"lo\" accept") {
		t.Error("filter_output lets anyone reach all of 127/8, docker's DNS on 127.0.0.11 among it")
	}
	egress := section(s, "chain egress")
	for _, want := range []string{
		"ip daddr @resolvers4 udp dport 53 accept",
		"ip daddr . tcp dport @pinned4 accept",
		"169.254.0.0/16",
		"meta nfproto ipv4 meta l4proto tcp accept",
		"counter reject",
	} {
		if !strings.Contains(egress, want) {
			t.Errorf("egress lacks %q", want)
		}
	}
	// The pinned address is let through before link-local is refused.
	if strings.Index(egress, "@pinned4") > strings.Index(egress, "169.254.0.0/16") {
		t.Error("the link-local refusal comes before the pinned addresses")
	}
	if strings.Contains(egress, "udp accept") || strings.Contains(egress, "l4proto udp") {
		t.Error("the egress may send UDP beyond DNS")
	}
}

func TestSetsHoldTheNameserversAndPins(t *testing.T) {
	s := render(t)
	for _, want := range []string{
		"set resolvers4 {\n    type ipv4_addr;\n    elements = { 10.96.0.10 }",
		"set resolvers6 {\n    type ipv6_addr;\n    elements = { fd00::10 }",
		"set pinned4 {\n    type ipv4_addr . inet_service;\n    elements = { 169.254.170.23 . 80 }",
		"set pinned6 {\n    type ipv6_addr . inet_service;\n  }",
	} {
		if !strings.Contains(s, want) {
			t.Errorf("lacks %q in\n%s", want, s)
		}
	}
}

func TestRenderRefusesAMissingSetting(t *testing.T) {
	if _, err := Render(Config{DNSPort: 1, ProxyPort: 2}); err == nil {
		t.Fatal("uid 0 was accepted")
	}
}

func section(s, head string) string {
	i := strings.Index(s, head)
	if i < 0 {
		return ""
	}
	rest := s[i:]
	if j := strings.Index(rest, "\n  }"); j >= 0 {
		return rest[:j]
	}
	return rest
}
