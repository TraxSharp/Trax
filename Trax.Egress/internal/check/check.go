// Package check proves a pod's egress from inside its network namespace, as the app sees it: what the list allows
// must open, and everything else must not. It reads the same list the sidecar does, so a stack's check is one
// command, and it carries its own positive controls, so a probe that cannot work reports a failure rather than
// "blocked".
package check

import (
	"context"
	"crypto/tls"
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"net"
	"os/exec"
	"strconv"
	"strings"
	"syscall"
	"time"

	"github.com/TraxSharp/Trax/Trax.Egress/internal/allow"
)

// Outcomes a probe reports.
const (
	Open        = "open"
	Blocked     = "blocked"
	Intercepted = "intercepted"
)

// EnvDeny lists more host:port destinations that must be blocked, such as the host's WAN address or a tailnet peer.
const EnvDeny = "TRAX_EGRESS_CHECK_DENY"

// DefaultDeny is probed on every run: cloud metadata (EC2's and EKS Pod Identity's), each private range, the
// tailnet's resolver, and a public IPv6 address.
var DefaultDeny = []string{
	"169.254.169.254:80",
	"169.254.170.23:80",
	"10.0.0.1:80",
	"172.16.0.1:80",
	"192.168.1.1:80",
	"100.100.100.100:80",
	"[2606:4700:4700::1111]:443",
}

// Probe is one destination and what it must do.
type Probe struct {
	Kind   string
	Target string
	Want   string
	Run    func(ctx context.Context) string
}

// Result is a probe's outcome.
type Result struct {
	Probe Probe
	Got   string
}

// OK says whether the probe did what it must.
func (r Result) OK() bool { return r.Got == r.Probe.Want }

// Probes builds the probes for list, plus deny, the extra destinations that must be blocked.
func Probes(list allow.List, deny []string) []Probe {
	var probes []Probe
	// The control: an address nothing answers on. Without the egress the connection times out; with it the
	// rules hand it to the proxy, which accepts and resets it. A check that sees no interception is not
	// running behind the egress, and every "blocked" it reported would mean nothing.
	probes = append(probes, Probe{"control", "192.0.2.1:9", Intercepted, func(ctx context.Context) string {
		c, err := dialTimeout(ctx, "tcp4", "192.0.2.1:9", 3*time.Second)
		if c != nil {
			c.Close()
		}
		return Control(err)
	}})
	for _, e := range list.Entries {
		e := e
		switch {
		case e.Kind == allow.TLS:
			probes = append(probes, Probe{"tls", e.String(), Open, func(ctx context.Context) string {
				return tlsProbe(ctx, net.JoinHostPort(e.Host, strconv.Itoa(e.Port)), e.Host)
			}})
		default:
			target := e.String()[len("tcp:"):]
			probes = append(probes, Probe{"tcp", target, Open, func(ctx context.Context) string {
				return tcpProbe(ctx, target)
			}})
		}
	}
	unlisted := ""
	for _, n := range []string{"example.com", "example.net", "example.org"} {
		if !listed(list, n) {
			unlisted = n
			break
		}
	}
	if unlisted != "" {
		probes = append(probes,
			Probe{"dns", unlisted, Blocked, func(ctx context.Context) string {
				addrs, err := net.DefaultResolver.LookupHost(ctx, unlisted)
				if err != nil || len(addrs) == 0 {
					return Blocked
				}
				return Open
			}},
			Probe{"dns", unlisted + " @1.1.1.1", Blocked, func(ctx context.Context) string {
				return dnsDirect(ctx, "1.1.1.1:53", unlisted)
			}},
			Probe{"tls", unlisted + " via 1.1.1.1:443", Blocked, func(ctx context.Context) string {
				return tlsProbe(ctx, "1.1.1.1:443", unlisted)
			}},
		)
	}
	pinned := map[string]bool{}
	for _, e := range list.Entries {
		if e.Kind == allow.TCP {
			pinned[e.String()[len("tcp:"):]] = true
		}
	}
	for _, d := range append(append([]string(nil), DefaultDeny...), deny...) {
		d := d
		if pinned[d] {
			continue
		}
		probes = append(probes, Probe{"tcp", d, Blocked, func(ctx context.Context) string { return tcpProbe(ctx, d) }})
	}
	probes = append(probes,
		Probe{"nft", "list ruleset", Blocked, func(ctx context.Context) string { return nft(ctx, "list", "ruleset") }},
		Probe{"nft", "flush ruleset", Blocked, func(ctx context.Context) string { return nft(ctx, "flush", "ruleset") }},
	)
	return probes
}

func listed(list allow.List, name string) bool {
	for _, n := range list.Names() {
		if n == name {
			return true
		}
	}
	return false
}

