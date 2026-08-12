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
    [string]$ComplianceOutput,
    [string]$IsolationParent,
    [string]$IsolationMarker,
    [string]$IntermediateDirectory,
    [string]$BuildOutputDirectory,
    [string]$RestoreMetadataDirectory,
    [string]$PackageCacheDirectory,
    [ValidateRange(1, 600)]
    [int]$ProcessTimeoutSeconds = 300,
    [ValidateRange(0, 600000)]
    [int]$CancelAfterMilliseconds = 0,
    [string]$KnownDescendantIdentityPath,
    [switch]$PrivateBeta,
    [string]$BetaBaselineCommit,
    [string]$BetaParentCommit,
    [ValidateRange(1, 30000)]
    [int]$SmokeStartupMilliseconds = 1000
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
function Assert-IsolationState([string]$Parent, [string]$Marker, [string]$Repository) {
    if (-not [IO.Path]::IsPathFullyQualified($Parent)) { throw "Isolation root must be absolute." }
    $full = [IO.Path]::GetFullPath($Parent); $volume = [IO.Path]::GetPathRoot($full).TrimEnd('\','/')
    if ([string]::IsNullOrWhiteSpace($Marker) -or $full.TrimEnd('\','/') -eq $volume -or -not (Test-Path -LiteralPath $full -PathType Container)) { throw "Isolation root is invalid." }
    $sep = [IO.Path]::DirectorySeparatorChar
    if ($full -eq $Repository -or $full.StartsWith($Repository + $sep, [StringComparison]::OrdinalIgnoreCase) -or $Repository.StartsWith($full + $sep, [StringComparison]::OrdinalIgnoreCase)) { throw "Isolation root must not overlap repository inputs." }
    for ($current = $full; ; $current = [IO.DirectoryInfo]::new($current).Parent.FullName) { if (Is-Reparse $current) { throw "Isolation root ancestor must not be a reparse point." }; if ([IO.Path]::GetPathRoot($current).TrimEnd('\','/') -eq $current.TrimEnd('\','/')) { break } }
    $markerPath = Join-Path $full ".aibar-isolation-marker"
    try { if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf) -or [IO.File]::ReadAllText($markerPath) -cne $Marker) { throw "Isolation marker does not match." } } catch { throw "Isolation marker cannot be verified." }
    return $full
}
function Assert-IsolationReady($Isolation, [string]$Marker, [string]$Repository) {
    Assert-IsolationState $Isolation.parent $Marker $Repository | Out-Null
    foreach ($root in @($Isolation.intermediate,$Isolation.build,$Isolation.restore,$Isolation.packages)) {
        try { $item = Get-Item -LiteralPath $root -Force; if (-not ($item -is [IO.DirectoryInfo]) -or (Is-Reparse $root)) { throw "Isolation child is invalid." } } catch { throw "Isolation child cannot be revalidated safely." }
    }
}
function Get-IsolationRoots([string]$Repository, [string]$Output) {
    $values = @($IsolationParent,$IsolationMarker,$IntermediateDirectory,$BuildOutputDirectory,$RestoreMetadataDirectory,$PackageCacheDirectory)
    $present = @($values | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($present.Count -eq 0) { return $null }; if ($present.Count -ne $values.Count) { throw "Isolation parameters must be supplied together." }
    $parent = Assert-IsolationState $IsolationParent $IsolationMarker $Repository
    foreach ($value in @($IntermediateDirectory,$BuildOutputDirectory,$RestoreMetadataDirectory,$PackageCacheDirectory,$Output)) { if (-not [IO.Path]::IsPathFullyQualified($value)) { throw "Isolation child must be absolute." } }
    $roots = @($IntermediateDirectory,$BuildOutputDirectory,$RestoreMetadataDirectory,$PackageCacheDirectory,$Output | ForEach-Object { [IO.Path]::GetFullPath($_) })
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($root in $roots) {
        if ($root -cne $root.Normalize([Text.NormalizationForm]::FormC) -or -not $seen.Add($root) -or (Test-Path -LiteralPath $root)) { throw "Isolation child is stale or invalid." }
        $sep = [IO.Path]::DirectorySeparatorChar; if (-not $root.StartsWith($parent + $sep, [StringComparison]::OrdinalIgnoreCase) -or $root -eq $parent) { throw "Isolation child must be beneath the owned parent." }
        Assert-FreshLeaf $root $Repository | Out-Null
    }
    foreach ($left in $roots) { foreach ($right in $roots) { if ($left -ne $right -and ($left.StartsWith($right + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $right.StartsWith($left + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) { throw "Isolation children must not overlap." } } }
    return [pscustomobject]@{ parent=$parent; intermediate=$roots[0]; build=$roots[1]; restore=$roots[2]; packages=$roots[3] }
}
function Write-IsolationProps($Isolation) {
    $xml = "<Project><PropertyGroup><BaseIntermediateOutputPath>$($Isolation.intermediate)\`$(MSBuildProjectName)\</BaseIntermediateOutputPath><MSBuildProjectExtensionsPath>$($Isolation.restore)\`$(MSBuildProjectName)\</MSBuildProjectExtensionsPath><RestoreOutputPath>$($Isolation.restore)\`$(MSBuildProjectName)\</RestoreOutputPath><BaseOutputPath>$($Isolation.build)\`$(MSBuildProjectName)\</BaseOutputPath><RestorePackagesPath>$($Isolation.packages)</RestorePackagesPath><PathMap>$($Isolation.parent)=/_/isolation</PathMap><DefaultItemExcludes>`$(DefaultItemExcludes);`$(MSBuildProjectDirectory)\obj\**;`$(MSBuildProjectDirectory)\bin\**</DefaultItemExcludes></PropertyGroup></Project>"
    $path = Join-Path $Isolation.restore "aibar-isolation.props"; [IO.File]::WriteAllText($path, $xml, [Text.UTF8Encoding]::new($false)); return $path
}
function Invoke-OwnedProcess([string]$FileName, [string[]]$Arguments, [string]$Failure) {
    $start = [Diagnostics.ProcessStartInfo]::new($FileName)
    $start.UseShellExecute = $false; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = $null; $timeout = [Threading.CancellationTokenSource]::new(); $linked = $null; $known = @()
    try {
        $timeout.CancelAfter([TimeSpan]::FromSeconds($ProcessTimeoutSeconds))
        $linked = [Threading.CancellationTokenSource]::CreateLinkedTokenSource($timeout.Token, $PSCmdlet.PipelineStopToken)
        if ($CancelAfterMilliseconds -gt 0) { $linked.CancelAfter($CancelAfterMilliseconds) }
        $process = [Diagnostics.Process]::Start($start)
        if ($null -eq $process) { throw $Failure }
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        try { $process.WaitForExitAsync($linked.Token).GetAwaiter().GetResult() }
        catch [OperationCanceledException] {
            if (-not $process.HasExited) { $process.Kill($true) }
            try { $process.WaitForExitAsync().WaitAsync([TimeSpan]::FromSeconds($ProcessTimeoutSeconds)).GetAwaiter().GetResult() }
            catch [TimeoutException] { throw $Failure }
            throw $Failure
        }
        if ($process.ExitCode -ne 0) { throw $Failure }
        if (-not [string]::IsNullOrWhiteSpace($KnownDescendantIdentityPath)) {
            try {
                $identity = Get-Content -LiteralPath $KnownDescendantIdentityPath -Raw | ConvertFrom-Json
                foreach ($name in @("child", "grandchild")) {
                    $record = $identity.$name
                    if ($null -eq $record) { throw "missing identity" }
                    $known += [Diagnostics.Process]::GetProcessById([int]$record.pid)
                    if ($known[-1].StartTime.ToUniversalTime().Ticks -ne [long]$record.startTicks) { throw "identity changed" }
                }
            }
            catch { throw $Failure }
            foreach ($descendant in $known) {
                if (-not $descendant.HasExited) {
                    try { $descendant.Kill($true); $descendant.WaitForExitAsync().WaitAsync([TimeSpan]::FromSeconds($ProcessTimeoutSeconds)).GetAwaiter().GetResult() }
                    catch { }
                    throw $Failure
                }
            }
        }
        try { [Threading.Tasks.Task]::WhenAll($stdout, $stderr).WaitAsync([TimeSpan]::FromSeconds($ProcessTimeoutSeconds)).GetAwaiter().GetResult() }
        catch { throw $Failure }
    }
    finally {
        foreach ($descendant in $known) { $descendant.Dispose() }
        if ($null -ne $linked) { $linked.Dispose() }; $timeout.Dispose()
        if ($null -ne $process) { $process.Dispose() }
    }
}
function Invoke-PrivateBeta {
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
    $canonical = (& git -C $projectRoot rev-parse --show-toplevel).Trim()
    if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($canonical) -cne $projectRoot) { throw "BETA_REPOSITORY_MISMATCH" }
    $status = ((& git -C $projectRoot status --porcelain) -join "`n").Trim()
    if ($LASTEXITCODE -ne 0) { throw "BETA_REPOSITORY_INVALID" }
    if (-not [string]::IsNullOrWhiteSpace($status)) { throw "BETA_SOURCE_UNCOMMITTED" }
    $source = (& git -C $projectRoot rev-parse HEAD).Trim(); $parent = (& git -C $projectRoot rev-parse "$source^").Trim()
    if ($LASTEXITCODE -ne 0) { throw "BETA_SOURCE_MISSING" }
    $baseline = if ([string]::IsNullOrWhiteSpace($BetaBaselineCommit)) { "c4e35c01d5ed9cff30b136802a39196a3b2ed91d" } else { if ($env:AIBAR_PRIVATE_BETA_TEST_MODE -ne "1") { throw "BETA_TEST_OVERRIDE_DENIED" }; $BetaBaselineCommit }
    $requiredParent = if ([string]::IsNullOrWhiteSpace($BetaParentCommit)) { "816c28b6747a6e9aa62c5c23c216cd4feb7a55fd" } else { if ($env:AIBAR_PRIVATE_BETA_TEST_MODE -ne "1") { throw "BETA_TEST_OVERRIDE_DENIED" }; $BetaParentCommit }
    if ($parent -cne $requiredParent) { throw "BETA_PARENT_MISMATCH" }
    & git -C $projectRoot merge-base --is-ancestor $baseline $source
    if ($LASTEXITCODE -ne 0) { throw "BETA_BASELINE_MISMATCH" }
    [xml]$projectXml = Get-Content -LiteralPath (Join-Path $projectRoot "src\AIBar.Desktop\AIBar.Desktop.csproj") -Raw
    $version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($version)) { throw "BETA_VERSION_MISSING" }
    $outputRoot = Assert-FreshLeaf $OutputDirectory $projectRoot; $created = $false
    try {
        New-Item -ItemType Directory -Path $outputRoot -ErrorAction Stop | Out-Null; $created = $true
        $publish = Join-Path $outputRoot "publish"; New-Item -ItemType Directory -Path $publish -ErrorAction Stop | Out-Null
        Invoke-OwnedProcess $PublishCommand @("publish", (Join-Path $projectRoot "src\AIBar.Desktop\AIBar.Desktop.csproj"), "--disable-build-servers", "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true", "--output", $publish, "/p:ContinuousIntegrationBuild=true", "/p:Deterministic=true", "/p:DebugType=None") "BETA_PUBLISH_FAILED"
        $notice = "AIBar $version is an unsigned private beta for Windows x64. It is manually distributed and has no updater or installer.`nBaseline ancestor: $baseline`nSource commit: $source`n"
        [IO.File]::WriteAllText((Join-Path $publish "PRIVATE-BETA.txt"), $notice, [Text.UTF8Encoding]::new($false))
        $files = @{}; Get-ChildItem -LiteralPath $publish -Recurse -File | ForEach-Object { $relative = $_.FullName.Substring($publish.Length).TrimStart('\','/') -replace '\\','/'; if ($relative.StartsWith('/') -or $relative.Split('/') -contains '..' -or $files.ContainsKey($relative)) { throw "BETA_INVENTORY_INVALID" }; $files[$relative] = [ordered]@{ path=$relative; length=$_.Length; sha256=(Sha $_.FullName) } }
        $paths = [string[]]$files.Keys; [Array]::Sort($paths, [StringComparer]::Ordinal); $inventory = @($paths | ForEach-Object { [pscustomobject]$files[$_] })
        Add-Type -AssemblyName System.IO.Compression; Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zipPath = Join-Path $outputRoot "AIBar-win-x64-private-beta.zip"; $stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew); $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try { foreach ($relative in $paths) { $entry = $zip.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal); $entry.LastWriteTime = [DateTimeOffset]::FromUnixTimeSeconds([long]$SourceDateEpoch); $entry.ExternalAttributes = 0; $input = [IO.File]::OpenRead((Join-Path $publish ($relative -replace '/','\'))); try { $target = $entry.Open(); try { $input.CopyTo($target) } finally { $target.Dispose() } } finally { $input.Dispose() } } } finally { $zip.Dispose(); $stream.Dispose() }
        $smoke = Join-Path $outputRoot "smoke"; $process = $null
        try { Expand-Archive -LiteralPath $zipPath -DestinationPath $smoke; $executable = Join-Path $smoke "AIBar.Desktop.exe"; if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "BETA_SMOKE_EXECUTABLE_MISSING" }; $process = Start-Process -FilePath $executable -WorkingDirectory $smoke -PassThru; if ($process.WaitForExit($SmokeStartupMilliseconds)) { throw "BETA_SMOKE_EARLY_EXIT" }; $process.Kill($true); if (-not $process.WaitForExit($ProcessTimeoutSeconds * 1000)) { throw "BETA_SMOKE_TIMEOUT" } } finally { if ($null -ne $process) { if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }; $process.Dispose() }; if (Test-Path -LiteralPath $smoke) { Remove-Item -LiteralPath $smoke -Recurse -Force } }
        $zipSha = Sha $zipPath
        [ordered]@{ schemaVersion="aibar-private-beta-1"; target="win-x64"; selfContained=$true; unsigned=$true; baselineCommit=$baseline; parentCommit=$parent; sourceCommit=$source; version=$version; inventory=$inventory; zipSha256=$zipSha } | ConvertTo-Json -Compress -Depth 8 | Set-Content -LiteralPath (Join-Path $outputRoot "private-beta-manifest.json") -NoNewline
        $instructions = "AIBar private beta $version is unsigned. Extract the ZIP, verify `$((Get-FileHash .\AIBar-win-x64-private-beta.zip -Algorithm SHA256).Hash) equals $zipSha, then launch AIBar.Desktop.exe from the extracted directory. Replace an older beta by exiting it and replacing its extracted directory manually; no updater or uninstall is provided. Baseline ancestor: $baseline. Parent commit: $parent. Source commit: $source."
        [IO.File]::WriteAllText((Join-Path $outputRoot "private-beta-instructions.txt"), $instructions + "`n", [Text.UTF8Encoding]::new($false))
    } catch { if ($created -and (Test-Path -LiteralPath $outputRoot)) { Remove-Item -LiteralPath $outputRoot -Recurse -Force }; throw }
}
if ($PrivateBeta) { Invoke-PrivateBeta; exit 0 }
try {
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { throw "OutputDirectory is required." }
    try { $epoch = [long]::Parse($SourceDateEpoch, [Globalization.CultureInfo]::InvariantCulture) } catch { throw "SourceDateEpoch must be an integer." }
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..")); $isolation = Get-IsolationRoots $projectRoot $OutputDirectory; $outputRoot = Assert-FreshLeaf $OutputDirectory $projectRoot
    $outputRoot = Assert-FreshLeaf $outputRoot $projectRoot
    if ($null -ne $isolation) { foreach ($root in @($isolation.intermediate,$isolation.build,$isolation.restore,$isolation.packages)) { New-Item -ItemType Directory -Path $root -ErrorAction Stop | Out-Null }; Assert-IsolationReady $isolation $IsolationMarker $projectRoot }
    New-Item -ItemType Directory -Path $outputRoot -ErrorAction Stop | Out-Null; $created = $true
    $publish = Join-Path $outputRoot "publish"; New-Item -ItemType Directory -Path $publish -ErrorAction Stop | Out-Null
    $project = Join-Path $projectRoot "src\AIBar.Desktop\AIBar.Desktop.csproj"
    if ($null -ne $isolation) { $properties = @("/p:DirectoryBuildPropsPath=$(Write-IsolationProps $isolation)"); $restoreArguments = @("restore", $project, "--runtime", "win-x64", "--disable-parallel", "--nologo") + $properties; Assert-IsolationReady $isolation $IsolationMarker $projectRoot; Invoke-OwnedProcess $PublishCommand $restoreArguments "Self-contained win-x64 restore failed."; $publishArguments = @("publish", $project, "--no-restore", "--disable-build-servers", "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true", "--output", $publish, "/p:ContinuousIntegrationBuild=true", "/p:Deterministic=true", "/p:DebugType=None") + $properties; Assert-IsolationReady $isolation $IsolationMarker $projectRoot; Invoke-OwnedProcess $PublishCommand $publishArguments "Self-contained win-x64 publish failed." } else { Invoke-OwnedProcess $PublishCommand @("publish", $project, "--disable-build-servers", "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true", "--output", $publish, "/p:ContinuousIntegrationBuild=true", "/p:Deterministic=true", "/p:DebugType=None") "Self-contained win-x64 publish failed." }
    $files = @{}; Get-ChildItem -LiteralPath $publish -Recurse -File | ForEach-Object { $relative = $_.FullName.Substring($publish.Length).TrimStart('\','/') -replace '\\','/'; if ($relative.StartsWith('/') -or $relative.Split('/') -contains '..' -or $files.ContainsKey($relative)) { throw "Unsafe or duplicate publish path." }; $files[$relative] = [ordered]@{ path = $relative; length = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash } }
    $paths = [string[]]$files.Keys; [Array]::Sort($paths, [StringComparer]::Ordinal); $inventory = @($paths | ForEach-Object { [pscustomobject]$files[$_] })
    function Write-Json([string]$Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Compress -Depth 4), [Text.UTF8Encoding]::new($false)) }
    $inventoryPath = Join-Path $outputRoot "recovery-inventory.json"; Write-Json $inventoryPath $inventory
    Add-Type -AssemblyName System.IO.Compression; Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath = Join-Path $outputRoot "AIBar-win-x64-recovery.zip"; $stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew); $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try { foreach ($relative in $paths) { $entry = $zip.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal); $entry.LastWriteTime = [DateTimeOffset]::FromUnixTimeSeconds($epoch); $entry.ExternalAttributes = 0; $input = [IO.File]::OpenRead((Join-Path $publish ($relative -replace '/','\\'))); try { $target = $entry.Open(); try { $input.CopyTo($target) } finally { $target.Dispose() } } finally { $input.Dispose() } } } finally { $zip.Dispose(); $stream.Dispose() }
    $inventoryHash = (Get-FileHash $inventoryPath -Algorithm SHA256).Hash; $manifest = [ordered]@{ target = "win-x64"; configuration = "Release"; publishInventorySha256 = $inventoryHash; recoveryZipSha256 = (Get-FileHash $zipPath -Algorithm SHA256).Hash; recoveryInventorySha256 = $inventoryHash }; Write-Json (Join-Path $outputRoot "artifact-manifest.json") $manifest
} catch { if ($created) { [Console]::Error.WriteLine("PUBLISH_INCOMPLETE_OUTPUT=$outputRoot") }; throw }
