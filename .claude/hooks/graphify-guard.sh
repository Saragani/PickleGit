#!/usr/bin/env bash
# Runs `graphify hook-guard <mode>` when graphify is installed; otherwise does nothing.
# Keeps the PreToolUse hooks harmless for developers who don't use graphify.
# Install: `uv tool install graphifyy` (or `pipx install graphifyy`).

command -v graphify >/dev/null 2>&1 || exit 0

exec graphify hook-guard "$@"
