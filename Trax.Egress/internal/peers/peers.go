// Package peers finds the processes in the pod, other than the egress's own, that run as the egress uid. The rules
// let that uid out unredirected, so such a process would reach anything; serve refuses to start while there is one,
// and stops when one appears.
//
// It sees the rest of the pod only when the pod shares one PID namespace, which is how trax deploy renders a held
// service (shareProcessNamespace on Kubernetes and podman, the sidecar's PID namespace on compose). A process is the
// egress's own when it is in the sidecar's mount namespace: serve itself and the probes run in its container
// (`trax-egress ready`). Every other container has a mount namespace of its own, and entering the sidecar's takes
// CAP_SYS_ADMIN.
package peers

import (
	"context"
	"errors"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"syscall"
	"time"
)

// Process is a thread running as the egress uid outside the egress's own container.
type Process struct {
	PID  int
	TID  int
	Name string
	// Uids are the thread's real, effective, saved and filesystem uids.
	Uids [4]int
}

func (p Process) String() string {
	return fmt.Sprintf("pid %d (%s, thread %d, uids %d %d %d %d)", p.PID, p.Name, p.TID, p.Uids[0], p.Uids[1], p.Uids[2], p.Uids[3])
}

// Foreign lists every thread under proc that runs as uid (in any of its real, effective, saved or filesystem ids)
// and is not in self's mount namespace. self is the caller's pid; its own threads are skipped. A thread whose mount
// namespace cannot be read counts as foreign, so a process that makes itself unreadable is not let through. A
// process that exits while it is read is skipped.
func Foreign(proc string, uid int, self int) ([]Process, error) {
	own, err := os.Readlink(filepath.Join(proc, strconv.Itoa(self), "ns", "mnt"))
	if err != nil {
		return nil, fmt.Errorf("could not read the egress's own mount namespace: %w", err)
	}
	entries, err := os.ReadDir(proc)
	if err != nil {
		return nil, err
	}
	var found []Process
	for _, e := range entries {
		pid, err := strconv.Atoi(e.Name())
		if err != nil || pid == self {
			continue
		}
		tasks, err := os.ReadDir(filepath.Join(proc, e.Name(), "task"))
		if errors.Is(err, fs.ErrNotExist) || errors.Is(err, syscall.ESRCH) {
			continue
		}
		if err != nil {
			return nil, err
		}
		for _, t := range tasks {
			tid, err := strconv.Atoi(t.Name())
			if err != nil {
				continue
			}
			dir := filepath.Join(proc, e.Name(), "task", t.Name())
			status, err := os.ReadFile(filepath.Join(dir, "status"))
			if errors.Is(err, fs.ErrNotExist) || errors.Is(err, syscall.ESRCH) {
				continue
			}
			if err != nil {
				return nil, err
			}
			name, uids, ok := parseStatus(string(status))
			if !ok {
				return nil, fmt.Errorf("could not read the uids of pid %d thread %d", pid, tid)
			}
			if uids[0] != uid && uids[1] != uid && uids[2] != uid && uids[3] != uid {
				continue
			}
			ns, err := os.Readlink(filepath.Join(dir, "ns", "mnt"))
			if errors.Is(err, fs.ErrNotExist) || errors.Is(err, syscall.ESRCH) {
				// Gone, or a zombie: it holds no socket.
				if _, gone := os.Stat(dir); gone != nil {
					continue
				}
			}
			if err == nil && ns == own {
				continue
			}
			found = append(found, Process{PID: pid, TID: tid, Name: name, Uids: uids})
		}
	}
	return found, nil
}

func parseStatus(status string) (string, [4]int, bool) {
	var name string
	var uids [4]int
	seen := false
	for _, line := range strings.Split(status, "\n") {
		key, value, ok := strings.Cut(line, ":")
		if !ok {
			continue
		}
		switch key {
		case "Name":
			name = strings.TrimSpace(value)
		case "Uid":
			fields := strings.Fields(value)
			if len(fields) != 4 {
				return "", uids, false
			}
			for i, f := range fields {
				n, err := strconv.Atoi(f)
				if err != nil {
					return "", uids, false
				}
				uids[i] = n
			}
			seen = true
		}
	}
	return name, uids, seen
}

// Watch runs scan every interval until ctx ends, and returns an error as soon as scan finds a process or fails.
func Watch(ctx context.Context, every time.Duration, scan func() ([]Process, error)) error {
	t := time.NewTicker(every)
	defer t.Stop()
	for {
		select {
		case <-ctx.Done():
			return nil
		case <-t.C:
			if err := Refuse(scan); err != nil {
				return err
			}
		}
	}
}

// Refuse runs scan once and returns an error naming what it found, or the scan's own error.
func Refuse(scan func() ([]Process, error)) error {
	found, err := scan()
	if err != nil {
		return fmt.Errorf("could not check the pod's processes for the egress uid: %w", err)
	}
	if len(found) == 0 {
		return nil
	}
	names := make([]string, len(found))
	for i, p := range found {
		names[i] = p.String()
	}
	return fmt.Errorf("another process in the pod runs as the egress uid, which the rules let out unchecked: %s",
		strings.Join(names, "; "))
}
