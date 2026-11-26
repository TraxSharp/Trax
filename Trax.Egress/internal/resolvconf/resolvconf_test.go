package resolvconf

import "testing"

func TestParse(t *testing.T) {
	c, err := Parse("# kubelet\nsearch app.svc.cluster.local svc.cluster.local cluster.local.\nnameserver 10.96.0.10\nnameserver fe80::1%eth0\noptions ndots:5\nnameserver nonsense\n")
	if err != nil {
		t.Fatal(err)
	}
	if len(c.Nameservers) != 2 || c.Nameservers[0].String() != "10.96.0.10" || c.Nameservers[1].String() != "fe80::1" {
		t.Errorf("nameservers %v", c.Nameservers)
	}
	if len(c.Search) != 3 || c.Search[2] != "cluster.local" {
		t.Errorf("search %v", c.Search)
	}
	if _, err := Parse("search x\n"); err == nil {
		t.Error("no nameserver was accepted")
	}
}
