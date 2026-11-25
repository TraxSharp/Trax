package resolver

import (
	"context"
	"encoding/binary"
	"errors"
	"strings"
	"testing"
)

func query(id uint16, name string, qtype uint16) []byte {
	q := []byte{0, 0, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0}
	binary.BigEndian.PutUint16(q[0:2], id)
	for _, l := range strings.Split(name, ".") {
		q = append(q, byte(len(l)))
		q = append(q, l...)
	}
	q = append(q, 0)
	q = binary.BigEndian.AppendUint16(q, qtype)
	return binary.BigEndian.AppendUint16(q, ClassIN)
}

// fakeUpstream answers every A query with one record and remembers what it was sent.
type fakeUpstream struct {
	sent [][]byte
	fail bool
	// mangle changes the answer before it is returned.
	mangle func([]byte) []byte
	pad    int
}

func (f *fakeUpstream) Exchange(_ context.Context, q []byte) ([]byte, error) {
	f.sent = append(f.sent, append([]byte(nil), q...))
	if f.fail {
		return nil, errors.New("down")
	}
	resp := append([]byte(nil), q...)
	binary.BigEndian.PutUint16(resp[2:4], 0x8180)
	binary.BigEndian.PutUint16(resp[6:8], 1)
	resp = append(resp, 0xC0, 0x0C, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 203, 0, 113, 7)
	if f.pad > 0 {
		resp = append(resp, make([]byte, f.pad)...)
	}
	if f.mangle != nil {
		resp = f.mangle(resp)
	}
	return resp, nil
}

func newResolver(up *fakeUpstream) (*Resolver, *[]string) {
	refused := []string{}
	return &Resolver{
		Policy: Policy{
			Names:  map[string]bool{"api.anthropic.com": true, "database": true},
			Short:  map[string]bool{"database": true},
			Search: []string{"app.svc.cluster.local", "svc.cluster.local"},
		},
		Upstream: up,
		Refused:  func(n string) { refused = append(refused, n) },
	}, &refused
}

func rcode(m []byte) int { return int(m[3] & 0xF) }

func TestAllowedNameIsForwardedAsAFreshQuery(t *testing.T) {
	up := &fakeUpstream{}
	r, _ := newResolver(up)
	q := query(0xBEEF, "API.Anthropic.com", TypeA)
	// An additional record the app added (an EDNS option could carry data) must not be forwarded.
	q[11] = 1
	q = append(q, 0, 0, 41, 0x10, 0, 0, 0, 0, 0, 0, 4, 'l', 'e', 'a', 'k')
	resp := r.Answer(context.Background(), q, true)
	if resp == nil || rcode(resp) != RcodeOK || binary.BigEndian.Uint16(resp[0:2]) != 0xBEEF {
		t.Fatalf("answer %v", resp)
	}
	if binary.BigEndian.Uint16(resp[6:8]) != 1 {
		t.Fatal("the answer lost its record")
	}
	if len(up.sent) != 1 {
		t.Fatalf("sent %d queries", len(up.sent))
	}
	sent := up.sent[0]
	if binary.BigEndian.Uint16(sent[10:12]) != 0 || strings.Contains(string(sent), "leak") {
		t.Error("the app's additional record was forwarded")
	}
}

func TestTheUpstreamQuestionIsLowerCase(t *testing.T) {
	up := &fakeUpstream{}
	r, _ := newResolver(up)
	q := query(0xBEEF, "aPi.AnTHropIC.cOm", TypeA)
	resp := r.Answer(context.Background(), q, true)
	if resp == nil || rcode(resp) != RcodeOK {
		t.Fatalf("answer %v", resp)
	}
	sent := up.sent[0]
	if want := query(0, "api.anthropic.com", TypeA)[headerLen:]; string(sent[headerLen:]) != string(want) {
		t.Errorf("the letter case the app chose went upstream: %q", sent[headerLen:])
	}
	// The app gets its own question back, as any resolver answers it.
	if got := resp[headerLen:len(q)]; string(got) != string(q[headerLen:]) {
		t.Errorf("the answer's question is %q, not the app's", got)
	}
}

func TestShortNameUnderASearchDomain(t *testing.T) {
	up := &fakeUpstream{}
	r, _ := newResolver(up)
	for _, n := range []string{"database", "database.app.svc.cluster.local", "database.svc.cluster.local"} {
		if resp := r.Answer(context.Background(), query(1, n, TypeA), true); rcode(resp) != RcodeOK {
			t.Errorf("%s: rcode %d", n, rcode(resp))
		}
	}
	for _, n := range []string{"other.app.svc.cluster.local", "database.evil.example", "x.database.app.svc.cluster.local"} {
		if resp := r.Answer(context.Background(), query(1, n, TypeA), true); rcode(resp) != RcodeName {
			t.Errorf("%s: rcode %d", n, rcode(resp))
		}
	}
}

