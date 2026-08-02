#!/usr/bin/env bash
# Package the scrobbler formula with Homebrew and produce a bottle.
#
# This script must be run as the `linuxbrew` user (brew refuses to run as root),
# e.g.:
#   sudo chown -R linuxbrew:linuxbrew .
#   sudo -u linuxbrew bash scripts/package.sh
#
# Output: `<formula>--<version>.<os>_<arch>.bottle.tar.gz` + `.bottle.json`
# written to the current working directory (the workspace).
set -euo pipefail

# If running as root, re-exec this script as the linuxbrew user with a full
# login environment (brew refuses to run as root). Preserve the working dir.
if [[ "$(id -u)" -eq 0 ]]; then
  exec su - linuxbrew -c "cd '$PWD' && ROOT_URL='${ROOT_URL:-}' bash '$0'"
fi


git config --global user.email "andrzej.piotrowski76@gmail.com"
git config --global user.name "Avabin"

brew tap-new local/scrobbler
TAP_FORMULA="$(brew --repository)/Library/Taps/local/homebrew-scrobbler/Formula"
mkdir -p "$TAP_FORMULA"
cp Formula/scrobbler.rb "$TAP_FORMULA/scrobbler.rb"

brew install --build-bottle local/scrobbler/scrobbler
brew bottle --json --root-url "${ROOT_URL:-https://github.com/Avabin/Scrobbler/releases}" local/scrobbler/scrobbler

echo "Bottle artifacts:"
ls -la ./*.bottle.tar.gz ./*.bottle.json 2>/dev/null || true
