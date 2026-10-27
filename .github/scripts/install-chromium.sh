#!/usr/bin/env bash
# Installs the chromium build a test project's Playwright package names, for the browser tests.
#
# Usage: install-chromium.sh <path to the playwright.ps1 the build put beside the test assembly>
#
# The browser comes from Playwright's CDN in seconds. `--with-deps` also apt-installs every font
# and library Playwright might want, which on the runner image is about 25 MB of fonts the tests
# never render, from a mirror that has served it in six minutes. So the browser is installed on its
# own, and the system packages only when the image lacks a library one of its binaries links
# against.
#
# The CDN's addresses change between lookups, and a lookup the egress filter did not see is
# refused, so a refused download is tried again before the step fails.
set -euo pipefail

script=$1

for attempt in 1 2 3; do
  pwsh "$script" install chromium && break
  if [ "$attempt" -eq 3 ]; then
    echo "::error::Installing chromium failed three times."
    exit 1
  fi
  echo "::warning::Installing chromium failed (attempt $attempt of 3)."
  sleep 20
done

# Where Playwright puts its browsers, unless PLAYWRIGHT_BROWSERS_PATH says otherwise.
browsers=${PLAYWRIGHT_BROWSERS_PATH:-$HOME/.cache/ms-playwright}
shopt -s nullglob
binaries=(
  "$browsers"/chromium-*/chrome-linux64/chrome
  "$browsers"/chromium_headless_shell-*/chrome-headless-shell-linux64/chrome-headless-shell
)
if [ "${#binaries[@]}" -eq 0 ]; then
  echo "::error::No chromium binary under $browsers after the install."
  exit 1
fi

missing=$(ldd "${binaries[@]}" | grep 'not found' | sort -u || true)
if [ -n "$missing" ]; then
  echo "The runner image lacks libraries chromium links against; installing Playwright's system packages:"
  echo "$missing"
  pwsh "$script" install-deps chromium
fi
