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

# Every @lexical/* package plus lexical itself is released in lockstep, so pin
# them all to the same version. Keep this list in sync with the dependencies in
# Source/Blazor.Lexical/js/package.json.
$lexicalPackages = @(
    'lexical',
    '@lexical/rich-text',
    '@lexical/history',
    '@lexical/html',
    '@lexical/markdown',
    '@lexical/list',
    '@lexical/link',
    '@lexical/table',
    '@lexical/selection',
    '@lexical/utils'
)

$pkg = Get-Content $pkgPath -Raw | ConvertFrom-Json

# Remember the Lexical minor we are moving away from, so we can reset the package
# serial when a new Lexical-minor line begins (see Version.props).
$oldVersion = $pkg.dependencies.lexical
foreach ($name in $lexicalPackages) {
    if ($pkg.dependencies.PSObject.Properties.Name -contains $name) {
        Write-Host "  $name : $($pkg.dependencies.$name) -> $Version"
        $pkg.dependencies.$name = $Version
    }
    else {
        Write-Warning "package.json has no dependency '$name' — skipping."
    }
}

($pkg | ConvertTo-Json -Depth 32) | Set-Content $pkgPath -Encoding utf8
Write-Host "Updated package.json to Lexical $Version."

# Reset the package serial to 0 when the Lexical MINOR changes: each Lexical-minor
# line starts its own serial. A same-minor bump (a Lexical patch) keeps the serial,
# since it is still the next release within the current line.
$oldMinor = ($oldVersion -split '\.')[1]
$newMinor = ($Version -split '\.')[1]
if ($oldMinor -ne $newMinor) {
    $propsPath = (Resolve-Path (Join-Path $jsDir '..' 'Version.props')).Path
    $props = Get-Content $propsPath -Raw
    $props = [regex]::Replace($props, '(<LexicalPackageSerial>)\d+(</LexicalPackageSerial>)', '${1}0${2}')
    Set-Content $propsPath $props -Encoding utf8 -NoNewline
    Write-Host "Lexical minor changed ($oldMinor -> $newMinor): reset LexicalPackageSerial to 0."
}

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
