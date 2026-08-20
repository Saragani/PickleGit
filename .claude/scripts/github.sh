#!/usr/bin/env bash
#
# github.sh — unified GitHub Issues operations for the pkl: skills (host-side dev tooling).
#
# Single source of truth for the deterministic GitHub API mechanics that the pkl:
# issue skills would otherwise embed inline (transport selection, owner/repo
# detection, JSON building). Each skill calls a subcommand here and keeps only
# its judgment (summarizing, comment composition, type choice) in prose.
#
# Transport (picked once per invocation, not retried per-call — a retry-after-
# failure fallback could double-post a mutating call like `comment`):
#   1. PRIMARY   — `gh api`, if `gh` is installed and `gh auth status` succeeds.
#                  Uses gh's own stored credential; no token env var needed.
#   2. FALLBACK  — raw REST via curl, if gh is missing/unauthenticated. Needs a
#                  token in $GITHUB_TOKEN (classic or fine-grained PAT with
#                  `repo` scope for a private repo, `public_repo` for a public
#                  one). Never printed, never placed on argv — passed to curl
#                  via a --config file (see _api_call_rest for why not a
#                  process-substitution fd — the Windows curl build can't read one).
#
# Both transports hit the same REST endpoints and return the same JSON shape,
# so the response parsers below are shared regardless of which one ran.
#
# Failure contract
#   Every subcommand exits non-zero with NO partial/garbage on stdout when the
#   call fails — output is built in full and printed only on success.
#
# Owner/repo: derived from `git remote get-url origin` unless GITHUB_REPO
# (`owner/repo`) is set explicitly — set it to override when origin isn't GitHub
# or points somewhere other than the repo you want to operate on.
#
# Subcommands:
#   view <N>                         Print an issue as labeled plain-text blocks
#   comment <N> <body-source>        Post a comment; <body-source> is a file or '-' (stdin)
#   close <N>                        Close an issue (state=closed)
#   create --summary <s> [flags]     Create an issue
#
# Usage:
#   github.sh view <N>
#   github.sh comment <N> <body-file>     # or '-' to read the body from stdin
#   github.sh close <N>
#   github.sh create --summary <s> [--body <b>] [--label <l>] [--assignee <a>]

API="https://api.github.com"

# --- transport selection ----------------------------------------------------
_have_gh() {
    command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1
}

# --- owner/repo detection ----------------------------------------------------
_repo_slug() {
    if [ -n "${GITHUB_REPO:-}" ]; then
        printf '%s' "$GITHUB_REPO"
        return 0
    fi
    local url
    url="$(git remote get-url origin 2>/dev/null)" || { echo "github.sh: no 'origin' remote and GITHUB_REPO not set" >&2; return 1; }
    # Accept both:  git@github.com:owner/repo.git  and  https://github.com/owner/repo(.git)
    local slug
    slug="$(printf '%s' "$url" | sed -E 's#^git@github\.com:##; s#^https?://github\.com/##; s#\.git$##')"
    if [ -z "$slug" ] || [ "$slug" = "$url" ]; then
        echo "github.sh: origin remote '$url' is not a github.com URL; set GITHUB_REPO=owner/repo" >&2
        return 1
    fi
    printf '%s' "$slug"
}

require_token() {
    if [ -z "${GITHUB_TOKEN:-}" ]; then
        echo "github.sh: gh CLI unavailable/unauthenticated and GITHUB_TOKEN not set — no working transport" >&2
        return 1
    fi
}

require_curl() {
    if ! command -v curl >/dev/null 2>&1; then
        echo "github.sh: curl not found in PATH" >&2
        return 1
    fi
}

# _read_body_source <src>  ->  prints body text. <src>: a file path, or '-' for stdin.
_read_body_source() {
    local src="$1"
    if [ "$src" = "-" ]; then
        cat
    elif [ -r "$src" ]; then
        cat "$src"
    else
        echo "github.sh: cannot read body source: $src" >&2
        return 1
    fi
}

# _api_call_gh <method> <path> [<body-source>]  ->  prints response body on success.
# <path> is relative to the repo, e.g. "/issues/5". <body-source>: '-' reads raw
# JSON from this function's stdin; omit for a bodyless request (GET).
_api_call_gh() {
    local method="$1" path="$2" src="${3:-}" repo
    repo="$(_repo_slug)" || return 1
    local endpoint="repos/$repo$path"

    local body_input=""
    if [ -n "$src" ]; then
        body_input="$(_read_body_source "$src")" || return 1
    fi

    local out rc errfile
    errfile="$(mktemp)"
    if [ -n "$body_input" ]; then
        out="$(printf '%s' "$body_input" | gh api --method "$method" "$endpoint" --input - 2>"$errfile")"
    else
        out="$(gh api --method "$method" "$endpoint" 2>"$errfile")"
    fi
    rc=$?
    local err; err="$(cat "$errfile")"; rm -f "$errfile"
    if [ $rc -ne 0 ]; then
        echo "github.sh: gh api $method $endpoint failed" >&2
        [ -n "$err" ] && printf '%s\n' "$err" >&2
        return 1
    fi
    printf '%s' "$out"
}

