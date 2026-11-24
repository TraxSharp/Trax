package proxy

import (
	"errors"
	"net"
	"net/netip"
	"syscall"
)

// soOriginalDst is SO_ORIGINAL_DST from linux/netfilter_ipv4.h: the destination a connection had before nat
// redirected it.
const soOriginalDst = 80

// OriginalDst reads the address an IPv4 TCP connection was dialled to before the rules redirected it to the proxy.
func OriginalDst(c net.Conn) (netip.AddrPort, error) {
	t, ok := c.(*net.TCPConn)
	if !ok {
		return netip.AddrPort{}, errors.New("not a TCP connection")
	}
	raw, err := t.SyscallConn()
	if err != nil {
		return netip.AddrPort{}, err
	}
	var out netip.AddrPort
	var inner error
	err = raw.Control(func(fd uintptr) {
		// sockaddr_in is 16 bytes; IPv6Mreq is the one getsockopt wrapper that returns that many.
		m, e := syscall.GetsockoptIPv6Mreq(int(fd), syscall.IPPROTO_IP, soOriginalDst)
		if e != nil {
			inner = e
			return
		}
		b := m.Multiaddr
		if uint16(b[0])|uint16(b[1])<<8 != syscall.AF_INET {
			inner = errors.New("not an IPv4 destination")
			return
		}
		port := uint16(b[2])<<8 | uint16(b[3])
		out = netip.AddrPortFrom(netip.AddrFrom4([4]byte{b[4], b[5], b[6], b[7]}), port)
	})
	if err != nil {
		return netip.AddrPort{}, err
	}
	return out, inner
}