func TestUnlistedNameIsNXDomainAndNeverForwarded(t *testing.T) {
	up := &fakeUpstream{}
	r, refused := newResolver(up)
	for _, n := range []string{
		"example.com",
		"aGVsbG8gd29ybGQ.tunnel.example.com",
		"x.api.anthropic.com",
		"api.anthropic.com.example.com",
	} {
		resp := r.Answer(context.Background(), query(7, n, TypeA), true)
		if rcode(resp) != RcodeName {
			t.Errorf("%s: rcode %d", n, rcode(resp))
		}
	}
	if len(up.sent) != 0 {
		t.Fatalf("an unlisted name reached the upstream: %d queries", len(up.sent))
	}
	if len(*refused) != 4 {
		t.Errorf("refused %v", *refused)
	}
}

func TestOtherTypesOfAnAllowedNameGetNoRecords(t *testing.T) {
	up := &fakeUpstream{}
	r, _ := newResolver(up)
	for _, qt := range []uint16{28, 16, 15, 33, 65, 255, 10} {
		resp := r.Answer(context.Background(), query(3, "api.anthropic.com", qt), true)
		if rcode(resp) != RcodeOK || binary.BigEndian.Uint16(resp[6:8]) != 0 {
			t.Errorf("type %d: %v", qt, resp)
		}
	}
	if len(up.sent) != 0 {
		t.Fatal("a non-A query was forwarded")
	}
}

func TestMalformedQueriesGetNoAnswer(t *testing.T) {
	up := &fakeUpstream{}
	r, _ := newResolver(up)
	good := query(1, "api.anthropic.com", TypeA)
	reply := append([]byte(nil), good...)
	reply[2] |= 0x80
	twoQ := append([]byte(nil), good...)
	twoQ[5] = 2
	withAnswer := append([]byte(nil), good...)
	withAnswer[7] = 1
	notQuery := append([]byte(nil), good...)
	notQuery[2] |= 0x28 // opcode 5, update
	pointer := []byte{0, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0xC0, 0x0C, 0, 1, 0, 1}
	long := []byte{0, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0}
	for i := 0; i < 5; i++ {
		long = append(long, 63)
		long = append(long, []byte(strings.Repeat("a", 63))...)
	}
	long = append(long, 0, 0, 1, 0, 1)
	for name, q := range map[string][]byte{
		"short": good[:5], "reply": reply, "two questions": twoQ, "an answer": withAnswer,
		"opcode": notQuery, "compression": pointer, "over 255": long, "truncated": good[:len(good)-2],
	} {
		if resp := r.Answer(context.Background(), q, true); resp != nil {
			t.Errorf("%s: answered %v", name, resp)
		}
	}
	if len(up.sent) != 0 {
		t.Fatal("a malformed query was forwarded")
	}
}

func TestOddBytesInANameAreRefused(t *testing.T) {
	up := &fakeUpstream{}
	r, _ := newResolver(up)
	q := query(1, "api.anthropic.com", TypeA)
	// "api" becomes "a.i": a label holding a dot must not match "a" + "i".
	q[13] = '.'
	if resp := r.Answer(context.Background(), q, true); rcode(resp) != RcodeName {
		t.Fatalf("rcode %d", rcode(resp))
	}
}

func TestNonINClassIsRefused(t *testing.T) {
	r, _ := newResolver(&fakeUpstream{})
	q := query(1, "api.anthropic.com", TypeA)
	q[len(q)-1] = 3 // CH
	if resp := r.Answer(context.Background(), q, true); rcode(resp) != RcodeRef {
		t.Fatalf("rcode %d", rcode(resp))
	}
}

func TestUpstreamFailureIsServfail(t *testing.T) {
	r, _ := newResolver(&fakeUpstream{fail: true})
	if resp := r.Answer(context.Background(), query(1, "api.anthropic.com", TypeA), true); rcode(resp) != RcodeFail {
		t.Fatalf("rcode %d", rcode(resp))
	}
}

func TestAnAnswerToAnotherQuestionIsNotPassedOn(t *testing.T) {
	for name, mangle := range map[string]func([]byte) []byte{
		"id":       func(b []byte) []byte { b[0] ^= 0xFF; return b },
		"question": func(b []byte) []byte { b[13] = 'x'; return b },
		"not qr":   func(b []byte) []byte { b[2] &^= 0x80; return b },
		"short":    func(b []byte) []byte { return b[:6] },
	} {
		r, _ := newResolver(&fakeUpstream{mangle: mangle})
		if resp := r.Answer(context.Background(), query(1, "api.anthropic.com", TypeA), true); rcode(resp) != RcodeFail {
			t.Errorf("%s: rcode %d", name, rcode(resp))
		}
	}
}

func TestALongAnswerOverUDPIsTruncated(t *testing.T) {
	r, _ := newResolver(&fakeUpstream{pad: 600})
	resp := r.Answer(context.Background(), query(1, "api.anthropic.com", TypeA), true)
	if len(resp) > MaxUDP || resp[2]&0x02 == 0 {
		t.Fatalf("len %d flags %x", len(resp), resp[2])
	}
	resp = r.Answer(context.Background(), query(1, "api.anthropic.com", TypeA), false)
	if len(resp) <= MaxUDP {
		t.Fatal("TCP got a cut answer")
	}
}
