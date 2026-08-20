# security-review

Run a focused security checklist against the file or component specified in `$ARGUMENTS`.

## Instructions

Read every file in the specified target, then check each item in the following checklist. Report findings with file path and line number. Rate each finding: **CRITICAL** / **HIGH** / **MEDIUM** / **LOW**.

---

## Checklist

### 1. Path Traversal
- [ ] Any file/repo path derived from user input (clone target, open-repo dialog, `.gitignore`-add, "reveal in Explorer")?
  - Resolved with `Path.GetFullPath()` before use?
  - Validated to stay within the intended base directory?
- [ ] Any file written to a path derived from external input without that validation?

### 2. Hardcoded Credentials / Secrets
- [ ] Hardcoded tokens, PATs, or credentials in source?
- [ ] Any credential embedded in a committed config or settings file?

### 3. Credential Storage
- [ ] Hosting provider tokens (GitHub/GitLab/Bitbucket) and git credentials go through `CredentialStore` (Windows Credential Manager) only?
- [ ] Nothing sensitive persisted to `settings.json` in plaintext?

### 4. Process / Shell Execution
- [ ] `GitCli.RunAsync` / `Process.Start()` called with arguments from user input, branch names, paths, or commit messages?
- [ ] Arguments passed as a discrete argument list, not concatenated into a shell-interpreted string?
- [ ] `ProcessStartInfo.UseShellExecute` not set `true` for anything carrying user-controlled input?

### 5. Deserialization
- [ ] `settings.json` / commit-cache JSON (Newtonsoft.Json) values validated/clamped after deserializing before being used (e.g. as a `Width`, an index, a path)?
- [ ] `BinaryFormatter` or similar unsafe deserialization introduced anywhere? (None should exist in this codebase.)

### 6. Patch Application
- [ ] Unified-diff text built by `PatchBuilder` for hunk/line staging round-trips correctly through `git apply` rather than being hand-assembled with assumed offsets?

### 7. Logging / Secrets in Logs
- [ ] `AppLog` output free of credentials, tokens, or full remote URLs with embedded basic-auth (`https://user:pass@host/...`)?

### 8. Reflection / Dynamic Type Loading
- [ ] Any new `Activator.CreateInstance()` / `Type.GetType()` call with a type name from external input? (Not currently used anywhere security-relevant in this codebase.)

---

## Output Format

For each finding:
```
[SEVERITY] <Category>
File: <path>:<line>
Issue: <description>
Risk: <what could go wrong>
Fix: <recommended change>
```

End with a summary count by severity and an overall risk rating.
