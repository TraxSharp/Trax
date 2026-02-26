#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "Installing dependencies..."
npm install

echo "Compiling TypeScript..."
npm run compile

echo "Packaging VSIX..."
npm run package

VSIX_FILE=$(ls -t *.vsix 2>/dev/null | head -1)
if [[ -n "$VSIX_FILE" ]]; then
  echo ""
  echo "Built: $VSIX_FILE"
  echo "Install with: code --install-extension $VSIX_FILE"
fi
