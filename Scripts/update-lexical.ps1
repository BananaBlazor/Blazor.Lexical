#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Updates the pinned Lexical package versions and rebuilds the bundle.

.DESCRIPTION
    Sets lexical and every @lexical/* dependency to the given version
    in Source/Blazor.Lexical/js/package.json, refreshes package-lock.json / node_modules
    via npm install, then rebuilds wwwroot/blazor-lexical.mjs. Review and commit
    the changed package.json and package-lock.json afterward.

.PARAMETER Version
    The Lexical version to pin (e.g. 0.49.0).

.EXAMPLE
    ./Scripts/update-lexical.ps1 -Version 0.49.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$jsDir = (Resolve-Path (Join-Path $PSScriptRoot '..' 'Source' 'Blazor.Lexical' 'js')).Path
$pkgPath = Join-Path $jsDir 'package.json'

$pkg = Get-Content $pkgPath -Raw | ConvertFrom-Json

# Every @lexical/* package plus lexical itself is released in lockstep, so pin them
# all to the same version. The family is DISCOVERED from package.json rather than
# listed here — the same thing Scripts/lexical-version.mjs does when it enforces
# lockstep. A hardcoded list silently misses a package added later (which is exactly
# how @lexical/text, and then @lexical/mark, got left behind), and the publish gate
# would then fail the next build with no obvious cause.
$lexicalPackages = @(
    $pkg.dependencies.PSObject.Properties.Name |
        Where-Object { $_ -eq 'lexical' -or $_ -like '@lexical/*' } |
        Sort-Object
)

if ($lexicalPackages.Count -eq 0) {
    throw "No 'lexical'/'@lexical/*' dependencies found in $pkgPath."
}

foreach ($name in $lexicalPackages) {
    Write-Host "  $name : $($pkg.dependencies.$name) -> $Version"
    $pkg.dependencies.$name = $Version
}

($pkg | ConvertTo-Json -Depth 32) | Set-Content $pkgPath -Encoding utf8
Write-Host "Updated package.json to Lexical $Version."

Push-Location $jsDir
try {
    Write-Host 'Refreshing lockfile and node_modules (npm install)...'
    npm install
    if ($LASTEXITCODE -ne 0) { throw "npm install failed (exit $LASTEXITCODE)" }
}
finally {
    Pop-Location
}

# Rebuild the bundle against the new versions.
& (Join-Path $PSScriptRoot 'build-js.ps1')

Write-Host "Done. Review and commit package.json + package-lock.json." -ForegroundColor Green
