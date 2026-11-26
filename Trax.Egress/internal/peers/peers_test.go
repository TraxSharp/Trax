package peers

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

const (
	egress  = 61000
	ownNS   = "mnt:[4026532001]"
	otherNS = "mnt:[4026532002]"
)

// task writes one thread of a fake /proc: its status, and its mount namespace link unless ns is empty.
func task(t *testing.T, proc string, pid, tid int, name string, uids [4]int, ns string) {
	t.Helper()
	dir := filepath.Join(proc, fmt.Sprint(pid), "task", fmt.Sprint(tid))
	if err := os.MkdirAll(filepath.Join(dir, "ns"), 0o755); err != nil {
		t.Fatal(err)
	}
	status := fmt.Sprintf("Name:\t%s\nUmask:\t0022\nState:\tS (sleeping)\nUid:\t%d\t%d\t%d\t%d\nGid:\t0\t0\t0\t0\n",
		name, uids[0], uids[1], uids[2], uids[3])
	if err := os.WriteFile(filepath.Join(dir, "status"), []byte(status), 0o644); err != nil {
		t.Fatal(err)
	}
	if ns != "" {
		if err := os.Symlink(ns, filepath.Join(dir, "ns", "mnt")); err != nil {
			t.Fatal(err)
		}
	}
}

func self(t *testing.T, proc string, pid int) {
	t.Helper()
	if err := os.MkdirAll(filepath.Join(proc, fmt.Sprint(pid), "ns"), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.Symlink(ownNS, filepath.Join(proc, fmt.Sprint(pid), "ns", "mnt")); err != nil {
		t.Fatal(err)
	}
	task(t, proc, pid, pid, "trax-egress", [4]int{egress, egress, egress, egress}, ownNS)
}

func all(uid int) [4]int { return [4]int{uid, uid, uid, uid} }

func TestForeign(t *testing.T) {
	proc := t.TempDir()
	self(t, proc, 7)
	// The probe the kubelet runs in the sidecar's container: ours.
	task(t, proc, 40, 40, "trax-egress", all(egress), ownNS)
	// The app, as it should be.
	task(t, proc, 12, 12, "dotnet", all(1654), otherNS)
	// The app, as an image whose USER is the egress uid.
	task(t, proc, 13, 13, "dotnet", all(egress), otherNS)
	// A process that switched only its effective uid (a setuid binary).
	task(t, proc, 14, 14, "su", [4]int{1654, egress, egress, egress}, otherNS)
	// A thread that switched alone, its leader still the app's uid.
	task(t, proc, 15, 15, "dotnet", all(1654), otherNS)
	task(t, proc, 15, 16, ".NET Worker", [4]int{egress, egress, egress, egress}, otherNS)
	// One whose namespace cannot be read (a non-dumpable process): foreign, not ours.
	task(t, proc, 17, 17, "hidden", all(egress), "")
	// Not a process.
	if err := os.WriteFile(filepath.Join(proc, "uptime"), []byte("1 1"), 0o644); err != nil {
		t.Fatal(err)
	}

	found, err := Foreign(proc, egress, 7)
	if err != nil {
		t.Fatal(err)
	}
	var got []string
	for _, p := range found {
		got = append(got, fmt.Sprintf("%d/%d", p.PID, p.TID))
	}
	want := "13/13 14/14 15/16 17/17"
	if strings.Join(got, " ") != want {
		t.Errorf("found %v, want %s", got, want)
	}
}

func TestForeignNothing(t *testing.T) {
	proc := t.TempDir()
	self(t, proc, 1)
	task(t, proc, 12, 12, "dotnet", all(1654), otherNS)
	task(t, proc, 20, 20, "pause", all(65535), otherNS)
	found, err := Foreign(proc, egress, 1)
	if err != nil || len(found) != 0 {
		t.Errorf("found %v, %v", found, err)
	}
}

func TestForeignWithoutItsOwnNamespace(t *testing.T) {
	if _, err := Foreign(t.TempDir(), egress, 7); err == nil {
		t.Error("a scan that could not read its own namespace passed")
	}
}

func TestForeignOnThisMachine(t *testing.T) {
	// The real /proc: this test's own process is skipped, and whatever else runs here is not the egress uid.
	if _, err := os.Stat("/proc/self/ns/mnt"); err != nil {
		t.Skip("no /proc here")
	}
	if os.Getuid() == egress {
		t.Skip("running as the egress uid")
	}
	found, err := Foreign("/proc", egress, os.Getpid())
	if err != nil {
		t.Fatal(err)
	}
	if len(found) != 0 {
		t.Errorf("found %v", found)
	}
}

func TestRefuse(t *testing.T) {
	if err := Refuse(func() ([]Process, error) { return nil, nil }); err != nil {
		t.Errorf("nothing found, and %v", err)
	}
	err := Refuse(func() ([]Process, error) {
		return []Process{{PID: 13, TID: 13, Name: "dotnet", Uids: all(egress)}}, nil
	})
	if err == nil || !strings.Contains(err.Error(), "pid 13 (dotnet") {
		t.Errorf("found one, and %v", err)
	}
	if Refuse(func() ([]Process, error) { return nil, fmt.Errorf("no /proc") }) == nil {
		t.Error("a scan that failed passed")
	}
}

func TestWatchStopsWhenOneAppears(t *testing.T) {
	scans := 0
	err := Watch(context.Background(), time.Millisecond, func() ([]Process, error) {
		scans++
		if scans < 3 {
			return nil, nil
		}
		return []Process{{PID: 13, TID: 13, Name: "dotnet", Uids: all(egress)}}, nil
	})
	if err == nil || scans != 3 {
		t.Errorf("after %d scans: %v", scans, err)
	}
}

func TestWatchEndsWithItsContext(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	if err := Watch(ctx, time.Hour, func() ([]Process, error) { return nil, fmt.Errorf("never") }); err != nil {
		t.Error(err)
	}
}
