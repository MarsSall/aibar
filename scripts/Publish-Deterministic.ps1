[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$SourceDateEpoch = "1767225600",
    [string]$PublishCommand = "dotnet",
    [switch]$CapabilityPlan,
    [string]$MakeAppxPath,
    [string]$SignToolPath,
    [string]$CertificateSubject,
    [string]$Secret,
    [switch]$SigningRequested,
    [string]$PackageOutput
)
$ErrorActionPreference = "Stop"
function Write-CapabilityPlan {
    $publisher = "CN=AIBar Development"
    $hasMakeAppx = -not [string]::IsNullOrWhiteSpace($MakeAppxPath)
    $staleOutput = -not [string]::IsNullOrWhiteSpace($PackageOutput) -and (Test-Path -LiteralPath $PackageOutput)
    $hasSigningInputs = -not [string]::IsNullOrWhiteSpace($SignToolPath) -and -not [string]::IsNullOrWhiteSpace($CertificateSubject) -and -not [string]::IsNullOrWhiteSpace($Secret)
    $publisherMatches = $CertificateSubject -eq $publisher
    $msixStatus = if ($staleOutput) { "stale-output" } elseif (-not $hasMakeAppx) { "tool-unavailable" } else { "unsigned" }
    $signingStatus = "signing-unavailable"
    if ($SigningRequested) {
        if (-not $publisherMatches) { $signingStatus = "publisher-mismatch" } elseif (-not $hasSigningInputs) { $signingStatus = "requested-signing-unavailable" } else { $signingStatus = "planned" }
        if ($msixStatus -ne "stale-output" -and ($signingStatus -ne "planned" -or -not $hasMakeAppx)) { $msixStatus = "failed" }
    }
    $commands = [System.Collections.Generic.List[string]]::new()
    if ($signingStatus -eq "planned" -and $msixStatus -eq "unsigned") { $commands.Add("MakeAppx pack"); $commands.Add("SignTool sign") }
    [pscustomobject]@{ msixStatus = $msixStatus; signingStatus = $signingStatus; publisher = $publisher; commands = $commands } | ConvertTo-Json -Compress
}
if ($CapabilityPlan) { Write-CapabilityPlan; exit 0 }
function Is-Reparse([string]$Path) { return ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }
function Assert-FreshLeaf([string]$Leaf, [string]$Repository) {
    if ([string]::IsNullOrWhiteSpace($Leaf)) { throw "OutputDirectory is required." }
    $root = [IO.Path]::GetFullPath($Leaf); $trimmed = $root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $volume = [IO.Path]::GetPathRoot($root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if ($trimmed -eq $volume) { throw "OutputDirectory must not be a filesystem root." }
    $sep = [IO.Path]::DirectorySeparatorChar
    if ($root -eq $Repository -or $root.StartsWith($Repository + $sep, [StringComparison]::OrdinalIgnoreCase) -or $Repository.StartsWith($root + $sep, [StringComparison]::OrdinalIgnoreCase)) { throw "OutputDirectory must not overlap repository inputs." }
    $parent = [IO.Path]::GetDirectoryName($root)
    if ([string]::IsNullOrWhiteSpace($parent) -or -not (Test-Path -LiteralPath $parent -PathType Container)) { throw "OutputDirectory parent must be an existing directory." }
    try { $item = Get-Item -LiteralPath $parent -Force; if (-not ($item -is [IO.DirectoryInfo])) { throw "OutputDirectory parent is not a directory." } } catch { throw "OutputDirectory parent cannot be classified safely." }
    for ($current = $parent; ; $current = [IO.DirectoryInfo]::new($current).Parent.FullName) { if (Is-Reparse $current) { throw "OutputDirectory ancestor must not be a reparse point." }; if ([IO.Path]::GetPathRoot($current).TrimEnd('\','/') -eq $current.TrimEnd('\','/')) { break } }
    if (Get-ChildItem -LiteralPath $parent -Force | Where-Object Name -eq ([IO.Path]::GetFileName($root))) { throw "OutputDirectory must be a nonexistent leaf." }
    return $root
}
try {
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { throw "OutputDirectory is required." }
    try { $epoch = [long]::Parse($SourceDateEpoch, [Globalization.CultureInfo]::InvariantCulture) } catch { throw "SourceDateEpoch must be an integer." }
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..")); $outputRoot = Assert-FreshLeaf $OutputDirectory $projectRoot
    $outputRoot = Assert-FreshLeaf $outputRoot $projectRoot
    New-Item -ItemType Directory -Path $outputRoot -ErrorAction Stop | Out-Null; $created = $true
    $publish = Join-Path $outputRoot "publish"; New-Item -ItemType Directory -Path $publish -ErrorAction Stop | Out-Null
    & $PublishCommand publish (Join-Path $projectRoot "src\AIBar.Desktop\AIBar.Desktop.csproj") --disable-build-servers --configuration Release --runtime win-x64 --self-contained true --output $publish /p:ContinuousIntegrationBuild=true /p:Deterministic=true /p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw "Self-contained win-x64 publish failed." }
    $files = @{}; Get-ChildItem -LiteralPath $publish -Recurse -File | ForEach-Object { $relative = $_.FullName.Substring($publish.Length).TrimStart('\','/') -replace '\\','/'; if ($relative.StartsWith('/') -or $relative.Split('/') -contains '..' -or $files.ContainsKey($relative)) { throw "Unsafe or duplicate publish path." }; $files[$relative] = [ordered]@{ path = $relative; length = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash } }
    $paths = [string[]]$files.Keys; [Array]::Sort($paths, [StringComparer]::Ordinal); $inventory = @($paths | ForEach-Object { [pscustomobject]$files[$_] })
    function Write-Json([string]$Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Compress -Depth 4), [Text.UTF8Encoding]::new($false)) }
    $inventoryPath = Join-Path $outputRoot "recovery-inventory.json"; Write-Json $inventoryPath $inventory
    Add-Type -AssemblyName System.IO.Compression; Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath = Join-Path $outputRoot "AIBar-win-x64-recovery.zip"; $stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew); $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try { foreach ($relative in $paths) { $entry = $zip.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal); $entry.LastWriteTime = [DateTimeOffset]::FromUnixTimeSeconds($epoch); $entry.ExternalAttributes = 0; $input = [IO.File]::OpenRead((Join-Path $publish ($relative -replace '/','\\'))); try { $target = $entry.Open(); try { $input.CopyTo($target) } finally { $target.Dispose() } } finally { $input.Dispose() } } } finally { $zip.Dispose(); $stream.Dispose() }
    $inventoryHash = (Get-FileHash $inventoryPath -Algorithm SHA256).Hash; $manifest = [ordered]@{ target = "win-x64"; configuration = "Release"; publishInventorySha256 = $inventoryHash; recoveryZipSha256 = (Get-FileHash $zipPath -Algorithm SHA256).Hash; recoveryInventorySha256 = $inventoryHash }; Write-Json (Join-Path $outputRoot "artifact-manifest.json") $manifest
} catch { if ($created) { [Console]::Error.WriteLine("PUBLISH_INCOMPLETE_OUTPUT=$outputRoot") }; throw }