# _api_call_rest <method> <path> [<body-source>]  ->  prints response body on success.
# Same contract as _api_call_gh, over raw REST via curl + $GITHUB_TOKEN.
#
# Token handling: the mingw64 curl shipped with Git for Windows cannot read a
# --config file from a /dev/fd/N process-substitution path (it errors with
# "option --config: error encountered when reading a file") — that build has
# no real fd-table access the way Linux/macOS curl does. So the token header
# goes into a real temp file instead: mktemp (private per-user temp dir),
# chmod 600 best-effort, and an unconditional `rm -f` immediately after the
# curl call (both success and failure paths) so it never outlives this call.
_api_call_rest() {
    local method="$1" path="$2" src="${3:-}"
    require_curl || return 1
    require_token || return 1
    local repo; repo="$(_repo_slug)" || return 1

    local body_input=""
    if [ -n "$src" ]; then
        body_input="$(_read_body_source "$src")" || return 1
    fi

    local cfg; cfg="$(mktemp)" || { echo "github.sh: mktemp failed" >&2; return 1; }
    chmod 600 "$cfg" 2>/dev/null
    printf 'header = "Authorization: token %s"\n' "$GITHUB_TOKEN" > "$cfg"

    local resp http rbody
    if [ -n "$body_input" ]; then
        resp="$(printf '%s' "$body_input" | curl -s -w $'\n%{http_code}' \
            --config "$cfg" \
            -H "Accept: application/vnd.github+json" -H "Content-Type: application/json" \
            -X "$method" "$API/repos/$repo$path" -d @-)"
    else
        resp="$(curl -s -w $'\n%{http_code}' \
            --config "$cfg" \
            -H "Accept: application/vnd.github+json" \
            -X "$method" "$API/repos/$repo$path")"
    fi
    rm -f "$cfg"

    http="$(printf '%s' "$resp" | tail -n1)"
    rbody="$(printf '%s' "$resp" | sed '$d')"

    case "$http" in
        2??)
            printf '%s' "$rbody"
            return 0
            ;;
        *)
            echo "github.sh: REST $method $path failed (HTTP ${http:-none})" >&2
            [ -n "$rbody" ] && printf '%s\n' "$rbody" >&2
            return 1
            ;;
    esac
}

# _api_call <method> <path> [<body-source>]  ->  dispatches to whichever transport is live.
_api_call() {
    if _have_gh; then
        _api_call_gh "$@"
    else
        _api_call_rest "$@"
    fi
}

# --- Python helper: issue JSON (stdin) -> the view contract (stdout) ---
IFS= read -r -d '' GH_VIEW_PY <<'PY'
import json, sys

raw = sys.stdin.read()
try:
    d = json.loads(raw)
except Exception:
    sys.stderr.write("github.sh: invalid JSON from API\n")
    sys.exit(1)

title = d.get("title")
if not title:
    sys.stderr.write("github.sh: no title field in response\n")
    sys.exit(1)

state = d.get("state", "")
labels = ", ".join(l.get("name", "") for l in (d.get("labels") or []))
assignee = (d.get("assignee") or {}).get("login", "Unassigned")
body = (d.get("body") or "").strip()

out = []
out.append("Title: " + str(title))
out.append("Status: " + state)
out.append("Labels: " + (labels or "(none)"))
out.append("Assignee: " + (assignee or "Unassigned"))
out.append("")
out.append("Description:")
out.append(body if body else "(none)")
sys.stdout.write("\n".join(out) + "\n")
PY

# --- Python helper: comment-list JSON (stdin) -> plain-text block (stdout) ---
IFS= read -r -d '' GH_COMMENTS_PY <<'PY'
import json, sys

raw = sys.stdin.read()
try:
    comments = json.loads(raw)
except Exception:
    sys.stderr.write("github.sh: invalid JSON from API\n")
    sys.exit(1)

out = ["", "Comments (%d):" % len(comments)]
for c in comments:
    author = (c.get("user") or {}).get("login", "?")
    date = (c.get("created_at") or "")[:10]
    body = (c.get("body") or "").replace("\n", " ")
    out.append("[%s] %s: %s" % (date, author, body[:200]))
