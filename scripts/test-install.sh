#!/usr/bin/env bash
#
# Dockerized smoke test for scripts/install.sh against a PUBLISHED release.
# Runs the installer in a clean ubuntu container and asserts the core paths:
#   1. happy path  — curl | sh  → exit 0, the binary runs `version`
#   2. --help      — prints usage, exit 0
#
# This covers the "does install.sh actually download + checksum-verify +
# install + run" smoke. The conflict (exit 6), checksum-mismatch (exit 5),
# same-version (exit 7) and per-OS/Gatekeeper paths stay in the manual matrix
# (docs/testing/manual-test-plan.md) — they need tampering or non-Linux hosts.
#
# Requires Docker. NOT in the CI gate — wire as workflow_dispatch (it hits the
# network + GitHub Releases).
#
# Usage:
#   scripts/test-install.sh [version-tag]      # default: latest release
#   WEBREAPER_VERSION=v10.0.1 scripts/test-install.sh
#
set -euo pipefail

VERSION="${1:-${WEBREAPER_VERSION:-}}"   # empty → installer resolves latest
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_SH="${SCRIPT_DIR}/install.sh"
IMAGE="ubuntu:24.04"

[ -f "${INSTALL_SH}" ] || { echo "install.sh not found at ${INSTALL_SH}"; exit 1; }

echo "==> install.sh smoke in ${IMAGE} (version: ${VERSION:-latest})"

# Mount the repo's actual install.sh (read-only) and run it — tests the
# working-tree script, not whatever happens to be on the remote. It still
# fetches the release binary from GitHub Releases, so network is required.
docker run --rm \
  -e "WEBREAPER_VERSION=${VERSION}" \
  -v "${INSTALL_SH}:/install.sh:ro" \
  "${IMAGE}" bash -euo pipefail -c '
  apt-get update -qq
  apt-get install -y -qq curl unzip tar ca-certificates >/dev/null
  echo "--- 1. happy path: sh install.sh ---"
  sh /install.sh
  echo "--- binary runs: webreaper version ---"
  webreaper version
  echo "--- 2. --help exits 0 ---"
  sh /install.sh --help
  echo "ALL INSTALL SMOKE CHECKS PASSED"
'
echo "==> OK"
