package proxy

import (
	"bytes"
	"context"
	"crypto/tls"
	"encoding/binary"
	"errors"
	"io"
	"net"
	"net/netip"
	"strings"
	"testing"
	"time"

	"github.com/TraxSharp/Trax/Trax.Egress/internal/allow"
)

// hello captures the ClientHello Go's TLS client sends for name.
func hello(t *testing.T, name string) []byte {
	t.Helper()
	a, b := net.Pipe()
	go func() {
		c := tls.Client(a, &tls.Config{ServerName: name, InsecureSkipVerify: true})
		_ = c.Handshake()
	}()
	_ = b.SetReadDeadline(time.Now().Add(5 * time.Second))
	var buf bytes.Buffer
	h := make([]byte, 5)
	if _, err := io.ReadFull(b, h); err != nil {
		t.Fatal(err)
	}
	body := make([]byte, binary.BigEndian.Uint16(h[3:5]))
	if _, err := io.ReadFull(b, body); err != nil {
		t.Fatal(err)
	}
	buf.Write(h)
	buf.Write(body)
	a.Close()
	b.Close()
	return buf.Bytes()
}

func TestReadClientHelloFindsTheName(t *testing.T) {
	raw := hello(t, "API.Anthropic.com")
	name, got, err := ReadClientHello(bytes.NewReader(raw))
	if err != nil || name != "api.anthropic.com" {
		t.Fatalf("%q %v", name, err)
	}
	if !bytes.Equal(got, raw) {
		t.Fatal("the bytes read are not the bytes sent")
	}
}

func TestReadClientHelloAcrossRecords(t *testing.T) {
	raw := hello(t, "api.anthropic.com")
	body := raw[5:]
	// The same handshake split over two records, as a client may send it.
	var split []byte
	for _, part := range [][]byte{body[:40], body[40:]} {
		split = append(split, 22, 3, 1)
		split = binary.BigEndian.AppendUint16(split, uint16(len(part)))
		split = append(split, part...)
	}
	name, _, err := ReadClientHello(bytes.NewReader(split))
	if err != nil || name != "api.anthropic.com" {
		t.Fatalf("%q %v", name, err)
	}
}

func TestReadClientHelloRefuses(t *testing.T) {
	raw := hello(t, "api.anthropic.com")
	noSNI := func() []byte {
		a, b := net.Pipe()
		go func() {
			c := tls.Client(a, &tls.Config{InsecureSkipVerify: true})
			_ = c.Handshake()
		}()
		h := make([]byte, 5)
		_, _ = io.ReadFull(b, h)
		body := make([]byte, binary.BigEndian.Uint16(h[3:5]))
		_, _ = io.ReadFull(b, body)
		a.Close()
		b.Close()
		return append(h, body...)
	}()
	for name, in := range map[string][]byte{
		"http":        []byte("GET / HTTP/1.1\r\nHost: api.anthropic.com\r\n\r\n"),
		"empty":       {},
		"truncated":   raw[:len(raw)-10],
		"no sni":      noSNI,
		"not hello":   append([]byte{22, 3, 1, 0, 4}, 2, 0, 0, 0),
		"huge record": {22, 3, 1, 0xFF, 0xFF},
		"ip as name":  hello(t, "1.1.1.1"),
	} {
		if n, _, err := ReadClientHello(bytes.NewReader(in)); err == nil {
			t.Errorf("%s: accepted with name %q", name, n)
		}
	}
}

type fakeNet struct {
	dst     netip.AddrPort
	resolve map[string][]netip.Addr
	dialed  []string
	refuse  map[string]bool
}

