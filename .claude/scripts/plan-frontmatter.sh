#!/usr/bin/env bash
#
# plan-frontmatter.sh — atomic, byte-preserving YAML-frontmatter mutation for
# .plans/<ticket>.md files. This is the ONLY writer of plan frontmatter state;
# checkpoint.sh and the workflow skills call it.
#
# It edits only the value of targeted keys INSIDE the leading `--- … ---` block;
# every other frontmatter line and the entire body are preserved byte-for-byte.
# Writes go through a temp file + atomic rename; if a targeted key is missing or
# the file has no valid frontmatter, it exits non-zero WITHOUT touching the file.
#
# Subcommands:
#   set-field <file> <key> <value>
#   set-phase-step-next <file> <phase> <step> <title>   (also stamps `updated`)
#   --self-test

# Confirm <file> exists and has a closed `--- … ---` frontmatter block.
_validate_fm() {
    local f="$1"
    if [ ! -r "$f" ]; then
        echo "plan-frontmatter: cannot read $f" >&2
        return 1
    fi
    IFS= read -r first < "$f"
    if [ "$first" != "---" ]; then
        echo "plan-frontmatter: $f has no YAML frontmatter (line 1 is not '---')" >&2
        return 1
    fi
    if ! awk 'NR==1{next} /^---$/{found=1; exit} END{exit !found}' "$f"; then
        echo "plan-frontmatter: $f frontmatter is not closed with '---'" >&2
        return 1
    fi
    return 0
}

# _atomic_awk <file> <awk-prog> <awk-args...> — run awk to a temp file; on awk
# success mv it over <file>, else discard (file untouched). Propagates awk's exit.
_atomic_awk() {
    local f="$1"; shift
    local prog="$1"; shift
    local tmp="$f.fmtmp.$$" rc
    awk "$@" "$prog" "$f" > "$tmp" 2>/dev/null
    rc=$?
    if [ "$rc" -eq 0 ]; then
        mv "$tmp" "$f"
    else
        rm -f "$tmp"
    fi
    return "$rc"
}

cmd_set_field() {
    local f="$1" key="$2" val="$3"
    if [ -z "$f" ] || [ -z "$key" ]; then
        echo "usage: plan-frontmatter.sh set-field <file> <key> <value>" >&2
        return 2
    fi
    _validate_fm "$f" || return 1
    if ! _atomic_awk "$f" '
        NR==1 && $0=="---" { infm=1; print; next }
        infm && $0=="---"  { infm=0; print; next }
        infm && !done && index($0, key ":")==1 { print key ": " val; done=1; next }
        { print }
        END { if (!done) exit 3 }
    ' -v key="$key" -v val="$val"; then
        echo "plan-frontmatter: key '$key' not found in $f frontmatter (not written)" >&2
        return 1
    fi
}

cmd_set_psn() {
    local f="$1" phase="$2" step="$3" title="$4"
    if [ -z "$f" ] || [ -z "$phase" ] || [ -z "$step" ] || [ -z "$title" ]; then
        echo "usage: plan-frontmatter.sh set-phase-step-next <file> <phase> <step> <title>" >&2
        return 2
    fi
    _validate_fm "$f" || return 1
    local now; now="$(date '+%Y-%m-%d %H:%M')"
    if ! _atomic_awk "$f" '
        NR==1 && $0=="---" { infm=1; print; next }
        infm && $0=="---"  { infm=0; print; next }
        infm && index($0,"phase:")==1   { print "phase: " phase; n++; next }
        infm && index($0,"step:")==1    { print "step: " step;  n++; next }
        infm && index($0,"next:")==1    { print "next: " title; n++; next }
        infm && index($0,"updated:")==1 { print "updated: " now; n++; next }
        { print }
        END { if (n < 4) exit 3 }
    ' -v phase="$phase" -v step="$step" -v title="$title" -v now="$now"; then
        echo "plan-frontmatter: phase/step/next/updated not all present in $f (not written)" >&2
        return 1
    fi
}

