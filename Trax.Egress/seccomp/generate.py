#!/usr/bin/env python3
"""Writes apps-nouserns.json, the seccomp profile for app containers, from containers-default.json.

containers-default.json is the default profile of containers/common (pkg/seccomp/seccomp.json, Apache-2.0), as
containers-common 0.70.0 installs it at /usr/share/containers/seccomp.json. The profile written from it differs in
three ways:

- No new user namespaces. clone() is allowed only without CLONE_NEWUSER, clone3() answers ENOSYS so glibc falls
  back to the clone() that can be filtered, and unshare() and setns() are refused. A nested user namespace hands an
  unprivileged process CAP_SYS_ADMIN and CAP_NET_ADMIN over namespaces of its own, the usual first step of a kernel
  privilege escalation.
- No mount API at all (mount, fsopen, open_tree, move_mount, pivot_root and the rest).
- No kernel keyring (keyctl, add_key, request_key).

It is for the app containers only, never for the egress's rules step, which needs nothing this takes away but
would gain nothing from it either.

Run from this folder after updating containers-default.json:
  python3 -I generate.py containers-default.json > apps-nouserns.json
CI runs it and fails when the committed profile differs.
"""
import json
import sys

CLONE_NEWUSER = 0x10000000
ENOSYS = 38
EPERM = 1
DENY = ["unshare", "setns", "mount", "umount", "umount2", "fsopen", "fsmount",
        "fsconfig", "fspick", "open_tree", "open_tree_attr", "move_mount",
        "mount_setattr", "pivot_root", "keyctl", "add_key", "request_key"]


def main(path):
    with open(path, encoding="utf-8") as f:
        profile = json.load(f)
    strip = set(DENY) | {"clone", "clone3"}
    rules = []
    for rule in profile["syscalls"]:
        names = [n for n in rule["names"] if n not in strip]
        if names:
            rules.append({**rule, "names": names})
    rules.append({"names": ["clone"], "action": "SCMP_ACT_ALLOW",
                  "args": [{"index": 0, "value": CLONE_NEWUSER, "valueTwo": 0,
                            "op": "SCMP_CMP_MASKED_EQ"}]})
    rules.append({"names": ["clone3"], "action": "SCMP_ACT_ERRNO", "errnoRet": ENOSYS})
    rules.append({"names": DENY, "action": "SCMP_ACT_ERRNO", "errnoRet": EPERM})
    profile["syscalls"] = rules
    sys.stdout.write(json.dumps(profile, indent=1) + "\n")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit("usage: generate.py <containers-default.json>")
    main(sys.argv[1])
