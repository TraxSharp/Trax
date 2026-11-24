package proxy

import (
	"encoding/binary"
	"errors"
	"io"
	"strings"

	"github.com/TraxSharp/Trax/Trax.Egress/internal/allow"
)

const (
	recordHandshake      = 22
	handshakeHello       = 1
	extServerName        = 0
	maxRecord            = 16384 + 2048
	maxHello             = 64 * 1024
	sniHostName     byte = 0
)

var errNotHello = errors.New("not a TLS ClientHello")

// ReadClientHello reads a TLS ClientHello from r, across as many records as it spans, and returns the name it asks
// for (its SNI) with every byte read, which the proxy sends on unchanged. A hello with no name, two names, or a name
// that is not a domain is refused: the proxy has nothing else to decide on.
func ReadClientHello(r io.Reader) (string, []byte, error) {
	var raw, hs []byte
	for {
		var h [5]byte
		if _, err := io.ReadFull(r, h[:]); err != nil {
			return "", raw, errNotHello
		}
		raw = append(raw, h[:]...)
		n := int(binary.BigEndian.Uint16(h[3:5]))
		if h[0] != recordHandshake || h[1] != 3 || n == 0 || n > maxRecord {
			return "", raw, errNotHello
		}
		body := make([]byte, n)
		if _, err := io.ReadFull(r, body); err != nil {
			return "", raw, errNotHello
		}
		raw = append(raw, body...)
		hs = append(hs, body...)
		if len(hs) > maxHello {
			return "", raw, errNotHello
		}
		if len(hs) < 4 {
			continue
		}
		if hs[0] != handshakeHello {
			return "", raw, errNotHello
		}
		want := int(hs[1])<<16 | int(hs[2])<<8 | int(hs[3])
		if want > maxHello {
			return "", raw, errNotHello
		}
		if len(hs)-4 >= want {
			name, err := serverName(hs[4 : 4+want])
			return name, raw, err
		}
	}
}

// serverName reads the one host_name of a ClientHello body.
func serverName(b []byte) (string, error) {
	s := cursor{b: b}
	s.skip(2 + 32)  // version, random
	s.skip(s.u8())  // session id
	s.skip(s.u16()) // cipher suites
	s.skip(s.u8())  // compression methods
	exts := s.take(s.u16())
	if s.bad {
		return "", errNotHello
	}
	e := cursor{b: exts}
	name := ""
	seen := false
	for len(e.b) > 0 && !e.bad {
		typ := e.u16()
		data := e.take(e.u16())
		if typ != extServerName {
			continue
		}
		if seen {
			return "", errors.New("two server_name extensions")
		}
		seen = true
		list := cursor{b: data}
		entries := cursor{b: list.take(list.u16())}
		for len(entries.b) > 0 && !entries.bad {
			kind := entries.u8()
			host := entries.take(entries.u16())
			if byte(kind) != sniHostName {
				continue
			}
			if name != "" {
				return "", errors.New("two host names")
			}
			name = string(host)
		}
		if list.bad || entries.bad {
			return "", errNotHello
		}
	}
	if e.bad {
		return "", errNotHello
	}
	if name == "" {
		return "", errors.New("no server name")
	}
	name = strings.TrimSuffix(strings.ToLower(name), ".")
	if !allow.ValidName(name, 2) {
		return "", errors.New("the server name is not a domain")
	}
	return name, nil
}

type cursor struct {
	b   []byte
	bad bool
}

func (c *cursor) take(n int) []byte {
	if c.bad || n < 0 || n > len(c.b) {
		c.bad = true
		return nil
	}
	out := c.b[:n]
	c.b = c.b[n:]
	return out
}

func (c *cursor) skip(n int) { c.take(n) }

func (c *cursor) u8() int {
	b := c.take(1)
	if b == nil {
		return 0
	}
	return int(b[0])
}

func (c *cursor) u16() int {
	b := c.take(2)
	if b == nil {
		return 0
	}
	return int(binary.BigEndian.Uint16(b))
}
