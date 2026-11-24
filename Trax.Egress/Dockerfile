# The egress image, ghcr.io/traxsharp/trax-egress. Built from this folder (podman reads this name too):
#   podman build --build-arg VERSION=<version> -t trax-egress:<version> .
# Both base images are pinned by the digest of their multi-arch index; bump the tag and the digest together.
FROM docker.io/library/golang:1.25-alpine@sha256:1ae0735f00daffa3aaf1363a5184c0d2dc55c78e3db4ec70241cdac97bf84b59 AS build
ARG VERSION=dev
WORKDIR /src
COPY go.mod ./
COPY cmd ./cmd
COPY internal ./internal
# Standard library only: nothing is downloaded, and the toolchain is the image's own.
ENV CGO_ENABLED=0 GOTOOLCHAIN=local GOFLAGS=-trimpath GOPROXY=off
RUN go build -ldflags="-s -w -buildid= -X main.version=${VERSION}" -o /out/trax-egress ./cmd/trax-egress

FROM docker.io/library/alpine:3.22@sha256:49340b346798875b5592e5d420241f2f9cfba5f47e62ebb1446a1b34734e898f
# nftables applies the rules; ca-certificates lets the check verify the names it reaches.
RUN apk add --no-cache nftables ca-certificates \
 && addgroup -S -g 61000 egress \
 && adduser -S -D -H -u 61000 -G egress -s /sbin/nologin egress
COPY --from=build /out/trax-egress /usr/local/bin/trax-egress
COPY seccomp/apps-nouserns.json /usr/share/trax-egress/seccomp/apps-nouserns.json
LABEL org.opencontainers.image.source="https://github.com/TraxSharp/Trax" \
      org.opencontainers.image.description="Holds a pod's outbound traffic to a list of names" \
      org.opencontainers.image.licenses="MIT"
USER 61000:61000
ENTRYPOINT ["/usr/local/bin/trax-egress"]
CMD ["serve"]
