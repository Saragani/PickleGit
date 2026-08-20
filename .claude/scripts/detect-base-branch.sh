#!/usr/bin/env bash
#
# detect-base-branch.sh — print the review base branch and merge-base.
#
# Single home for the dynamic base-branch detection used by pkl:review-pr and the
# other review gatherers (plan-drift-check.sh, diff-profile.sh). Never hardcodes
# the base to a fixed name beyond the final fallback.
#
# PickleGit branching: all work lands on `main` — no versioned release branches
# currently exist. If that changes, extend the grep pattern below rather than
# hardcoding a second branch name here.
#
# Output (parseable):
#   BASE=<branch>
#   MERGE_BASE=<sha>

BASE="$(git log --simplify-by-decoration --pretty=%D HEAD 2>/dev/null | grep -oE 'main' | head -1)"
BASE="${BASE:-main}"
MERGE_BASE="$(git merge-base HEAD "$BASE" 2>/dev/null)"

echo "BASE=$BASE"
echo "MERGE_BASE=$MERGE_BASE"
