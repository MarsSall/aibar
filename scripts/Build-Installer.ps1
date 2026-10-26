# Requires PowerShell 7, like Publish-Deterministic.ps1.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseDirectory,
    [string]$IsccPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Find-Iscc {
    if (-not [string]::IsNullOrWhiteSpace($IsccPath)) {
        if (-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) { throw "INSTALLER_ISCC_MISSING: $IsccPath" }
        return (Resolve-Path -LiteralPath $IsccPath).Path
    }
    $command = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { return $command.Source }
    foreach ($base in @($env:LOCALAPPDATA, ${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ([string]::IsNullOrWhiteSpace($base)) { continue }
        $relative = if ($base -eq $env:LOCALAPPDATA) { 'Programs\Inno Setup 6\ISCC.exe' } else { 'Inno Setup 6\ISCC.exe' }
        $candidate = Join-Path $base $relative
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw 'INSTALLER_ISCC_MISSING: Install Inno Setup 6 or pass -IsccPath.'
}

$setupPath = $null
$checksumTemporary = $null
$ownsSetup = $false
try {
    $output = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
    $publish = Join-Path $output 'publish'
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'AIBar.Desktop.exe') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $publish 'yasb') -PathType Container) -or
        -not (Test-Path -LiteralPath (Join-Path $publish 'fonts') -PathType Container)) {
        throw 'INSTALLER_PUBLISH_MISSING: Expected the T1 publish tree with AIBar.Desktop.exe, yasb and fonts.'
    }
    $manifest = Get-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Raw | ConvertFrom-Json
    $version = [string]$manifest.version
    # Versions become compiler arguments and filenames: reject path/define injection.
    if ($version -cnotmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?(?:\+[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
        throw 'INSTALLER_VERSION_INVALID: Expected a semantic version in release-manifest.json.'
    }
    if ($manifest.schemaVersion -ne 'aibar-release-1' -or $manifest.target -ne 'win-x64') {
        throw 'INSTALLER_MANIFEST_INVALID: Expected a T1 Windows x64 release manifest.'
    }
    $sumsPath = Join-Path $output 'SHA256SUMS.txt'
    $originalSums = [IO.File]::ReadAllText($sumsPath)
    $basename = "AIBar-$version-win-x64-setup"
    $setupPath = Join-Path $output "$basename.exe"
    # Do not overwrite an existing artifact or create duplicate checksum entries.
    if (Test-Path -LiteralPath $setupPath) { throw 'INSTALLER_OUTPUT_EXISTS: Use a release directory without a setup artifact.' }
    if ($originalSums -match "(?m)  $([regex]::Escape("$basename.exe"))\r?$") { throw 'INSTALLER_CHECKSUM_EXISTS' }
    $compiler = Find-Iscc
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $script = Join-Path $repository 'packaging\installer\AIBar.iss'
    $arguments = @("/DAppVersion=$version", "/DPublishDirectory=$publish", "/DRepositoryRoot=$repository", "/O$output", "/F$basename", $script)
    $ownsSetup = $true
    $LASTEXITCODE = 0
    & $compiler @arguments
    if (-not $? -or $LASTEXITCODE -ne 0) { throw "INSTALLER_ISCC_FAILED: exit code $LASTEXITCODE" }
    if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf) -or (Get-Item -LiteralPath $setupPath).Length -eq 0) {
        throw 'INSTALLER_OUTPUT_MISSING: ISCC did not produce the expected setup.'
    }
    $hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $separator = if ($originalSums.Length -gt 0 -and -not $originalSums.EndsWith("`n")) { "`n" } else { '' }
    $line = "$hash  $basename.exe"
    # Replace checksums only after a successful build; failure leaves the old bytes intact.
    $checksumTemporary = Join-Path $output ('.installer-checksums-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllText($checksumTemporary, "$originalSums$separator$line`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($checksumTemporary, $sumsPath, $true)
    $ownsSetup = $false
    Write-Output $setupPath
    Write-Output $line
} catch {
    if ($ownsSetup -and $null -ne $setupPath -and (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
        Remove-Item -LiteralPath $setupPath -Force
    }
    if ($null -ne $checksumTemporary -and (Test-Path -LiteralPath $checksumTemporary -PathType Leaf)) {
        Remove-Item -LiteralPath $checksumTemporary -Force
    }
    [Console]::Error.WriteLine("INSTALLER_BUILD_FAILED: $($_.Exception.Message)")
    exit 1
}
