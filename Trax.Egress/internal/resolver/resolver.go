// Package resolver is the pod's only way to resolve a name. It answers an A query for a name on the allowlist by
// asking the pod's upstream, and every other name with NXDOMAIN, so an unlisted name does not resolve and a name
// made of data (DNS tunnelling) never leaves the pod. It forwards no query as it was sent: it builds a fresh one
// holding only the name, in lower case, and the type, so nothing else the app put in its query reaches the upstream.
package resolver

import (
	"context"
	"crypto/rand"
	"encoding/binary"
	"errors"
	"strings"
)

// DNS constants used here.
const (
	TypeA     = 1
	ClassIN   = 1
	RcodeOK   = 0
	RcodeFail = 2
	RcodeName = 3
	RcodeRef  = 5

	headerLen = 12
	// MaxUDP is the largest answer sent over UDP to a client that offered no more (RFC 1035).
	MaxUDP = 512
)

// Upstream sends a query to the pod's nameservers and returns their answer.
type Upstream interface {
	Exchange(ctx context.Context, query []byte) ([]byte, error)
}

// Policy says which names may be resolved.
type Policy struct {
	// Names are answered as they are.
	Names map[string]bool
	// Short are names also answered under each search domain: the deployment's own services, which a pod finds
	// by their short name.
	Short map[string]bool
	// Search is the search domains from resolv.conf.
	Search []string
}

// Allows says whether name (lower case, no trailing dot) may be resolved.
func (p Policy) Allows(name string) bool {
	if p.Names[name] {
		return true
	}
	for short := range p.Short {
		rest, ok := strings.CutPrefix(name, short+".")
		if !ok {
			continue
		}
		for _, s := range p.Search {
			if rest == s {
				return true
			}
		}
	}
	return false
}

// Resolver answers queries under a policy.
type Resolver struct {
	Policy   Policy
	Upstream Upstream
	// Refused is called with each name refused, for the log.
	Refused func(name string)
}

// Question is a parsed query's one question.
type Question struct {
	Name  string
	Type  uint16
	Class uint16
	// Raw is the question section as it was sent.
	Raw []byte
}

var errMalformed = errors.New("malformed query")

// ParseQuery reads a query: one question, no answers or authority, at most one additional record (EDNS), opcode
// QUERY. A query that is anything else gets no answer.
func ParseQuery(msg []byte) (id uint16, rd bool, q Question, err error) {
	if len(msg) < headerLen {
		return 0, false, Question{}, errMalformed
	}
	id = binary.BigEndian.Uint16(msg[0:2])
	flags := binary.BigEndian.Uint16(msg[2:4])
	if flags&0x8000 != 0 || (flags>>11)&0xF != 0 {
		return 0, false, Question{}, errMalformed
	}
	rd = flags&0x0100 != 0
	qd := binary.BigEndian.Uint16(msg[4:6])
	an := binary.BigEndian.Uint16(msg[6:8])
	ns := binary.BigEndian.Uint16(msg[8:10])
	ar := binary.BigEndian.Uint16(msg[10:12])
	if qd != 1 || an != 0 || ns != 0 || ar > 1 {
		return 0, false, Question{}, errMalformed
	}
	q, _, err = parseQuestion(msg, headerLen)
	return id, rd, q, err
}

func parseQuestion(msg []byte, off int) (Question, int, error) {
	start := off
	var labels []string
	wire := 0
	for {
		if off >= len(msg) {
			return Question{}, 0, errMalformed
		}
		n := int(msg[off])
		off++
		wire++
		if n == 0 {
			break
		}
		// No compression in a question, and no label longer than 63.
		if n > 63 || off+n > len(msg) {
			return Question{}, 0, errMalformed
		}
		labels = append(labels, string(msg[off:off+n]))
		off += n
		wire += n
		if wire > 255 {
			return Question{}, 0, errMalformed
		}
	}
	if off+4 > len(msg) {
		return Question{}, 0, errMalformed
	}
	q := Question{
		Name:  strings.ToLower(strings.Join(labels, ".")),
		Type:  binary.BigEndian.Uint16(msg[off : off+2]),
		Class: binary.BigEndian.Uint16(msg[off+2 : off+4]),
	}
	off += 4
	q.Raw = append([]byte(nil), msg[start:off]...)
	// A label holding a dot or a byte outside letters, digits, dash and underscore cannot be a name on the list.
	for _, l := range labels {
		for i := 0; i < len(l); i++ {
			c := l[i] | 0x20
			if !(c >= 'a' && c <= 'z' || l[i] >= '0' && l[i] <= '9' || l[i] == '-' || l[i] == '_') {
				q.Name = ""
				return q, off, nil
			}
		}
	}
	return q, off, nil
}

