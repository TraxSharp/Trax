// Package addr says which addresses count as the public internet. A name on the allowlist is reached only at a
// public address, so a record that points an allowed name at the LAN, the tailnet or the cloud's metadata service
// does not open a way there.
package addr

import "net/netip"

var notPublic = []netip.Prefix{
	netip.MustParsePrefix("0.0.0.0/8"),       // this network
	netip.MustParsePrefix("10.0.0.0/8"),      // private
	netip.MustParsePrefix("100.64.0.0/10"),   // carrier-grade NAT, the tailnet
	netip.MustParsePrefix("127.0.0.0/8"),     // loopback
	netip.MustParsePrefix("169.254.0.0/16"),  // link-local, cloud metadata
	netip.MustParsePrefix("172.16.0.0/12"),   // private
	netip.MustParsePrefix("192.0.0.0/24"),    // IETF protocol assignments
	netip.MustParsePrefix("192.0.2.0/24"),    // documentation
	netip.MustParsePrefix("192.168.0.0/16"),  // private
	netip.MustParsePrefix("198.18.0.0/15"),   // benchmarking
	netip.MustParsePrefix("198.51.100.0/24"), // documentation
	netip.MustParsePrefix("203.0.113.0/24"),  // documentation
	netip.MustParsePrefix("224.0.0.0/3"),     // multicast and reserved
	netip.MustParsePrefix("::/128"),
	netip.MustParsePrefix("::1/128"),
	netip.MustParsePrefix("64:ff9b:1::/48"), // local-use NAT64
	netip.MustParsePrefix("100::/64"),       // discard
	netip.MustParsePrefix("2001:db8::/32"),  // documentation
	netip.MustParsePrefix("fc00::/7"),       // unique local, the tailnet's IPv6
	netip.MustParsePrefix("fe80::/10"),      // link-local
	netip.MustParsePrefix("ff00::/8"),       // multicast
}

// Public says whether a is an address on the public internet. An IPv4 address mapped into IPv6 is judged as IPv4.
func Public(a netip.Addr) bool {
	if !a.IsValid() {
		return false
	}
	a = a.Unmap()
	for _, p := range notPublic {
		if p.Contains(a) {
			return false
		}
	}
	return true
}
