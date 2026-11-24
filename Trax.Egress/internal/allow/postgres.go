package allow

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
)

// DefaultPostgresPort is the port a host without one is reached on, as Npgsql assumes.
const DefaultPostgresPort = 5432

// PostgresHosts reads the hosts of an Npgsql connection string (Host or Server, a comma-separated list, each with an
// optional :port, and Port as the default) as TCP entries. A Unix socket path is left out: it is not network
// egress. Nothing else in the string is kept, the password included.
func PostgresHosts(connectionString string) ([]Entry, error) {
	var hosts string
	port := DefaultPostgresPort
	for _, part := range strings.Split(connectionString, ";") {
		key, value, ok := strings.Cut(part, "=")
		if !ok {
			continue
		}
		value = strings.Trim(strings.TrimSpace(value), "\"'")
		switch strings.ToLower(strings.TrimSpace(key)) {
		case "host", "server":
			hosts = value
		case "port":
			p, err := strconv.Atoi(value)
			if err != nil || p < 1 || p > 65535 {
				return nil, fmt.Errorf("port %q is not a TCP port", value)
			}
			port = p
		}
	}
	if hosts == "" {
		return nil, errors.New("the connection string names no host")
	}
	var entries []Entry
	for _, h := range strings.Split(hosts, ",") {
		h = strings.ToLower(strings.TrimSpace(h))
		if h == "" || strings.HasPrefix(h, "/") || strings.HasPrefix(h, "@") {
			continue
		}
		text := h
		if !hasPort(h) {
			if strings.Count(h, ":") > 1 && !strings.HasPrefix(h, "[") {
				text = "[" + h + "]"
			}
			text += ":" + strconv.Itoa(port)
		}
		e, err := ParseEntry("tcp:" + text)
		if err != nil {
			return nil, err
		}
		entries = append(entries, e)
	}
	if len(entries) == 0 {
		return nil, errors.New("the connection string names no network host")
	}
	return entries, nil
}

func hasPort(h string) bool {
	if strings.HasPrefix(h, "[") {
		return strings.Contains(h, "]:")
	}
	return strings.Count(h, ":") == 1
}