sys.stdout.write("\n".join(out) + "\n")
PY

usage() {
    cat <<'EOF'
github.sh — unified GitHub Issues operations for the pkl: skills

Usage:
  github.sh view <N>                 Print an issue as labeled plain-text blocks
  github.sh comment <N> <src>        Post a comment; <src> is a body file or '-' (stdin)
  github.sh close <N>                Close an issue
  github.sh create --summary <s> [--body <b>] [--label <l>] [--assignee <a>]

Transport: `gh api` if `gh` is installed and authenticated (no token needed);
otherwise raw REST via curl + $GITHUB_TOKEN. Owner/repo comes from
`git remote get-url origin`, or $GITHUB_REPO (owner/repo) to override.

On non-zero exit, the calling skill reports the failure to the user — there is
no MCP fallback for GitHub issues in this project.
EOF
}

# view <N>
cmd_view() {
    local n="$1"
    if [ -z "$n" ]; then
        echo "usage: github.sh view <N>" >&2
        return 2
    fi

    local body
    body="$(_api_call GET "/issues/$n")" || return 1

    local rendered
    rendered="$(printf '%s' "$body" | python3 -c "$GH_VIEW_PY")" || return 1

    local cbody comments=""
    if cbody="$(_api_call GET "/issues/$n/comments" 2>/dev/null)"; then
        comments="$(printf '%s' "$cbody" | python3 -c "$GH_COMMENTS_PY")"
    fi

    printf '%s\n%s' "$rendered" "$comments"
}

# comment <N> <body-source>
cmd_comment() {
    local n="$1" src="$2"
    if [ -z "$n" ] || [ -z "$src" ]; then
        echo "usage: github.sh comment <N> <body-file|->" >&2
        return 2
    fi

    local body_input
    body_input="$(_read_body_source "$src")" || return 1

    local payload
    payload="$(python3 -c "import json,sys; print(json.dumps({'body': sys.stdin.read()}))" <<<"$body_input")" || {
        echo "github.sh: failed to build comment payload" >&2
        return 1
    }

    printf '%s' "$payload" | _api_call POST "/issues/$n/comments" - >/dev/null || return 1
    echo "github.sh: comment posted to #$n"
}

# close <N>
cmd_close() {
    local n="$1"
    if [ -z "$n" ]; then
        echo "usage: github.sh close <N>" >&2
        return 2
    fi

    printf '%s' '{"state":"closed"}' | _api_call PATCH "/issues/$n" - >/dev/null || return 1
    echo "github.sh: closed #$n"
}

# create --summary <s> [--body <b>] [--label <l>] [--assignee <a>]
cmd_create() {
    local summary="" body="" label="" assignee=""
    while [ $# -gt 0 ]; do
        case "$1" in
            --summary|-s)  summary="$2";  shift 2 ;;
            --body|-d)     body="$2";     shift 2 ;;
            --label|-l)    label="$2";    shift 2 ;;
            --assignee|-a) assignee="$2"; shift 2 ;;
            *) echo "github.sh: unknown create flag: $1" >&2; return 2 ;;
        esac
    done

    if [ -z "$summary" ]; then
        echo "usage: github.sh create --summary <text> [--body <text>] [--label <l>] [--assignee <a>]" >&2
        return 2
    fi

    local payload
    payload="$(python3 -c "
import json, sys
title, body, label, assignee = sys.argv[1:5]
d = {'title': title}
if body: d['body'] = body
if label: d['labels'] = [x.strip() for x in label.split(',') if x.strip()]
if assignee: d['assignees'] = [assignee]
print(json.dumps(d))
" "$summary" "$body" "$label" "$assignee")" || {
        echo "github.sh: failed to build create payload" >&2
        return 1
    }

    local rbody
    rbody="$(printf '%s' "$payload" | _api_call POST "/issues" -)" || return 1

    printf '%s' "$rbody" | python3 -c "
import json, sys
d = json.load(sys.stdin)
print('Created: ' + d.get('html_url', '(no url)'))
"
}

main() {
    local sub="${1:-}"
    [ $# -gt 0 ] && shift
    case "$sub" in
        view)           cmd_view "$@" ;;
        comment)        cmd_comment "$@" ;;
        close)          cmd_close "$@" ;;
        create)         cmd_create "$@" ;;
        -h|--help|help) usage; return 0 ;;
        "")             echo "github.sh: missing subcommand" >&2; usage >&2; return 2 ;;
        *)              echo "github.sh: unknown subcommand: $sub" >&2; usage >&2; return 2 ;;
    esac
}

main "$@"
