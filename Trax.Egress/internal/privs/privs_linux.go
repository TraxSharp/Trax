// Package privs gives up root and every capability once the rules are applied, for the compose sidecar, which
// applies them and then serves in the same container.
package privs

import (
	"bufio"
	"fmt"
	"os"
	"strings"
	"syscall"
)

// Drop moves the process (every thread of it) to uid and gid with no supplementary groups. Leaving uid 0 clears
// the permitted and effective capability sets, and Drop checks that they are empty before it returns.
func Drop(uid, gid int) error {
	if err := syscall.Setgroups([]int{}); err != nil {
		return fmt.Errorf("setgroups: %w", err)
	}
	if err := syscall.Setresgid(gid, gid, gid); err != nil {
		return fmt.Errorf("setresgid: %w", err)
	}
	if err := syscall.Setresuid(uid, uid, uid); err != nil {
		return fmt.Errorf("setresuid: %w", err)
	}
	return NoCapabilities()
}

// NoCapabilities fails unless the process holds no capability, permitted or effective.
func NoCapabilities() error {
	f, err := os.Open("/proc/self/status")
	if err != nil {
		return err
	}
	defer f.Close()
	sc := bufio.NewScanner(f)
	for sc.Scan() {
		key, value, ok := strings.Cut(sc.Text(), ":")
		if !ok || (key != "CapPrm" && key != "CapEff") {
			continue
		}
		if strings.Trim(strings.TrimSpace(value), "0") != "" {
			return fmt.Errorf("%s is %s after dropping root", key, strings.TrimSpace(value))
		}
	}
	return sc.Err()
}
