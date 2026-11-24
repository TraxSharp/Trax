// Command trax-egress holds a pod's outbound traffic to a list of names. It is one image with five commands:
//
//	rules   apply the nftables rules to the pod's network namespace and exit (needs NET_ADMIN, runs as root)
//	serve   run the resolver and the proxy (runs as the egress uid, no capabilities)
//	run     rules, then give up root and every capability, then serve (for compose, where there is no init container)
//	ready   exit 0 when the resolver and the proxy are listening (a startup probe or a healthcheck)
//	check   probe from inside the namespace that the list holds (run as the app's uid, never the egress's)
//
// The list is read from TRAX_EGRESS_ALLOW, TRAX_EGRESS_ALLOW_FILE and TRAX_EGRESS_POSTGRES by every command, so
// the rules, the sidecar and the check cannot disagree about it.
package main

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"net"
	"net/netip"
	"os"
	"os/signal"
	"strconv"
	"syscall"
	"time"

	"github.com/TraxSharp/Trax/Trax.Egress/internal/allow"
	"github.com/TraxSharp/Trax/Trax.Egress/internal/check"
	"github.com/TraxSharp/Trax/Trax.Egress/internal/logx"
	"github.com/TraxSharp/Trax/Trax.Egress/internal/privs"
	"github.com/TraxSharp/Trax/Trax.Egress/internal/proxy"
	"github.com/TraxSharp/Trax/Trax.Egress/internal/resolvconf"
	"github.com/TraxSharp/Trax/Trax.Egress/internal/resolver"
	"github.com/TraxSharp/Trax/Trax.Egress/internal/rules"
)

// The egress's own uid and gid, and the loopback ports its resolver and proxy listen on. trax deploy renders the
// same numbers; an app must not run as this uid, since the rules let it out.
const (
	UID       = 61000
	GID       = 61000
	DNSPort   = 15053
	ProxyPort = 15001
)

// version is set at build time.
var version = "dev"

func main() {
	if len(os.Args) < 2 {
		usage()
		os.Exit(2)
	}
	var err error
	switch os.Args[1] {
	case "rules":
		err = applyRules()
	case "serve":
		err = serve()
	case "run":
		err = run()
	case "ready":
		err = ready()
	case "check":
		err = runCheck()
	case "version":
		fmt.Println(version)
	default:
		usage()
		os.Exit(2)
	}
	if err != nil {
		fmt.Fprintf(os.Stderr, "trax-egress %s: %v\n", os.Args[1], err)
		os.Exit(1)
	}
}

func usage() {
	fmt.Fprintln(os.Stderr, "usage: trax-egress rules|serve|run|ready|check|version")
}

func applyRules() error {
	list, err := allow.Read()
	if err != nil {
		return err
	}
	rc, err := resolvconf.Read()
	if err != nil {
		return err
	}
	script, err := rules.Render(rules.Config{
		UID:         UID,
		DNSPort:     DNSPort,
		ProxyPort:   ProxyPort,
		Nameservers: rc.Nameservers,
		Pinned:      list.Pinned(),
	})
	if err != nil {
		return err
	}
	var out bytes.Buffer
	if err := rules.Apply(script, &out); err != nil {
		return err
	}
	fmt.Fprintf(os.Stderr, "trax-egress: rules applied for %d entries\n%s", len(list.Entries), out.String())
	for _, e := range list.Entries {
		fmt.Fprintf(os.Stderr, "trax-egress: allow %s\n", e)
	}
	return nil
}

func run() error {
	if err := applyRules(); err != nil {
		return err
	}
	if err := privs.Drop(UID, GID); err != nil {
		return fmt.Errorf("could not give up root: %w", err)
	}
	return serve()
}

func serve() error {
	if os.Getuid() != UID {
		return fmt.Errorf("serve runs as uid %d, the one the rules let out; it is running as %d", UID, os.Getuid())
	}
	if err := privs.NoCapabilities(); err != nil {
		return err
	}
	list, err := allow.Read()
	if err != nil {
		return err
	}
	rc, err := resolvconf.Read()
	if err != nil {
		return err
	}
	log := &logx.Limited{PerSecond: 20, Out: os.Stderr, Now: time.Now}

	policy := resolver.Policy{Names: map[string]bool{}, Short: map[string]bool{}, Search: rc.Search}
	for _, n := range list.Names() {
		policy.Names[n] = true
	}
	for _, e := range list.Entries {
		if e.Kind == allow.TCP && e.Host != "" {
			policy.Short[e.Host] = true
		}
	}
	dns := &resolver.Resolver{
		Policy:   policy,
		Upstream: resolver.Nameservers{Addrs: rc.Nameservers, Timeout: 3 * time.Second},
		Refused:  func(name string) { log.Printf("refused to resolve %q", name) },
	}
	lookup := &net.Resolver{PreferGo: true}
	p := &proxy.Proxy{
		List: list,
		Resolve: func(ctx context.Context, host string) ([]netip.Addr, error) {
			return lookup.LookupNetIP(ctx, "ip4", host)
		},
		Dial: func(ctx context.Context, network, address string) (net.Conn, error) {
			var d net.Dialer
			return d.DialContext(ctx, network, address)
		},
		OriginalDst:  proxy.OriginalDst,
		Log:          log.Printf,
		HelloTimeout: 10 * time.Second,
		DialTimeout:  10 * time.Second,
	}

	ctx, stop := signal.NotifyContext(context.Background(), syscall.SIGTERM, syscall.SIGINT)
	defer stop()
	pcs, dls, err := resolver.Listen(DNSPort)
	if err != nil {
		return err
	}
	pl, err := proxy.Listen(ProxyPort)
	if err != nil {
		return err
	}
	errs := make(chan error, 2*len(pcs)+1)
	for i := range pcs {
		pc, l := pcs[i], dls[i]
		go func() { errs <- dns.ServeUDP(ctx, pc) }()
		go func() { errs <- dns.ServeTCP(ctx, l) }()
	}
	go func() { errs <- p.Serve(ctx, pl) }()
	fmt.Fprintf(os.Stderr, "trax-egress %s: serving %d entries (resolver :%d, proxy :%d)\n",
		version, len(list.Entries), DNSPort, ProxyPort)
	select {
	case <-ctx.Done():
	case err = <-errs:
	}
	for _, pc := range pcs {
		pc.Close()
	}
	for _, l := range dls {
		l.Close()
	}
	pl.Close()
	return err
}

func ready() error {
	for _, port := range []int{DNSPort, ProxyPort} {
		c, err := net.DialTimeout("tcp4", net.JoinHostPort("127.0.0.1", strconv.Itoa(port)), time.Second)
		if err != nil {
			return fmt.Errorf("nothing listens on %d yet", port)
		}
		c.Close()
	}
	return nil
}

func runCheck() error {
	if os.Getuid() == UID {
		return errors.New("the check must run as the app does; the egress's own uid is let out and would prove nothing")
	}
	list, err := allow.Read()
	if err != nil {
		return err
	}
	deny, err := check.ParseDeny(os.Getenv(check.EnvDeny))
	if err != nil {
		return err
	}
	results := check.Run(context.Background(), check.Probes(list, deny))
	if !check.Report(os.Stdout, results) {
		return errors.New("a probe did not do what it must")
	}
	return nil
}
