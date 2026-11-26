package addr

import (
	"net/netip"
	"testing"
)

func TestPublic(t *testing.T) {
	for _, s := range []string{"1.1.1.1", "160.79.104.10", "2606:4700:4700::1111", "::ffff:8.8.8.8"} {
		if !Public(netip.MustParseAddr(s)) {
			t.Errorf("%s is public", s)
		}
	}
	for _, s := range []string{
		"10.1.2.3", "172.16.0.1", "172.31.255.255", "192.168.1.1", "100.64.0.1", "100.127.255.254",
		"169.254.169.254", "169.254.170.23", "127.0.0.1", "0.0.0.0", "224.0.0.1", "255.255.255.255",
		"192.0.2.1", "198.18.0.1", "::1", "::", "fe80::1", "fd7a:115c:a1e0::1", "ff02::1", "2001:db8::1",
		"::ffff:10.0.0.1", "::ffff:169.254.169.254",
	} {
		if Public(netip.MustParseAddr(s)) {
			t.Errorf("%s is not public", s)
		}
	}
	if Public(netip.Addr{}) {
		t.Error("the zero address is not public")
	}
}
