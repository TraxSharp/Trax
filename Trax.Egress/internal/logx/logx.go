// Package logx writes the egress's log lines, at most a few a second: what is logged on a refusal is chosen by the
// app being held, and an app that tries a million names must not fill the node's disk with them.
package logx

import (
	"fmt"
	"io"
	"sync"
	"time"
)

// Limited writes at most PerSecond lines a second and counts the rest, saying how many were dropped once it may
// write again.
type Limited struct {
	PerSecond int
	Out       io.Writer
	Now       func() time.Time

	mu      sync.Mutex
	window  time.Time
	written int
	dropped int
}

// Printf writes one line, unless this second's lines are spent.
func (l *Limited) Printf(format string, args ...any) {
	l.mu.Lock()
	defer l.mu.Unlock()
	now := l.Now().Truncate(time.Second)
	if !now.Equal(l.window) {
		if l.dropped > 0 {
			fmt.Fprintf(l.Out, "trax-egress: %d more lines were not logged\n", l.dropped)
		}
		l.window, l.written, l.dropped = now, 0, 0
	}
	if l.written >= l.PerSecond {
		l.dropped++
		return
	}
	l.written++
	fmt.Fprintf(l.Out, "trax-egress: "+format+"\n", args...)
}