// Reply builds an answer with no records.
func Reply(id uint16, rd bool, q Question, rcode int) []byte {
	msg := make([]byte, headerLen, headerLen+len(q.Raw))
	binary.BigEndian.PutUint16(msg[0:2], id)
	flags := uint16(0x8000) | uint16(0x0080) | uint16(rcode&0xF)
	if rd {
		flags |= 0x0100
	}
	binary.BigEndian.PutUint16(msg[2:4], flags)
	binary.BigEndian.PutUint16(msg[4:6], 1)
	return append(msg, q.Raw...)
}

// lower returns the question section raw with its labels in lower case, so the letter case the app chose (which
// could carry a bit a letter) does not reach the upstream. Lowering keeps every length, so the section is the
// same size.
func lower(raw []byte) []byte {
	out := append([]byte(nil), raw...)
	for off := 0; off < len(out) && out[off] != 0; off += 1 + int(out[off]) {
		for i := off + 1; i <= off+int(out[off]) && i < len(out); i++ {
			if out[i] >= 'A' && out[i] <= 'Z' {
				out[i] |= 0x20
			}
		}
	}
	return out
}

// NewQuery builds the query sent upstream: a fresh id, recursion desired, the one question in lower case and
// nothing else.
func NewQuery(q Question) ([]byte, uint16, error) {
	var idb [2]byte
	if _, err := rand.Read(idb[:]); err != nil {
		return nil, 0, err
	}
	id := binary.BigEndian.Uint16(idb[:])
	msg := make([]byte, headerLen, headerLen+len(q.Raw))
	binary.BigEndian.PutUint16(msg[0:2], id)
	binary.BigEndian.PutUint16(msg[2:4], 0x0100)
	binary.BigEndian.PutUint16(msg[4:6], 1)
	return append(msg, lower(q.Raw)...), id, nil
}

// Answer is the reply to one query, or nil when the query gets none (it was malformed or was itself a reply).
// udp says the reply goes back over UDP, where an answer longer than MaxUDP is cut to its question with TC set, so
// the client asks again over TCP.
func (r *Resolver) Answer(ctx context.Context, query []byte, udp bool) []byte {
	id, rd, q, err := ParseQuery(query)
	if err != nil {
		return nil
	}
	if q.Class != ClassIN {
		return Reply(id, rd, q, RcodeRef)
	}
	if q.Name == "" || !r.Policy.Allows(q.Name) {
		if r.Refused != nil {
			r.Refused(q.Name)
		}
		return Reply(id, rd, q, RcodeName)
	}
	// An allowed name has an IPv4 address and nothing else: the pod reaches nothing over IPv6, and no other record
	// type is needed to connect to a name.
	if q.Type != TypeA {
		return Reply(id, rd, q, RcodeOK)
	}
	out, outID, err := NewQuery(q)
	if err != nil {
		return Reply(id, rd, q, RcodeFail)
	}
	resp, err := r.Upstream.Exchange(ctx, out)
	if err != nil || !answers(resp, outID, q) {
		return Reply(id, rd, q, RcodeFail)
	}
	resp = append([]byte(nil), resp...)
	binary.BigEndian.PutUint16(resp[0:2], id)
	// The app gets its own question back, letter case and all, as from any resolver.
	copy(resp[headerLen:], q.Raw)
	if udp && len(resp) > MaxUDP {
		cut := Reply(id, rd, q, int(binary.BigEndian.Uint16(resp[2:4])&0xF))
		binary.BigEndian.PutUint16(cut[2:4], binary.BigEndian.Uint16(cut[2:4])|0x0200)
		return cut
	}
	return resp
}

// answers says whether resp is the upstream's reply to the query with id and question q.
func answers(resp []byte, id uint16, q Question) bool {
	if len(resp) < headerLen || binary.BigEndian.Uint16(resp[0:2]) != id {
		return false
	}
	flags := binary.BigEndian.Uint16(resp[2:4])
	if flags&0x8000 == 0 || binary.BigEndian.Uint16(resp[4:6]) != 1 {
		return false
	}
	got, _, err := parseQuestion(resp, headerLen)
	return err == nil && got.Name == q.Name && got.Type == q.Type && got.Class == q.Class &&
		string(lower(got.Raw)) == string(lower(q.Raw))
}