# Extract the body (everything after the closing frontmatter ---) for byte-diffing.
_body() { awk 'NR==1&&$0=="---"{infm=1;next} infm&&$0=="---"{infm=0;body=1;next} body{print}' "$1"; }

cmd_self_test() {
    local d; d="$(mktemp -d)"
    local f="$d/FIXTURE.md" snap="$d/snap.md"
    cat > "$f" <<'EOF'
---
ticket: FIXTURE-1
title: Fixture plan
phase: PLAN
step: 1
next: first step
updated: 2020-01-01 00:00
arch: ALL
---

# Body — must stay byte-identical
This line has: colons, and ---dashes--- and a fake key.
phase: this-is-in-the-body-not-frontmatter
EOF
    cp "$f" "$snap"
    local fail=0

    # 1. set-phase-step-next changes only phase/step/next/updated; body untouched.
    if ! cmd_set_psn "$f" BUILD 2 "second step"; then echo "self-test: set-psn failed" >&2; fail=1; fi
    grep -q '^phase: BUILD$'      "$f" || { echo "self-test: phase not set" >&2; fail=1; }
    grep -q '^step: 2$'           "$f" || { echo "self-test: step not set" >&2; fail=1; }
    grep -q '^next: second step$' "$f" || { echo "self-test: next not set" >&2; fail=1; }
    grep -q '^updated: 2020'      "$f" && { echo "self-test: updated not stamped" >&2; fail=1; }
    grep -q '^ticket: FIXTURE-1$' "$f" || { echo "self-test: ticket changed" >&2; fail=1; }
    grep -q '^arch: ALL$'         "$f" || { echo "self-test: arch changed" >&2; fail=1; }
    if ! diff <(_body "$snap") <(_body "$f") >/dev/null; then
        echo "self-test: BODY changed (must be byte-identical)" >&2; fail=1
    fi
    grep -q '^phase: this-is-in-the-body-not-frontmatter$' "$f" || { echo "self-test: body phase line altered" >&2; fail=1; }

    # 2. set-field on one key changes only that key.
    cp "$snap" "$f"
    cmd_set_field "$f" arch NG >/dev/null || { echo "self-test: set-field failed" >&2; fail=1; }
    grep -q '^arch: NG$'          "$f" || { echo "self-test: set-field did not set arch" >&2; fail=1; }
    grep -q '^phase: PLAN$'       "$f" || { echo "self-test: set-field touched phase" >&2; fail=1; }
    diff <(_body "$snap") <(_body "$f") >/dev/null || { echo "self-test: set-field touched body" >&2; fail=1; }

    # 3. missing key -> non-zero, file untouched.
    cp "$snap" "$f"
    if cmd_set_field "$f" nosuchkey x 2>/dev/null; then echo "self-test: missing key should fail" >&2; fail=1; fi
    cmp -s "$snap" "$f" || { echo "self-test: file changed on missing-key error" >&2; fail=1; }

    # 4. malformed (no frontmatter) -> non-zero, file untouched.
    printf '# no frontmatter here\nphase: x\n' > "$f"; cp "$f" "$snap"
    if cmd_set_field "$f" phase y 2>/dev/null; then echo "self-test: malformed should fail" >&2; fail=1; fi
    cmp -s "$snap" "$f" || { echo "self-test: malformed file was modified" >&2; fail=1; }

    rm -rf "$d"
    if [ "$fail" -ne 0 ]; then
        echo "plan-frontmatter self-test: FAILED" >&2
        return 1
    fi
    echo "plan-frontmatter self-test: PASSED"
    return 0
}

main() {
    local sub="${1:-}"
    [ "$#" -gt 0 ] && shift
    case "$sub" in
        set-field)            cmd_set_field "$@" ;;
        set-phase-step-next)  cmd_set_psn "$@" ;;
        --self-test)          cmd_self_test ;;
        *) echo "usage: plan-frontmatter.sh {set-field|set-phase-step-next|--self-test} ..." >&2; return 2 ;;
    esac
}

main "$@"
