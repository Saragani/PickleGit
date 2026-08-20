#!/usr/bin/env bash
#
# plan-drift-check.sh <ticket> [<merge-base>]
#
# Read-only ADVISORY gatherer for pkl:review-pr §1e. Lists changed files not
# covered by any **Touches** entry in .plans/<ticket>.md, for human confirmation.
# It never gates a review. The judgment (were these intended?) stays in prose.
#
# Matching is generous (to avoid false positives): a changed file is covered if a
# planned entry is a path-prefix of it, OR shares its basename, OR is a glob that
# matches it. Workflow artifacts (.plans/, .claude/) are never reported as drift.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

ticket="$1"
if [ -z "$ticket" ]; then
    echo "usage: plan-drift-check.sh <ticket> [<merge-base>]" >&2
    exit 2
fi
mb="${2:-$("$SCRIPT_DIR/detect-base-branch.sh" | sed -n 's/^MERGE_BASE=//p')}"

plan=".plans/$ticket.md"
if [ ! -r "$plan" ]; then
    echo "plan-drift: no plan file at $plan — skipped"
    exit 0
fi

mapfile -t changed < <(git diff --name-only "$mb" 2>/dev/null)
mapfile -t planned < <(grep -oE '\*\*Touches\*\*:.*' "$plan" | grep -oE '`[^`]+`' | tr -d '`')

drift=0
for c in "${changed[@]}"; do
    [ -n "$c" ] || continue
    case "$c" in .plans/*|.claude/*) continue ;; esac   # workflow artifacts
    cb="$(basename "$c")"
    matched=""
    for p in "${planned[@]}"; do
        [ -n "$p" ] || continue
        case "$c" in "$p"*) matched="$p"; break ;; esac          # path-prefix
        [ "$cb" = "$(basename "$p")" ] && { matched="$p"; break; } # basename
        case "$p" in *'*'*) case "$c" in $p) matched="$p"; break ;; esac ;; esac  # glob
    done
    if [ -z "$matched" ]; then
        echo "  - $c   (closest planned: no match)"
        drift=$((drift + 1))
    fi
done

if [ "$drift" -eq 0 ]; then
    echo "Plan-drift: none — all changed files covered by **Touches**."
else
    echo "Plan-drift (advisory): $drift changed file(s) not covered by any **Touches** entry (confirm intended, or update the plan)."
fi
exit 0
