// Package resolvconf reads the pod's /etc/resolv.conf: the upstream the resolver forwards allowed names to, and the
// search domains a short name such as "database" is tried under.
package resolvconf

import (
	"bufio"
	"errors"
	"net/netip"
	"os"
	"strings"
)

// Path is where the runtime writes the pod's resolver configuration.
const Path = "/etc/resolv.conf"

// Config is what the egress reads from resolv.conf.
type Config struct {
	Nameservers []netip.Addr
	Search      []string
}

// Parse reads nameserver and search lines; everything else is ignored.
func Parse(text string) (Config, error) {
	var c Config
	sc := bufio.NewScanner(strings.NewReader(text))
	for sc.Scan() {
		fields := strings.Fields(sc.Text())
		if len(fields) == 0 || strings.HasPrefix(fields[0], "#") || strings.HasPrefix(fields[0], ";") {
			continue
		}
		switch fields[0] {
		case "nameserver":
			if len(fields) < 2 {
				continue
			}
			a, err := netip.ParseAddr(fields[1])
			if err != nil {
				continue
			}
			c.Nameservers = append(c.Nameservers, a.WithZone("").Unmap())
		case "search", "domain":
			c.Search = nil
			for _, s := range fields[1:] {
				s = strings.ToLower(strings.TrimSuffix(s, "."))
				if s != "" {
					c.Search = append(c.Search, s)
				}
			}
		}
	}
	if len(c.Nameservers) == 0 {
		return Config{}, errors.New("resolv.conf names no nameserver")
	}
	return c, nil
}

// Read parses the pod's resolv.conf.
func Read() (Config, error) {
	data, err := os.ReadFile(Path)
	if err != nil {
		return Config{}, err
	}
	return Parse(string(data))
}
