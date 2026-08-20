#!/usr/bin/env bash
#
# checkpoint.sh — drive a BUILD per-step checkpoint on a .plans/<ticket>.md file.
#
# Mechanical state only — the judgment (deviation detection, stub-logic review,
# the Tests/Changed/Deviations content) stays in workflow.md prose. This script:
#   - marks "### Step N:" heading ✓ (and strips its "← current"),
#   - marks "### Step N+1:" heading "← current",
#   - resolves "## Deviation Register" entries targeting Step N (appends ✓),
#   - syncs frontmatter (phase=BUILD, step=N+1, next=<title>, updated) via the
#     atomic plan-frontmatter.sh,
#   - manual mode: prints the Step Checkpoint skeleton (model fills Tests/Changed/
#     Deviations and waits for `approved: step N`);
#     auto mode: records a one-line summary to .plans/<ticket>.report.md and does
#     NOT print the blocking prompt (the run continues).
#
# Usage:
#   checkpoint.sh <ticket> <N> [--mode manual|auto] [--deviations "<text>"] [--add-register "<entry>"]
#   checkpoint.sh <ticket> --final-report
#   checkpoint.sh --self-test
#
# --deviations writes the step's **Deviations** field; --add-register appends a new
# Deviation Register entry; both let checkpoint.sh own ALL plan-state writes (the
# model never hand-edits the plan). Completing a step also ticks its `- [ ]` stubs to
# `- [x]` (every stub is GREEN by checkpoint time).
#
# Body writes go to a temp file + atomic mv; frontmatter via plan-frontmatter.sh.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FM="$SCRIPT_DIR/plan-frontmatter.sh"

_plan_path() { printf '.plans/%s.md' "$1"; }
_report_path() { printf '.plans/%s.report.md' "$1"; }

# Title of "### Step <n>:" (markers stripped). Empty if not found.
_step_title() {
    local plan="$1" n="$2"
    grep -m1 -E "^### Step $n:" "$plan" 2>/dev/null \
        | sed -E "s/^### Step $n:[[:space:]]*//; s/[[:space:]]*(✓|← current)[[:space:]]*$//"
}

# Body mutation: mark Step n ✓, Step n+1 ← current, resolve register entries for n.
_mutate_body() {
    local plan="$1" n="$2" dev="$3" tmp="$plan.cptmp.$$"
    awk -v n="$n" -v dev="$dev" '
        BEGIN { reg=0; instep=0 }
        /^## Deviation Register/ { reg=1; instep=0; print; next }
        /^## / { reg=0; instep=0; print; next }
        $0 ~ ("^### Step " n ":") {
            line=$0; sub(/[[:space:]]*← current[[:space:]]*$/, "", line)
            if (line !~ /✓[[:space:]]*$/) line=line " ✓"
            print line; instep=1; next
        }
        $0 ~ ("^### Step " (n+1) ":") {
            line=$0; sub(/[[:space:]]*✓[[:space:]]*$/, "", line)
            if (line !~ /← current[[:space:]]*$/) line=line " ← current"
            print line; instep=0; next
        }
        /^### Step / { instep=0; print; next }
        instep && /^- \[ \] / { sub(/^- \[ \] /, "- [x] "); print; next }
        instep && dev != "" && /^\*\*Deviations\*\*:/ { print "**Deviations**: " dev; next }
        reg && /^\[Step / && $0 !~ /✓/ {
            if (match($0, /\[[^]]*\]/)) {
                br=substr($0, RSTART, RLENGTH)
                if (br ~ /affects Steps?/) {
                    ap=br
                    sub(/.*affects Steps? /, "", ap); sub(/].*/, "", ap)
                    m=split(ap, arr, /[ ,]+/); hit=0
                    for (i=1;i<=m;i++) if (arr[i]==n) hit=1
                    if (hit) {
                        newbr=substr(br,1,length(br)-1) " ✓]"
                        print substr($0,1,RSTART-1) newbr substr($0,RSTART+RLENGTH); next
                    }
                }
            }
            print; next
        }
        { print }
    ' "$plan" > "$tmp" || { rm -f "$tmp"; return 1; }
    mv "$tmp" "$plan"
}

