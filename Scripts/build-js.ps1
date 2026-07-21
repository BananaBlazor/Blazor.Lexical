#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the Blazor.Lexical ESM bundle (wwwroot/blazor-lexical.mjs).

.DESCRIPTION
    Installs the npm packages under Source/Blazor.Lexical/js when they are missing
    (or when -Force is given), then bundles src/index.ts with esbuild. esbuild
    code-splits the output into the entry (blazor-lexical.mjs), a shared core
    chunk, and a lazily-loaded table chunk (blazor-lexical-chunk-<hash>.mjs), so
    stale hashed chunks from a previous build are cleared first.

    MSBuild invokes this automatically when the bundle is missing or its
    sources changed, so the Lexical JS never needs to be checked in. Run it
    by hand after editing js/src to refresh the committed-nowhere bundle.

.PARAMETER Force
    Reinstall npm packages even if node_modules already exists.
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$jsDir = Join-Path $PSScriptRoot '..' 'Source' 'Blazor.Lexical' 'js'
$jsDir = (Resolve-Path $jsDir).Path
$nodeModules = Join-Path $jsDir 'node_modules'
$lockFile = Join-Path $jsDir 'package-lock.json'

Push-Location $jsDir
try {
    if ($Force -or -not (Test-Path $nodeModules)) {
        if (Test-Path $lockFile) {
            Write-Host 'Blazor.Lexical: installing npm packages (npm ci)...'
            npm ci
        }
        else {
            Write-Host 'Blazor.Lexical: installing npm packages (npm install)...'
            npm install
        }
        if ($LASTEXITCODE -ne 0) { throw "npm install failed (exit $LASTEXITCODE)" }
    }

    # Clear the previous build's entry + hashed chunks so no stale chunk files
    # (whose hashes change whenever the sources do) linger in wwwroot and get
    # picked up as static web assets.
    $wwwroot = (Resolve-Path (Join-Path $jsDir '..' 'wwwroot')).Path
    Get-ChildItem -Path $wwwroot -Filter 'blazor-lexical*.mjs' -ErrorAction SilentlyContinue |
        Remove-Item -Force

    Write-Host 'Blazor.Lexical: bundling wwwroot/blazor-lexical.mjs (+ chunks)...'
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed (exit $LASTEXITCODE)" }

    # Publish the extension module contract as a declaration file, so extension
    # authors can type their JS half against the shipped SDK. src/extension.ts
    # declares types only (it is never imported at runtime), so a verbatim copy is
    # a valid .d.ts.
    Copy-Item -Path (Join-Path $jsDir 'src' 'extension.ts') `
              -Destination (Join-Path $wwwroot 'blazor-lexical-extension.d.ts') -Force
}
finally {
    Pop-Location
}
