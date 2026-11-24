// Package proxy is where every IPv4 TCP connection the app makes lands, whatever address it dialled. A connection
// to one of the deployment's own services (a TCP entry) is passed through when the address dialled is one that
// service's name resolves to now. Any other connection must open with a TLS ClientHello naming a domain on the
// list for that port; the proxy then resolves that name itself and connects to a public address it resolves to,
// not to the address the app dialled, so an allowed name cannot be used to reach another host that shares its
// CDN or to reach the LAN. Everything else is reset.
package proxy

import (
	"context"
	"errors"
	"fmt"
	"io"
	"net"
	"net/netip"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/TraxSharp/Trax/Trax.Egress/internal/addr"
	"github.com/TraxSharp/Trax/Trax.Egress/internal/allow"
)

var errLoopback = errors.New("a loopback address is not dialled through the egress")

// MaxConns bounds the connections held at once; past it a new one is reset.
const MaxConns = 4096

// Proxy decides and carries connections.
type Proxy struct {
	List allow.List
	// Resolve looks a name up over IPv4, through the pod's upstream (not through the egress resolver's policy).
	Resolve func(ctx context.Context, host string) ([]netip.Addr, error)
	// Dial connects to the destination.
	Dial func(ctx context.Context, network, address string) (net.Conn, error)
	// OriginalDst is the address the app dialled before the redirect.
	OriginalDst func(c net.Conn) (netip.AddrPort, error)
	// Log takes one line about each refusal.
	Log func(format string, args ...any)
	// HelloTimeout is how long a client has to send its ClientHello.
	HelloTimeout time.Duration
	// DialTimeout bounds the connection to the destination.
	DialTimeout time.Duration
}

// Serve accepts connections on l until ctx ends.
func (p *Proxy) Serve(ctx context.Context, l net.Listener) error {
	slots := make(chan struct{}, MaxConns)
	for {
		c, err := l.Accept()
		if err != nil {
			if ctx.Err() != nil {
				return nil
			}
			var ne net.Error
			if errors.As(err, &ne) && ne.Timeout() {
				continue
			}
			return err
		}
		select {
		case slots <- struct{}{}:
		default:
			p.Log("refused a connection: %d are open", MaxConns)
			reset(c)
			continue
		}
		go func() {
			defer func() { <-slots }()
			p.Handle(ctx, c)
		}()
	}
}

// Handle decides one connection and, when it is allowed, carries it until either side closes.
func (p *Proxy) Handle(ctx context.Context, client net.Conn) {
	upstream, prefix, err := p.Connect(ctx, client)
	if err != nil {
		// A connection made straight to the proxy (the readiness probe's) is closed without a word.
		if !errors.Is(err, errLoopback) {
			p.Log("refused: %v", err)
		}
		reset(client)
		return
	}
	if len(prefix) > 0 {
		if _, err := upstream.Write(prefix); err != nil {
			reset(client)
			upstream.Close()
			return
		}
	}
	splice(client, upstream)
}

// Connect decides where client may go and connects there. It returns the connection and any bytes already read
// from the client, which must be sent on first.
func (p *Proxy) Connect(ctx context.Context, client net.Conn) (net.Conn, []byte, error) {
	dst, err := p.OriginalDst(client)
	if err != nil {
		return nil, nil, fmt.Errorf("no original destination: %v", err)
	}
	dst = netip.AddrPortFrom(dst.Addr().Unmap(), dst.Port())
	if dst.Addr().IsLoopback() || dst.Addr().IsUnspecified() {
		return nil, nil, fmt.Errorf("%s: %w", dst, errLoopback)
	}
	port := int(dst.Port())

	if p.tcpAllowed(ctx, dst) {
		c, err := p.dial(ctx, dst)
		if err != nil {
			return nil, nil, fmt.Errorf("%s: %v", dst, err)
		}
		return c, nil, nil
	}

	if !p.anyTLS(port) {
		return nil, nil, fmt.Errorf("%s: nothing on the list for port %d", dst, port)
	}
	_ = client.SetReadDeadline(time.Now().Add(p.HelloTimeout))
	name, hello, err := ReadClientHello(client)
	_ = client.SetReadDeadline(time.Time{})
	if err != nil {
		return nil, nil, fmt.Errorf("%s: %v", dst, err)
	}
	if _, ok := p.List.TLSFor(name, port); !ok {
		return nil, nil, fmt.Errorf("%s: %q is not on the list for port %d", dst, name, port)
	}
	addrs, err := p.Resolve(ctx, absolute(name))
	if err != nil {
		return nil, nil, fmt.Errorf("%q: %v", name, err)
	}
	var last error = fmt.Errorf("%q resolves to no public address", name)
	for _, a := range addrs {
		if !addr.Public(a) {
			continue
		}
		c, err := p.dial(ctx, netip.AddrPortFrom(a.Unmap(), dst.Port()))
		if err == nil {
			return c, hello, nil
		}
		last = err
	}
	return nil, nil, fmt.Errorf("%q: %v", name, last)
}

func (p *Proxy) dial(ctx context.Context, to netip.AddrPort) (net.Conn, error) {
	ctx, cancel := context.WithTimeout(ctx, p.DialTimeout)
	defer cancel()
	return p.Dial(ctx, "tcp", to.String())
}

func (p *Proxy) anyTLS(port int) bool {
	for _, e := range p.List.Entries {
		if e.Kind == allow.TLS && e.Port == port {
			return true
		}
	}
	return false
}

// tcpAllowed says whether dst is a TCP entry's address now: a fixed address, or one its name resolves to.
func (p *Proxy) tcpAllowed(ctx context.Context, dst netip.AddrPort) bool {
	for _, e := range p.List.TCPFor(int(dst.Port())) {
		if e.Addr.IsValid() {
			if e.Addr == dst.Addr() {
				return true
			}
			continue
		}
		addrs, err := p.Resolve(ctx, absolute(e.Host))
		if err != nil {
			continue
		}
		for _, a := range addrs {
			if a.Unmap() == dst.Addr() {
				return true
			}
		}
	}
	return false
}

// absolute marks a name with a dot as fully qualified, so the lookup does not try it under every search domain
// first. A short name ("database") is left to the search domains, which is how the pod finds its own services.
func absolute(host string) string {
	if strings.Contains(host, ".") {
		return host + "."
	}
	return host
}

func splice(a, b net.Conn) {
	var wg sync.WaitGroup
	wg.Add(2)
	copyHalf := func(dst, src net.Conn) {
		defer wg.Done()
		_, _ = io.Copy(dst, src)
		if t, ok := dst.(*net.TCPConn); ok {
			_ = t.CloseWrite()
		} else {
			_ = dst.Close()
		}
	}
	go copyHalf(a, b)
	go copyHalf(b, a)
	wg.Wait()
	a.Close()
	b.Close()
}

// reset closes c with a TCP reset, so the client sees the refusal at once rather than a close it might read as an
// empty answer.
func reset(c net.Conn) {
	if t, ok := c.(*net.TCPConn); ok {
		_ = t.SetLinger(0)
	}
	c.Close()
}

// Listen opens the proxy's socket on 127.0.0.1.
func Listen(port int) (net.Listener, error) {
	return net.Listen("tcp4", net.JoinHostPort("127.0.0.1", strconv.Itoa(port)))
}