# Append a new Deviation Register entry (idempotent), before the next ## section / EOF.
_add_register() {
    local plan="$1" entry="$2" tmp="$plan.cptmp.$$"
    # Idempotency is scoped to real entry lines (^[Step …) inside the
    # "## Deviation Register" section — a verbatim mention of the entry in step
    # prose elsewhere must not suppress the append.
    if awk -v entry="$entry" '
        /^## Deviation Register/ { reg=1; next }
        /^## / { reg=0 }
        reg && /^\[Step / && index($0, entry) { found=1; exit }
        END { exit(found ? 0 : 1) }
    ' "$plan"; then return 0; fi
    awk -v entry="$entry" '
        BEGIN { reg=0; added=0 }
        /^## Deviation Register/ { reg=1; print; next }
        reg && /^## / && !added { print entry; added=1; reg=0; print; next }
        { print }
        END { if (reg && !added) print entry }
    ' "$plan" > "$tmp" || { rm -f "$tmp"; return 1; }
    mv "$tmp" "$plan"
}

do_checkpoint() {
    local ticket="$1" n="$2" mode="$3" dev="$4" add_reg="$5"
    [ -n "$mode" ] || mode="manual"
    local plan; plan="$(_plan_path "$ticket")"
    if [ ! -r "$plan" ]; then echo "checkpoint: no plan at $plan" >&2; return 1; fi
    if ! grep -qE "^### Step $n:" "$plan"; then echo "checkpoint: Step $n not found in $plan" >&2; return 1; fi

    local title_n title_next next_n
    title_n="$(_step_title "$plan" "$n")"
    next_n=$((n + 1))
    title_next="$(_step_title "$plan" "$next_n")"

    _mutate_body "$plan" "$n" "$dev" || { echo "checkpoint: body mutation failed" >&2; return 1; }
    if [ -n "$add_reg" ]; then
        _add_register "$plan" "$add_reg" || { echo "checkpoint: add-register failed" >&2; return 1; }
    fi

    # Frontmatter via the atomic lib. If there's no next step, keep step=N and mark done-ish.
    if [ -n "$title_next" ]; then
        "$FM" set-phase-step-next "$plan" BUILD "$next_n" "$title_next" \
            || { echo "checkpoint: frontmatter update failed" >&2; return 1; }
    else
        "$FM" set-phase-step-next "$plan" BUILD "$n" "BUILD complete — SHIP when ready" \
            || { echo "checkpoint: frontmatter update failed" >&2; return 1; }
    fi

    if [ "$mode" = "auto" ]; then
        local rpt; rpt="$(_report_path "$ticket")"
        [ -f "$rpt" ] || printf '# Run report — %s (run_mode: auto)\n\n' "$ticket" > "$rpt"
        printf 'Step %s ✓ — %s%s\n' "$n" "$title_n" "${dev:+ (deviation: $dev)}" >> "$rpt"
        echo "checkpoint(auto): Step $n ✓ — $title_n${title_next:+  → Step $next_n: $title_next}"
        return 0
    fi

    # manual: print the checkpoint skeleton (model fills Tests/Changed/Deviations).
    if [ -n "$title_next" ]; then
        cat <<EOF
Step $n done: $title_n
Plan updated: phase=BUILD, step=$next_n, next=$title_next
Tests: <per stub — (unit)/(manual) GREEN>
Changed: <one line per requirement touched — FR<n> — <what> (files …)>
Deviations: ${dev:-<list each + why — or "None">}
Register: ${add_reg:+added: $add_reg}${add_reg:-<entry added/resolved — or "None">}
Proceed to Step $next_n? (\`approved: step $n\` / \`rejected: step $n\` / \`parked\` — or bare \`ok\`/\`next\`/\`go\`/\`1\`)
EOF
    else
        cat <<EOF
Step $n done: $title_n
Plan updated: phase=BUILD, step=$n, next=BUILD complete — SHIP when ready
Tests: <per stub — (unit)/(manual) GREEN>
Changed: <one line per requirement touched — FR<n> — <what> (files …)>
Deviations: ${dev:-<list each + why — or "None">}
Register: ${add_reg:+added: $add_reg}${add_reg:-<entry added/resolved — or "None">}
BUILD complete — all steps done. Initiate SHIP (\`pkl:commit\`) when ready.
EOF
    fi
}

final_report() {
    local ticket="$1" rpt; rpt="$(_report_path "$ticket")"
    if [ ! -r "$rpt" ]; then echo "checkpoint: no run report at $rpt" >&2; return 1; fi
    cat "$rpt"
}

self_test() {
    local d; d="$(mktemp -d)"; local fail=0
    mkdir -p "$d/.plans"
    local plan="$d/.plans/T.md"
    cat > "$plan" <<'EOF'
---
ticket: T
phase: BUILD
step: 2
next: second
updated: 2020-01-01 00:00
---

# Plan: T

## Steps
### Step 2: second step ← current
**verification stubs**:
- [ ] first stub passes  (manual)
- [ ] second stub passes  (manual)
**Deviations**:
body two
### Step 3: third step
- [ ] step-3 stub (must NOT be ticked when checkpointing step 2)  (manual)
body three
### Step 23: far step
body twentythree

## Deviation Register
[Step 1 → affects Step 2] something → Step 2: do x
[Step 1 → affects Steps 3,4] other → Step 3: do y
[Step 1 → affects Step 23] far → Step 23: do z
EOF
    ( cd "$d" && "$SCRIPT_DIR/checkpoint.sh" T 2 --deviations "touched extra; approved" --add-register "[Step 2 → affects Step 3] x created → Step 3: clean it" >/dev/null ) || { echo "self-test: checkpoint failed" >&2; fail=1; }

    grep -qE '^### Step 2: second step ✓$'      "$plan" || { echo "self-test: Step 2 not ✓" >&2; fail=1; }
    grep -qE '^### Step 2:.*← current'          "$plan" && { echo "self-test: Step 2 still ← current" >&2; fail=1; }
    grep -qE '^### Step 3: third step ← current$' "$plan" || { echo "self-test: Step 3 not ← current" >&2; fail=1; }
    # stub checkboxes for step 2 ticked; step-3 stub left unticked
    [ "$(grep -c '^- \[x\] ' "$plan")" = "2" ] || { echo "self-test: step-2 stubs not both ticked" >&2; fail=1; }
    grep -qE '^- \[ \] step-3 stub' "$plan" || { echo "self-test: step-3 stub wrongly ticked" >&2; fail=1; }
    # deviations field written + new register entry appended
    grep -qE '^\*\*Deviations\*\*: touched extra; approved$' "$plan" || { echo "self-test: deviations field not written" >&2; fail=1; }
    grep -qF '[Step 2 → affects Step 3] x created → Step 3: clean it' "$plan" || { echo "self-test: register entry not added" >&2; fail=1; }
    grep -qE '^\[Step 1 → affects Step 2 ✓\]' "$plan" || { echo "self-test: register for Step 2 not resolved" >&2; fail=1; }
    # Step 23 entry must NOT be resolved by n=2 (substring guard)
    grep -qE '^\[Step 1 → affects Step 23\] far → Step 23: do z$' "$plan" || { echo "self-test: Step 23 register wrongly touched by n=2" >&2; fail=1; }
    # Steps 3,4 entry must NOT be resolved by n=2
    if grep 'affects Steps 3,4' "$plan" | grep -q '✓'; then echo "self-test: Steps 3,4 wrongly resolved" >&2; fail=1; fi
    grep -qE '^step: 3$'    "$plan" || { echo "self-test: frontmatter step not 3" >&2; fail=1; }
    grep -qE '^next: third step$' "$plan" || { echo "self-test: frontmatter next not set" >&2; fail=1; }
    grep -q '^updated: 2020' "$plan" && { echo "self-test: updated not stamped" >&2; fail=1; }

    # auto mode: records report, no prompt
    cat > "$plan" <<'EOF'
---
ticket: T
phase: BUILD
step: 3
next: third
updated: 2020-01-01 00:00
---
## Steps
### Step 3: third step ← current
### Step 4: fourth step
EOF
    local out; out="$( cd "$d" && "$SCRIPT_DIR/checkpoint.sh" T 3 --mode auto )"
    printf '%s' "$out" | grep -qi "Proceed to Step" && { echo "self-test: auto printed blocking prompt" >&2; fail=1; }
    [ -f "$d/.plans/T.report.md" ] || { echo "self-test: auto did not write report" >&2; fail=1; }
    ( cd "$d" && "$SCRIPT_DIR/checkpoint.sh" T --final-report ) | grep -q 'Step 3 ✓' || { echo "self-test: final-report missing step" >&2; fail=1; }

    # regression: an entry quoted verbatim in step PROSE must NOT suppress the
    # --add-register append (idempotency is scoped to register-section ^[Step lines).
    local pq="$d/.plans/P.md"
    cat > "$pq" <<'EOF'
---
ticket: P
phase: BUILD
step: 1
next: one
updated: 2020-01-01 00:00
---
## Steps
### Step 1: s ← current
**What**: pass --add-register "[Step 1 → affects Step 2] q → Step 2: fix q".
**Deviations**:
### Step 2: t

## Deviation Register
EOF
    ( cd "$d" && "$SCRIPT_DIR/checkpoint.sh" P 1 --mode auto --add-register "[Step 1 → affects Step 2] q → Step 2: fix q" >/dev/null )
    [ "$(awk '/^## Deviation Register/{r=1;next} /^## /{r=0} r&&/^\[Step 1 → affects Step 2\]/{c++} END{print c+0}' "$pq")" = "1" ] \
        || { echo "self-test: --add-register suppressed by a prose quote (regression)" >&2; fail=1; }

    # malformed plan: missing step -> non-zero
    if ( cd "$d" && "$SCRIPT_DIR/checkpoint.sh" T 99 ) 2>/dev/null; then echo "self-test: missing step should fail" >&2; fail=1; fi

    rm -rf "$d"
    if [ "$fail" -ne 0 ]; then echo "checkpoint self-test: FAILED" >&2; return 1; fi
    echo "checkpoint self-test: PASSED"
}

main() {
    case "${1:-}" in
        --self-test) self_test; return $? ;;
        "") echo "usage: checkpoint.sh <ticket> <N> [--mode manual|auto] [--deviations <text>] [--add-register <entry>] | <ticket> --final-report | --self-test" >&2; return 2 ;;
    esac
    local ticket="$1"; shift
    if [ "${1:-}" = "--final-report" ]; then final_report "$ticket"; return $?; fi
    local n="$1"; shift
    if [ -z "$n" ]; then echo "usage: checkpoint.sh <ticket> <N> [--mode manual|auto]" >&2; return 2; fi
    local mode="manual" dev="" add_reg=""
    while [ "$#" -gt 0 ]; do
        case "$1" in
            --mode)         mode="$2"; shift 2 ;;
            --deviations)   dev="$2"; shift 2 ;;
            --add-register) add_reg="$2"; shift 2 ;;
            *) echo "checkpoint: unknown arg: $1" >&2; return 2 ;;
        esac
    done
    do_checkpoint "$ticket" "$n" "$mode" "$dev" "$add_reg"
}

main "$@"
