package resolver

import (
	"context"
	"encoding/binary"
	"errors"
	"io"
	"net"
	"net/netip"
	"strconv"
	"time"
)

// Nameservers asks each upstream in turn: over UDP first, and over TCP when the answer was truncated.
type Nameservers struct {
	Addrs   []netip.Addr
	Timeout time.Duration
}

// Exchange sends query to the first nameserver that answers.
func (n Nameservers) Exchange(ctx context.Context, query []byte) ([]byte, error) {
	var last error = errors.New("no nameserver")
	for _, a := range n.Addrs {
		server := net.JoinHostPort(a.String(), "53")
		resp, err := n.udp(ctx, server, query)
		if err == nil && len(resp) >= 4 && resp[2]&0x02 != 0 {
			resp, err = n.tcp(ctx, server, query)
		}
		if err == nil {
			return resp, nil
		}
		last = err
	}
	return nil, last
}

func (n Nameservers) udp(ctx context.Context, server string, query []byte) ([]byte, error) {
	var d net.Dialer
	ctx, cancel := context.WithTimeout(ctx, n.Timeout)
	defer cancel()
	c, err := d.DialContext(ctx, "udp", server)
	if err != nil {
		return nil, err
	}
	defer c.Close()
	deadline, _ := ctx.Deadline()
	_ = c.SetDeadline(deadline)
	if _, err := c.Write(query); err != nil {
		return nil, err
	}
	buf := make([]byte, 65535)
	for {
		m, err := c.Read(buf)
		if err != nil {
			return nil, err
		}
		// A reply to another id is not this query's; keep reading until the deadline.
		if m >= 2 && binary.BigEndian.Uint16(buf[0:2]) == binary.BigEndian.Uint16(query[0:2]) {
			return append([]byte(nil), buf[:m]...), nil
		}
	}
}

func (n Nameservers) tcp(ctx context.Context, server string, query []byte) ([]byte, error) {
	var d net.Dialer
	ctx, cancel := context.WithTimeout(ctx, n.Timeout)
	defer cancel()
	c, err := d.DialContext(ctx, "tcp", server)
	if err != nil {
		return nil, err
	}
	defer c.Close()
	deadline, _ := ctx.Deadline()
	_ = c.SetDeadline(deadline)
	if err := writeFramed(c, query); err != nil {
		return nil, err
	}
	return readFramed(c)
}

func writeFramed(w io.Writer, msg []byte) error {
	out := make([]byte, 2+len(msg))
	binary.BigEndian.PutUint16(out, uint16(len(msg)))
	copy(out[2:], msg)
	_, err := w.Write(out)
	return err
}

func readFramed(r io.Reader) ([]byte, error) {
	var l [2]byte
	if _, err := io.ReadFull(r, l[:]); err != nil {
		return nil, err
	}
	msg := make([]byte, binary.BigEndian.Uint16(l[:]))
	if _, err := io.ReadFull(r, msg); err != nil {
		return nil, err
	}
	return msg, nil
}

// MaxInFlight bounds the queries answered at once.
const MaxInFlight = 256

// ServeUDP answers queries on pc until ctx ends.
func (r *Resolver) ServeUDP(ctx context.Context, pc net.PacketConn) error {
	slots := make(chan struct{}, MaxInFlight)
	buf := make([]byte, 4096)
	for {
		n, from, err := pc.ReadFrom(buf)
		if err != nil {
			if ctx.Err() != nil {
				return nil
			}
			return err
		}
		query := append([]byte(nil), buf[:n]...)
		select {
		case slots <- struct{}{}:
		default:
			continue // Full: the client asks again.
		}
		go func() {
			defer func() { <-slots }()
			if resp := r.Answer(ctx, query, true); resp != nil {
				_, _ = pc.WriteTo(resp, from)
			}
		}()
	}
}

// ServeTCP answers queries on l until ctx ends, each connection for at most ten seconds of quiet.
func (r *Resolver) ServeTCP(ctx context.Context, l net.Listener) error {
	slots := make(chan struct{}, MaxInFlight)
	for {
		c, err := l.Accept()
		if err != nil {
			if ctx.Err() != nil {
				return nil
			}
			return err
		}
		select {
		case slots <- struct{}{}:
		default:
			c.Close()
			continue
		}
		go func() {
			defer func() { <-slots }()
			defer c.Close()
			for i := 0; i < 64; i++ {
				_ = c.SetDeadline(time.Now().Add(10 * time.Second))
				query, err := readFramed(c)
				if err != nil {
					return
				}
				resp := r.Answer(ctx, query, false)
				if resp == nil || writeFramed(c, resp) != nil {
					return
				}
			}
		}()
	}
}

// Listen opens the resolver's UDP and TCP sockets on 127.0.0.1 and, when the pod has it, ::1.
func Listen(port int) ([]net.PacketConn, []net.Listener, error) {
	var pcs []net.PacketConn
	var ls []net.Listener
	for i, host := range []string{"127.0.0.1", "::1"} {
		addr := net.JoinHostPort(host, strconv.Itoa(port))
		pc, err := net.ListenPacket("udp", addr)
		if err != nil {
			if i == 0 {
				return nil, nil, err
			}
			continue
		}
		l, err := net.Listen("tcp", addr)
		if err != nil {
			pc.Close()
			if i == 0 {
				return nil, nil, err
			}
			continue
		}
		pcs = append(pcs, pc)
		ls = append(ls, l)
	}
	return pcs, ls, nil
}