func (f *fakeNet) proxy(t *testing.T, list string) *Proxy {
	t.Helper()
	l, err := allow.Parse(list)
	if err != nil {
		t.Fatal(err)
	}
	return &Proxy{
		List: l,
		Resolve: func(_ context.Context, host string) ([]netip.Addr, error) {
			if a, ok := f.resolve[host]; ok {
				return a, nil
			}
			return nil, errors.New("no such host")
		},
		Dial: func(_ context.Context, _, address string) (net.Conn, error) {
			f.dialed = append(f.dialed, address)
			if f.refuse[address] {
				return nil, errors.New("refused")
			}
			a, b := net.Pipe()
			go func() { _, _ = io.Copy(io.Discard, b) }()
			return a, nil
		},
		OriginalDst:  func(net.Conn) (netip.AddrPort, error) { return f.dst, nil },
		Log:          func(string, ...any) {},
		HelloTimeout: 2 * time.Second,
		DialTimeout:  2 * time.Second,
	}
}

// connect runs Connect with the client sending first.
func connect(p *Proxy, first []byte) (net.Conn, []byte, error) {
	client, server := net.Pipe()
	go func() {
		if len(first) > 0 {
			_, _ = client.Write(first)
		}
	}()
	defer client.Close()
	return p.Connect(context.Background(), server)
}

func TestAnAllowedNameIsDialledWhereItResolvesNotWhereTheAppDialled(t *testing.T) {
	f := &fakeNet{
		dst:     netip.MustParseAddrPort("104.16.0.1:443"), // another site on the same CDN
		resolve: map[string][]netip.Addr{"api.anthropic.com.": {netip.MustParseAddr("160.79.104.10")}},
	}
	c, prefix, err := connect(f.proxy(t, "api.anthropic.com"), hello(t, "api.anthropic.com"))
	if err != nil {
		t.Fatal(err)
	}
	c.Close()
	if len(prefix) == 0 {
		t.Error("the hello was not handed on")
	}
	if strings.Join(f.dialed, " ") != "160.79.104.10:443" {
		t.Errorf("dialled %v", f.dialed)
	}
}

func TestAnUnlistedNameIsRefused(t *testing.T) {
	f := &fakeNet{dst: netip.MustParseAddrPort("93.184.215.14:443")}
	if _, _, err := connect(f.proxy(t, "api.anthropic.com"), hello(t, "example.com")); err == nil {
		t.Fatal("example.com was let through")
	}
	if len(f.dialed) != 0 {
		t.Errorf("dialled %v", f.dialed)
	}
}

func TestAListedNameOnAnotherPortIsRefused(t *testing.T) {
	f := &fakeNet{
		dst:     netip.MustParseAddrPort("160.79.104.10:8443"),
		resolve: map[string][]netip.Addr{"api.anthropic.com.": {netip.MustParseAddr("160.79.104.10")}},
	}
	if _, _, err := connect(f.proxy(t, "api.anthropic.com registry.npmjs.org:8443"), hello(t, "api.anthropic.com")); err == nil {
		t.Fatal("443's name was let through on 8443")
	}
}

func TestANameResolvingToAPrivateAddressIsRefused(t *testing.T) {
	for _, a := range []string{"169.254.169.254", "10.0.0.5", "192.168.1.1", "100.100.100.100", "127.0.0.1"} {
		f := &fakeNet{
			dst:     netip.MustParseAddrPort("1.2.3.4:443"),
			resolve: map[string][]netip.Addr{"api.anthropic.com.": {netip.MustParseAddr(a)}},
		}
		if _, _, err := connect(f.proxy(t, "api.anthropic.com"), hello(t, "api.anthropic.com")); err == nil {
			t.Errorf("%s was dialled", a)
		}
		if len(f.dialed) != 0 {
			t.Errorf("%s: dialled %v", a, f.dialed)
		}
	}
}

func TestPlainTextToAnAddressIsRefused(t *testing.T) {
	for _, dst := range []string{"10.0.0.1:80", "169.254.169.254:80", "169.254.170.23:80", "192.168.1.1:443", "1.1.1.1:443"} {
		f := &fakeNet{dst: netip.MustParseAddrPort(dst)}
		if _, _, err := connect(f.proxy(t, "api.anthropic.com tcp:database:5432"), []byte("GET / HTTP/1.1\r\n\r\n")); err == nil {
			t.Errorf("%s was let through", dst)
		}
		if len(f.dialed) != 0 {
			t.Errorf("%s: dialled %v", dst, f.dialed)
		}
	}
}

