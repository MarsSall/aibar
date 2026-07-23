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
    [string]$PackageOutput,
    [switch]$GraphProjection,
    [string]$FixtureDirectory,
    [string]$SbomOutput,
    [string]$ComplianceOutput
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
function Write-CanonicalJson([string]$Path, $Value) { [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Compress -Depth 32) + "`n"), [Text.UTF8Encoding]::new($false)) }
function Read-Json([string]$Path) { try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 32 } catch { throw "B1_INPUT_INVALID" } }
function Property([object]$Object, [string]$Name) { return $Object.PSObject.Properties[$Name].Value }
function Sha([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Ord([object]$Items) { $strings=[string[]]@($Items); [Array]::Sort($strings,[StringComparer]::Ordinal); return @($strings) }
function Invoke-GraphProjection {
    if ([string]::IsNullOrWhiteSpace($FixtureDirectory) -or [string]::IsNullOrWhiteSpace($SbomOutput) -or [string]::IsNullOrWhiteSpace($ComplianceOutput)) { throw "B1_INPUT_INVALID" }
    $assetsPath = Join-Path $FixtureDirectory "project.assets.json"; $depsPath = Join-Path $FixtureDirectory "AIBar.Desktop.deps.json"; $inventoryPath = Join-Path $FixtureDirectory "publish-inventory.json"; $recoveryPath = Join-Path $FixtureDirectory "recovery-inventory.json"
    $assets = Read-Json $assetsPath; $deps = Read-Json $depsPath; $projection = Read-Json $inventoryPath; $inventory = @(Property $projection "artifacts"); $recovery = @(Read-Json $recoveryPath)
    $targetName = "net8.0-windows7.0/win-x64"; $target = Property (Property $assets "targets") $targetName; $depsTarget = Property (Property $deps "targets") ".NETCoreApp,Version=v8.0/win-x64"
    if ($null -eq $target -or $null -eq $depsTarget -or (Property (Property $deps "runtimeTarget") "name") -ne ".NETCoreApp,Version=v8.0/win-x64") { throw "B1_RID_MISMATCH" }; if ((Property $projection "schemaVersion") -ne "aibar-publish-projection-1") { throw "B1_INPUT_INVALID" }
    $validTypes = @("project","package","runtimepack"); $validClasses = @("first-party","managed","runtime","native","resource"); $nodes=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal); $roots=@(); $componentPolicy=Property $projection "components"; if (@($componentPolicy.PSObject.Properties).Count -ne @($target.PSObject.Properties).Count) { throw "B1_COMPONENT_IDENTITY_MISSING" }; foreach ($entry in $target.PSObject.Properties) { $metadata=Property $componentPolicy $entry.Name; if ($null -eq $metadata -or (Property $metadata "testLeakage") -ne $false) { throw "B1_COMPONENT_IDENTITY_MISSING" }; if ($validTypes -notcontains (Property $metadata "componentType") -or $validClasses -notcontains (Property $metadata "classification")) { throw "B1_COMPONENT_IDENTITY_MISSING" }; if ((Property $metadata "root") -eq $true) { $roots += $entry.Name }; $nodes.Add($entry.Name,[pscustomobject]@{dependencies=(Property $entry.Value "dependencies");componentType=(Property $metadata "componentType");classification=(Property $metadata "classification")}) }
    if ($roots.Count -ne 1 -or (Property $nodes[$roots[0]] "componentType") -ne "project") { throw "B1_COMPONENT_IDENTITY_MISSING" }; $root=$roots[0]
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); $queue = [Collections.Generic.Queue[string]]::new(); $queue.Enqueue($root)
    while ($queue.Count) { $node = $queue.Dequeue(); if (-not $seen.Add($node)) { continue }; foreach ($dependency in (Property $nodes[$node] "dependencies").PSObject.Properties) { $next = "$($dependency.Name)/$($dependency.Value)"; if (-not $nodes.ContainsKey($next)) { throw "B1_GRAPH_UNREACHABLE" }; $queue.Enqueue($next) } }
    if ($seen.Count -ne $nodes.Count) { throw "B1_GRAPH_UNREACHABLE" }
    $direct=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); foreach ($dependency in (Property $nodes[$root] "dependencies").PSObject.Properties) { $direct.Add("$($dependency.Name)/$($dependency.Value)") | Out-Null }; $owners=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal); $components = @(); foreach ($id in (Ord ([object]$nodes.Keys))) { if ($null -eq (Property $depsTarget $id)) { throw "B1_GRAPH_UNREACHABLE" }; $node = $nodes[$id]; $kind = Property $node "componentType"; $ref = if ($kind -eq "project") { "pkg:generic/aibar/desktop@1.0.0" } else { "pkg:nuget/$($id -replace '/', '@')" }; $files = @()
        foreach ($section in "runtime","native","resources") { $entries = Property (Property $depsTarget $id) $section; if ($null -eq $entries) { continue }; foreach ($asset in $entries.PSObject.Properties) { $path = $asset.Name -replace '\\','/'; $classification = Property (Property $projection "files") $path; if ($validClasses -notcontains $classification) { throw "B1_COMPONENT_IDENTITY_MISSING" }; if ($owners.ContainsKey($path)) { throw "B1_OWNER_AMBIGUOUS" }; $owners.Add($path,[pscustomobject]@{ ref=$ref; path=$path; classification=$classification }); $files += $path } }
        $components += [pscustomobject]@{ id=$id; ref=$ref; kind=$kind; classification=(Property $node "classification"); files=$files; deps=@((Property $node "dependencies").PSObject.Properties | ForEach-Object { "$($_.Name)/$($_.Value)" }) }
    }
    $indexed=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal); $casePaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase); foreach ($item in $inventory) { $key = $item.path; if (-not $casePaths.Add($key) -or $indexed.ContainsKey($key)) { throw "B1_SHIPPED_FILE_EXTRA" }; $publish = Join-Path $FixtureDirectory ("publish/" + $item.path); $source = Join-Path $FixtureDirectory ("sources/" + $item.path); if (-not (Test-Path -LiteralPath $publish -PathType Leaf) -or -not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Item -LiteralPath $publish).Length -ne $item.length -or (Sha $publish) -ne $item.sha256 -or (Sha $publish) -ne (Sha $source)) { throw "B1_SOURCE_BYTES_MISMATCH" }; if (-not $owners.ContainsKey($key)) { throw "B1_SHIPPED_FILE_EXTRA" }; $indexed.Add($key,$item) }
    foreach ($key in $owners.Keys) { if (-not $indexed.ContainsKey($key)) { throw "B1_SHIPPED_FILE_MISSING" } }; if (@((Property $projection "files").PSObject.Properties).Count -ne $owners.Count) { throw "B1_COMPONENT_IDENTITY_MISSING" }
    if ($recovery.Count -ne $inventory.Count) { throw "B1_INPUT_INVALID" }; foreach ($item in $recovery) { $actual = if ($indexed.ContainsKey($item.path)) { $indexed[$item.path] } else { $null }; if ($null -eq $actual -or $actual.length -ne $item.length -or $actual.sha256 -ne $item.sha256) { throw "B1_INPUT_INVALID" } }
    $byRef=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal); foreach ($component in $components) { $byRef.Add($component.ref,$component) }; $cyclone = @(); foreach ($ref in (Ord ([object]@($components | ForEach-Object ref)))) { $component=$byRef[$ref]; $files = @((Ord ([object]$component.files)) | ForEach-Object { $item = $indexed[$_]; [ordered]@{ 'bom-ref'="urn:aibar:file:sha256-path:$([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($_))).ToLowerInvariant())"; hashes=@([ordered]@{alg="SHA-256";content=$item.sha256}); name=$_; properties=@([ordered]@{name="aibar:classification";value=$owners[$_].classification}); type="file" } }); if ($files.Count -eq 0) { throw "B1_OWNER_MISSING" }; $scope = if ($component.id -eq $root) { "first-party" } elseif ($direct.Contains($component.id)) { "direct" } else { "transitive" }; $cyclone += [ordered]@{ 'bom-ref'=$component.ref; components=$files; name=($component.id -split '/')[0]; properties=@([ordered]@{name="aibar:component-classification";value=$component.classification},[ordered]@{name="aibar:dependency-scope";value=$scope},[ordered]@{name="aibar:distribution-state";value="shipped"}); type="library"; version=($component.id -split '/')[1] } }
    $edges = @((Ord ([object]@($components | ForEach-Object ref))) | ForEach-Object { $component=$byRef[$_]; [ordered]@{ 'dependsOn'=@(Ord ([object]@($component.deps | ForEach-Object { $byRef[$_].ref }))); ref=$component.ref } }); $sbom = [ordered]@{ bomFormat="CycloneDX"; components=$cyclone; dependencies=$edges; metadata=[ordered]@{component=[ordered]@{'bom-ref'="pkg:generic/aibar/desktop@1.0.0";name="AIBar.Desktop";type="application";version="1.0.0"}}; specVersion="1.5"; version=1 }; Write-CanonicalJson $SbomOutput $sbom
    $artifacts = @((Ord ([object]@($inventory | ForEach-Object path))) | ForEach-Object { $item=$indexed[$_]; $owner=$owners[$_]; [ordered]@{classification=$owner.classification;length=$item.length;ownerBomRef=$owner.ref;path=$item.path;sha256=$item.sha256} }); $manifest = [ordered]@{ artifacts=$artifacts; documents=@([ordered]@{path="packaging/sbom.cdx.json";sha256=(Sha $SbomOutput)}); inputs=@([ordered]@{kind="license-evidence";sha256=("0" * 64)},[ordered]@{kind="published-deps";sha256=(Sha $depsPath)},[ordered]@{kind="recovery-inventory";sha256=(Sha $recoveryPath)},[ordered]@{kind="restore-assets";sha256=(Sha $assetsPath)}); outputIdentity=(Sha $SbomOutput); schemaVersion="aibar-compliance-manifest-1" }; Write-CanonicalJson $ComplianceOutput $manifest
}
if ($GraphProjection) { Invoke-GraphProjection; exit 0 }
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
