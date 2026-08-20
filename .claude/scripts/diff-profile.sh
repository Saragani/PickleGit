#!/usr/bin/env bash
#
# diff-profile.sh [<merge-base>]
#
# Read-only gatherer for pkl:review-pr — the diff size profile (changed-file
# summary, line delta, file count). merge-base defaults to detect-base-branch.sh.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mb="${1:-$("$SCRIPT_DIR/detect-base-branch.sh" | sed -n 's/^MERGE_BASE=//p')}"

stat_line="$(git diff "$mb" --stat 2>/dev/null | tail -1)"
echo "Summary: ${stat_line# }"
git diff "$mb" --numstat 2>/dev/null | awk '{a+=$1; r+=$2} END {printf "Lines: +%d/-%d\n", a, r}'
echo "Changed-files: $(git diff "$mb" --name-only 2>/dev/null | wc -l)"
