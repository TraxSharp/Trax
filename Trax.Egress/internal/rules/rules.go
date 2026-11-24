// Package rules writes the nftables rules that hold a pod's network namespace to its allowlist, and applies them.
//
// Every process but the egress's own (by uid) has its DNS sent to the egress resolver and its IPv4 TCP sent to the
// egress proxy, whatever address it dialled; anything else it sends that is not loopback is refused. The egress's
// own uid may reach the nameservers on port 53, the fixed addresses the list names, and TCP anywhere outside the
// loopback, link-local and multicast ranges: the proxy decides which of those connections to make. The rules are
// written by a process with NET_ADMIN that then exits (or drops it); the app has no NET_ADMIN, so it cannot change
// them, and the table is replaced whole in one transaction, so there is no moment with half of it applied.
package rules

import (
	"bytes"
	"fmt"
	"net/netip"
	"os/exec"
	"sort"
	"strings"

	"github.com/TraxSharp/Trax/Trax.Egress/internal/allow"
)

// Table is the nftables table the egress owns.
const Table = "trax_egress"

// Config is what the rules are written from.
type Config struct {
	// UID is the egress's own uid: its resolver and proxy run as it, and only it is let out.
	UID int
	// DNSPort and ProxyPort are where the resolver and the proxy listen on loopback.
	DNSPort   int
	ProxyPort int
	// Nameservers are the upstream resolvers, from resolv.conf.
	Nameservers []netip.Addr
	// Pinned are the TCP entries that name an address.
	Pinned []allow.Entry
}

// Render writes the nftables script. It deletes the table and writes it again in one file, which nft applies as
// one transaction.
func Render(c Config) (string, error) {
	if c.UID <= 0 || c.DNSPort <= 0 || c.ProxyPort <= 0 {
		return "", fmt.Errorf("uid, dns port and proxy port must be set")
	}
	var v4, v6 []string
	for _, a := range c.Nameservers {
		if a.Is4() {
			v4 = append(v4, a.String())
		} else {
			v6 = append(v6, a.String())
		}
	}
	var p4, p6 []string
	for _, e := range c.Pinned {
		if e.Addr.Is4() {
			p4 = append(p4, fmt.Sprintf("%s . %d", e.Addr, e.Port))
		} else {
			p6 = append(p6, fmt.Sprintf("%s . %d", e.Addr, e.Port))
		}
	}
	for _, s := range [][]string{v4, v6, p4, p6} {
		sort.Strings(s)
	}

	var b strings.Builder
	w := func(format string, args ...any) { fmt.Fprintf(&b, format+"\n", args...) }
	w("table inet %s", Table)
	w("delete table inet %s", Table)
	w("table inet %s {", Table)
	set(&b, "resolvers4", "ipv4_addr", v4)
	set(&b, "resolvers6", "ipv6_addr", v6)
	set(&b, "pinned4", "ipv4_addr . inet_service", p4)
	set(&b, "pinned6", "ipv6_addr . inet_service", p6)

	// Before the runtime's own nat (docker's embedded DNS rewrites 127.0.0.11:53 at priority -100), so a query
	// the app sends there reaches the egress resolver, not the runtime's.
	w("  chain nat_output {")
	w("    type nat hook output priority -150; policy accept;")
	w("    meta skuid %d return", c.UID)
	w("    udp dport 53 redirect to :%d", c.DNSPort)
	w("    tcp dport 53 redirect to :%d", c.DNSPort)
	w("    oif \"lo\" return")
	w("    meta nfproto ipv4 meta l4proto tcp redirect to :%d", c.ProxyPort)
	w("  }")

	w("  chain filter_output {")
	w("    type filter hook output priority 0; policy drop;")
	// 127.0.0.1 is where the redirects land and where the app talks to itself. A redirected packet still names
	// the interface it was first routed to here (the kernel routes it again after this hook), so this matches on
	// the address alone. The rest of 127/8 is not open: docker's embedded DNS listens on 127.0.0.11, and a query
	// sent straight to it would skip the resolver.
	w("    ip daddr 127.0.0.1 accept")
	w("    ip6 daddr ::1 accept")
	// The pod's own addresses are routed through lo too.
	w("    oif \"lo\" ip daddr != 127.0.0.0/8 accept")
	w("    oif \"lo\" meta nfproto ipv6 accept")
	w("    ct state established,related accept")
	w("    meta skuid %d jump egress", c.UID)
	w("    counter reject")
	w("  }")

	w("  chain egress {")
	w("    oif \"lo\" accept")
	w("    ip daddr @resolvers4 udp dport 53 accept")
	w("    ip daddr @resolvers4 tcp dport 53 accept")
	w("    ip6 daddr @resolvers6 udp dport 53 accept")
	w("    ip6 daddr @resolvers6 tcp dport 53 accept")
	w("    ip daddr . tcp dport @pinned4 accept")
	w("    ip6 daddr . tcp dport @pinned6 accept")
	w("    ip daddr { 0.0.0.0/8, 127.0.0.0/8, 169.254.0.0/16, 224.0.0.0/3 } reject")
	w("    meta nfproto ipv4 meta l4proto tcp accept")
	w("    counter reject")
	w("  }")
	w("}")
	return b.String(), nil
}

func set(b *strings.Builder, name, typ string, elements []string) {
	fmt.Fprintf(b, "  set %s {\n    type %s;\n", name, typ)
	if len(elements) > 0 {
		fmt.Fprintf(b, "    elements = { %s }\n", strings.Join(elements, ", "))
	}
	b.WriteString("  }\n")
}

// Apply runs nft with the script, then prints the table as the kernel holds it.
func Apply(script string, out *bytes.Buffer) error {
	cmd := exec.Command("nft", "-f", "-")
	cmd.Stdin = strings.NewReader(script)
	var stderr bytes.Buffer
	cmd.Stderr = &stderr
	if err := cmd.Run(); err != nil {
		return fmt.Errorf("nft refused the rules: %v: %s", err, strings.TrimSpace(stderr.String()))
	}
	list := exec.Command("nft", "list", "table", "inet", Table)
	list.Stdout = out
	list.Stderr = &stderr
	if err := list.Run(); err != nil {
		return fmt.Errorf("the rules were applied but could not be listed: %v: %s", err, strings.TrimSpace(stderr.String()))
	}
	return nil
}