// Run runs every probe in order.
func Run(ctx context.Context, probes []Probe) []Result {
	results := make([]Result, 0, len(probes))
	for _, p := range probes {
		results = append(results, Result{p, p.Run(ctx)})
	}
	return results
}

// Report writes one line a probe and says whether all passed.
func Report(w io.Writer, results []Result) bool {
	ok := true
	for _, r := range results {
		mark := "ok"
		if !r.OK() {
			mark = "FAIL"
			ok = false
		}
		fmt.Fprintf(w, "%-4s %-7s %-40s %-14s (want %s)\n", mark, r.Probe.Kind, r.Probe.Target, r.Got, r.Probe.Want)
	}
	if ok {
		fmt.Fprintln(w, "egress as intended")
	} else {
		fmt.Fprintln(w, "egress is NOT as intended")
	}
	return ok
}

// ParseDeny reads EnvDeny: host:port destinations separated by spaces or commas.
func ParseDeny(text string) ([]string, error) {
	var out []string
	for _, f := range strings.FieldsFunc(text, func(r rune) bool { return r == ',' || r == ' ' || r == '\n' }) {
		host, port, err := net.SplitHostPort(f)
		if err != nil || host == "" {
			return nil, fmt.Errorf("%s: %q is not host:port", EnvDeny, f)
		}
		if p, err := strconv.Atoi(port); err != nil || p < 1 || p > 65535 {
			return nil, fmt.Errorf("%s: %q has no TCP port", EnvDeny, f)
		}
		out = append(out, f)
	}
	return out, nil
}

func dialTimeout(ctx context.Context, network, address string, d time.Duration) (net.Conn, error) {
	ctx, cancel := context.WithTimeout(ctx, d)
	defer cancel()
	var dialer net.Dialer
	return dialer.DialContext(ctx, network, address)
}

// tcpProbe connects and waits: a destination that answers or keeps the connection open is open; one that refuses,
// resets or closes at once (as the proxy does to what it refuses) is blocked.
func tcpProbe(ctx context.Context, target string) string {
	c, err := dialTimeout(ctx, "tcp", target, 4*time.Second)
	if err != nil {
		return Blocked
	}
	defer c.Close()
	_ = c.SetReadDeadline(time.Now().Add(1500 * time.Millisecond))
	buf := make([]byte, 1)
	_, err = c.Read(buf)
	var ne net.Error
	if err == nil || errors.As(err, &ne) && ne.Timeout() {
		return Open
	}
	return Blocked
}

// tlsProbe completes a TLS handshake with name, the certificate checked.
func tlsProbe(ctx context.Context, address, name string) string {
	ctx, cancel := context.WithTimeout(ctx, 8*time.Second)
	defer cancel()
	d := tls.Dialer{Config: &tls.Config{ServerName: name, MinVersion: tls.VersionTLS12}}
	c, err := d.DialContext(ctx, "tcp", address)
	if err != nil {
		return Blocked
	}
	c.Close()
	return Open
}

// dnsDirect asks server for name's A record, past the pod's resolv.conf. Open means an address came back.
func dnsDirect(ctx context.Context, server, name string) string {
	c, err := dialTimeout(ctx, "udp", server, 3*time.Second)
	if err != nil {
		return Blocked
	}
	defer c.Close()
	q := []byte{0x13, 0x37, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0}
	for _, l := range strings.Split(name, ".") {
		q = append(q, byte(len(l)))
		q = append(q, l...)
	}
	q = append(q, 0, 0, 1, 0, 1)
	_ = c.SetDeadline(time.Now().Add(3 * time.Second))
	if _, err := c.Write(q); err != nil {
		return Blocked
	}
	buf := make([]byte, 1500)
	n, err := c.Read(buf)
	if err != nil || n < 12 {
		return Blocked
	}
	if buf[3]&0xF == 0 && binary.BigEndian.Uint16(buf[6:8]) > 0 {
		return Open
	}
	return Blocked
}

// nft tries to read or change the rules as the app would. Blocked means it was refused.
func nft(ctx context.Context, args ...string) string {
	ctx, cancel := context.WithTimeout(ctx, 5*time.Second)
	defer cancel()
	if err := exec.CommandContext(ctx, "nft", args...).Run(); err != nil {
		return Blocked
	}
	return Open
}

// Control reads the control probe's dial. A connection that completed was intercepted, and so was one reset: a reset
// comes only after the handshake, and the proxy accepts and resets what it refuses, so on a fast loopback the reset
// can reach the socket before the dial has looked at it (seen on podman 4.9, where Go's connect then reports
// ECONNRESET). Anything else (a timeout, an unreachable network, a refusal) means nothing answered for the
// address, so the check is not behind the egress.
func Control(err error) string {
	if err == nil || errors.Is(err, syscall.ECONNRESET) {
		return Intercepted
	}
	// The error is kept in the line, so a failure says what the dial met.
	return "not " + Intercepted + " (" + err.Error() + ")"
}
