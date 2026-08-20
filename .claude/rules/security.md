---
description: Security rules for all PickleGit code changes — path traversal, process execution, credential storage, patch application. Mandatory, no exceptions.
---

# Security — PickleGit

Standards: **OWASP Top 10**, **CWE Top 25**

Flag existing violations even when the task doesn't ask for a fix. Add `// SECURITY:` comments for non-obvious decisions.

## Checklist

- [ ] **Path traversal**: file/repo paths derived from user input (clone target, open-repo dialog, `.gitignore`-add, "reveal in Explorer") must be resolved with `Path.GetFullPath()` and validated to stay within the intended base directory before any read/write/delete
- [ ] **Process execution (`GitCli.RunAsync` / `Process.Start`)**: arguments (branch names, paths, commit messages, remote URLs) passed as a discrete argument array — never concatenated into a single shell-interpreted command string. No user-controlled value should ever pass through `cmd.exe`/`ProcessStartInfo.UseShellExecute = true`
- [ ] **Credentials / PATs**: hosting provider tokens (GitHub/GitLab/Bitbucket) and git credentials go through `CredentialStore` (Windows Credential Manager) only — never written to `settings.json`, a log file, or an exception message in plaintext
- [ ] **Logging**: `AppLog` output never includes credentials, tokens, or full remote URLs with embedded basic-auth (`https://user:pass@host/...`) — strip or redact before logging
- [ ] **File system writes**: settings/cache writes stay under `%APPDATA%\PickleGit\`; never write to a path derived from repo/branch/file names without validating it resolves inside the expected directory
- [ ] **JSON deserialization** (`Newtonsoft.Json` for `settings.json` / commit cache): treat the file as semi-trusted (a prior version of the app, or a hand-edit) — validate/clamp values after deserializing (e.g. pane widths, cached SHAs) rather than trusting them blindly; a malformed or negative value must not throw uncaught (WPF's `Width` setter throws on negative)
- [ ] **Patch application** (`PatchBuilder` → `git apply --cached`): unified-diff text built from user-selected hunks/lines must round-trip through git's own parser — never hand-construct a patch that could apply a mismatched hunk at the wrong offset
- [ ] **External processes launched on user request** ("Open in Explorer", "Open terminal here", diff/merge tool launch): validate the target path exists and is inside the repo/expected location before invoking `Process.Start`
- [ ] **`BinaryFormatter` / unsafe deserialization**: not currently used in this codebase — if ever introduced, never deserialize from a file the user didn't create with this same app version
- [ ] **Reflection / dynamic type loading**: not currently used for anything security-relevant in this codebase; if introduced, validate any external type name against an allowlist

For full audit, use `/pkl:security-review`.
