# PickleGit installer

Builds a per-user, no-admin-required installer (`setup.exe`) using [Inno Setup](https://jrsoftware.org/isdl.php).

## One-time setup

Install Inno Setup 6 (default install location is auto-detected).

## Build

```powershell
.\build-installer.ps1
```

This builds `MyGitClient.csproj` in Release (which bumps the build number in `..\Version.props`
and stamps it into `PickleGit.exe`), then compiles `Setup.iss` against that build. The result lands
in `Output\PickleGit-Setup-<version>.exe`.

## What the installer does

- Installs to `%LocalAppData%\Programs\PickleGit` — no admin rights or UAC prompt needed.
- Adds a Start Menu entry (current user only) for launching and uninstalling.
- Reads its own version directly from the compiled `PickleGit.exe`'s file version, so it's always
  in sync with whatever Release build produced it.
- Re-running a newer installer upgrades the existing install in place (same `AppId` in `Setup.iss`
  — never change that GUID).

## Versioning

`Major.Minor` in `..\Version.props` is set by hand; `VersionBuild` auto-increments once per Release
build (Debug builds don't touch it). See `MyGitClient.csproj`'s `ComputeAppVersion` target.
