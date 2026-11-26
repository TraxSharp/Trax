# Never built. The images the deploy proof (cluster.sh) pulls and the k3s release it installs, pinned here so that
# Dependabot's docker ecosystem proposes their updates like any other pin; pins.sh reads them for the script.
# k3s is not run as this image: its tag names the release cluster.sh installs (v1.36.6-k3s1 is v1.36.6+k3s1). A k3s
# bump also needs K3S_INSTALL_SHA256 in cluster.sh, and deploy-cluster fails until it has it.
FROM docker.io/rancher/k3s:v1.36.6-k3s1 AS k3s
# The local registry the k3s node pulls the sample's images from.
FROM docker.io/library/registry:3@sha256:82f6ff4a7f982960c8aa51617fea3aabc485df6e55cce80d22deb1de36f1bc3c AS registry
# The probes run inside the namespace.
FROM docker.io/library/busybox:1.37@sha256:a7dbc319b1438115a207e11930dc4bb3e643dcb3ec0bdd694bb36cd8fad289ec AS busybox
