<#
.SYNOPSIS
    Builds PickleGit in Release configuration (bumping the auto-incrementing build number) and
    then compiles the Inno Setup installer from the result.

.DESCRIPTION
    Two steps:
      1. MSBuild Release build of MyGitClient.csproj - this increments VersionBuild in
         ..\Version.props and stamps the new version into PickleGit.exe.
      2. ISCC.exe (Inno Setup Compiler) compiles Setup.iss, which reads the version straight back
         out of the just-built exe, producing Installer\Output\PickleGit-Setup-<version>.exe.

    Run this from anywhere; paths below are relative to this script's own location.
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csproj = Join-Path $root '..\MyGitClient\MyGitClient.csproj'
$setupScript = Join-Path $root 'Setup.iss'

function Find-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        if ($vsPath) {
            $candidate = Join-Path $vsPath 'MSBuild\Current\Bin\amd64\MSBuild.exe'
            if (Test-Path $candidate) { return $candidate }
            $candidate = Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
            if (Test-Path $candidate) { return $candidate }
        }
    }
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "MSBuild.exe not found. Install Visual Studio (or the Build Tools) and try again."
}

function Find-Iscc {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "ISCC.exe (Inno Setup Compiler) not found. Install Inno Setup 6 from https://jrsoftware.org/isdl.php and try again."
}

$msbuild = Find-MSBuild
$iscc = Find-Iscc

Write-Host "Building Release ($csproj)..." -ForegroundColor Cyan
& $msbuild $csproj /p:Configuration=Release /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Release build failed (exit code $LASTEXITCODE)." }

Write-Host "Compiling installer ($setupScript)..." -ForegroundColor Cyan
& $iscc $setupScript
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit code $LASTEXITCODE)." }

Write-Host "Done - see Installer\Output\" -ForegroundColor Green
