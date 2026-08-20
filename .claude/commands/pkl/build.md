# /pkl:build — Compilation Check

Invoked after `/pkl:static-analyze` passes. Offers to verify the change compiles cleanly.

---

Ask:
> "Want me to run MSBuild to verify compilation?"

If user says yes — run:

```bash
msbuild PickleGit.sln /p:Configuration=Debug /p:Platform="Any CPU" /m /nologo /clp:ErrorsOnly
```

(Single-project solution — there's no separate per-project build worth splitting out.)

Report errors, fix them, and re-run until clean.
If user says no → proceed.