func TestTheDatabaseIsReachedAtWhatItsNameResolvesTo(t *testing.T) {
	f := &fakeNet{
		dst:     netip.MustParseAddrPort("10.244.1.7:5432"),
		resolve: map[string][]netip.Addr{"database": {netip.MustParseAddr("10.244.1.7")}},
	}
	c, prefix, err := connect(f.proxy(t, "tcp:database:5432"), nil)
	if err != nil {
		t.Fatal(err)
	}
	c.Close()
	if len(prefix) != 0 || strings.Join(f.dialed, " ") != "10.244.1.7:5432" {
		t.Errorf("dialled %v prefix %d", f.dialed, len(prefix))
	}
	// Another address on the same port is not the database.
	f.dst = netip.MustParseAddrPort("10.244.1.8:5432")
	f.dialed = nil
	if _, _, err := connect(f.proxy(t, "tcp:database:5432"), nil); err == nil {
		t.Fatal("another host on 5432 was let through")
	}
}

func TestAPinnedAddressIsReachedOnlyOnItsPort(t *testing.T) {
	f := &fakeNet{dst: netip.MustParseAddrPort("169.254.170.23:80")}
	c, _, err := connect(f.proxy(t, "tcp:169.254.170.23:80"), nil)
	if err != nil {
		t.Fatal(err)
	}
	c.Close()
	f.dst = netip.MustParseAddrPort("169.254.170.23:81")
	if _, _, err := connect(f.proxy(t, "tcp:169.254.170.23:80"), nil); err == nil {
		t.Fatal("another port was let through")
	}
	f.dst = netip.MustParseAddrPort("169.254.169.254:80")
	if _, _, err := connect(f.proxy(t, "tcp:169.254.170.23:80"), nil); err == nil {
		t.Fatal("instance metadata was let through")
	}
}

func TestALoopbackDestinationIsRefusedQuietly(t *testing.T) {
	f := &fakeNet{dst: netip.MustParseAddrPort("127.0.0.1:15001")}
	_, _, err := connect(f.proxy(t, "api.anthropic.com"), nil)
	if !errors.Is(err, errLoopback) {
		t.Fatalf("got %v", err)
	}
}

func TestAClientThatSendsNothingTimesOut(t *testing.T) {
	f := &fakeNet{dst: netip.MustParseAddrPort("1.2.3.4:443")}
	p := f.proxy(t, "api.anthropic.com")
	p.HelloTimeout = 100 * time.Millisecond
	client, server := net.Pipe()
	defer client.Close()
	start := time.Now()
	if _, _, err := p.Connect(context.Background(), server); err == nil {
		t.Fatal("a silent client was let through")
	}
	if time.Since(start) > 2*time.Second {
		t.Fatal("the hello deadline was not applied")
	}
}

func TestTheNextAddressIsTriedWhenOneFails(t *testing.T) {
	f := &fakeNet{
		dst: netip.MustParseAddrPort("1.2.3.4:443"),
		resolve: map[string][]netip.Addr{"api.anthropic.com.": {
			netip.MustParseAddr("10.0.0.1"), netip.MustParseAddr("160.79.104.10"), netip.MustParseAddr("160.79.104.11"),
		}},
		refuse: map[string]bool{"160.79.104.10:443": true},
	}
	c, _, err := connect(f.proxy(t, "api.anthropic.com"), hello(t, "api.anthropic.com"))
	if err != nil {
		t.Fatal(err)
	}
	c.Close()
	if strings.Join(f.dialed, " ") != "160.79.104.10:443 160.79.104.11:443" {
		t.Errorf("dialled %v", f.dialed)
	}
}
