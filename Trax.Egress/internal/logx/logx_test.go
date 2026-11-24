package logx

import (
	"bytes"
	"strings"
	"testing"
	"time"
)

func TestLinesPastTheLimitAreCountedNotWritten(t *testing.T) {
	var out bytes.Buffer
	now := time.Unix(100, 0)
	l := &Limited{PerSecond: 2, Out: &out, Now: func() time.Time { return now }}
	for i := 0; i < 5; i++ {
		l.Printf("refused %d", i)
	}
	now = now.Add(time.Second)
	l.Printf("later")
	got := out.String()
	if strings.Count(got, "refused") != 2 || !strings.Contains(got, "3 more lines were not logged") || !strings.Contains(got, "later") {
		t.Fatalf("got:\n%s", got)
	}
}
