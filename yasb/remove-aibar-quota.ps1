param()

function Get-CleanupPath {
    $fixture = Get-Variable -Name AIBarQuotaCleanupFixturePath -Scope Script -ErrorAction SilentlyContinue
    if ($null -ne $fixture -and -not [string]::IsNullOrWhiteSpace($fixture.Value)) { return [IO.Path]::GetFullPath($fixture.Value) }
    return (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'AIBar\yasb-quota.json')
}

function Test-CleanupReparse([string]$Path) {
    $override = Get-Variable -Name AIBarQuotaCleanupTestReparse -Scope Script -ErrorAction SilentlyContinue
    if ($null -ne $override -and $override.Value -is [scriptblock]) { return (& $override.Value $Path) }
    try { return ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } catch { return $true }
}

function Test-DisabledSnapshot([string]$Path, [string]$ExpectedSnapshot) {
    try {
        $bytes = [IO.File]::ReadAllBytes($Path)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { return $false }
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        if (-not [string]::Equals($text, $ExpectedSnapshot, [StringComparison]::Ordinal)) { return $false }
        $document = $text | ConvertFrom-Json -ErrorAction Stop
        $names = @($document.PSObject.Properties.Name); $expected = @('schemaVersion', 'generatedAt', 'state', 'warning', 'sourceRetrievedAt', 'fiveHour', 'weekly')
        if ($names.Count -ne $expected.Count) { return $false }
        for ($index = 0; $index -lt $expected.Count; $index++) { if ($names[$index] -cne $expected[$index]) { return $false } }
        $schemaVersionMatch = [regex]::Match($text, '\A\{"schemaVersion":(?<value>[^,]+),', [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        $generatedAtMatch = [regex]::Match($text, '"generatedAt":"(?<value>[^"]*)"', [Text.RegularExpressions.RegexOptions]::CultureInvariant); $parsed = [DateTimeOffset]::MinValue
        if (-not $generatedAtMatch.Success -or -not [DateTimeOffset]::TryParseExact($generatedAtMatch.Groups['value'].Value, 'O', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed) -or $parsed.Offset -ne [TimeSpan]::Zero -or $parsed.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture) -cne $generatedAtMatch.Groups['value'].Value) { return $false }
        return $schemaVersionMatch.Success -and $schemaVersionMatch.Groups['value'].Value -ceq '1' -and $document.state -is [string] -and $document.state -ceq 'disabled' -and $document.warning -is [string] -and $document.warning -ceq 'disabled' -and $null -eq $document.sourceRetrievedAt -and $null -eq $document.fiveHour -and $null -eq $document.weekly
    } catch { return $false }
}

function Remove-ExistingSnapshot([string]$Path) {
    $override = Get-Variable -Name AIBarQuotaCleanupTestDelete -Scope Script -ErrorAction SilentlyContinue
    if ($null -ne $override -and $override.Value -is [scriptblock]) { & $override.Value $Path; return }
    [IO.File]::Delete($Path)
}

function Publish-DisabledSnapshot([string]$Path) {
    $directory = [IO.Path]::GetDirectoryName($Path)
    if ([string]::IsNullOrWhiteSpace($directory) -or -not (Test-Path -LiteralPath $directory -PathType Container) -or (Test-CleanupReparse $directory) -or (Test-CleanupReparse $Path)) { return $false }
    $identifier = [Guid]::NewGuid().ToString('N')
    $temporary = Join-Path $directory ('.yasb-quota-cleanup-' + $identifier + '.tmp')
    $backup = Join-Path $directory ('.yasb-quota-cleanup-' + $identifier + '.bak')
    try {
        $snapshot = [ordered]@{ schemaVersion = 1; generatedAt = [DateTimeOffset]::UtcNow.ToString('O'); state = 'disabled'; warning = 'disabled'; sourceRetrievedAt = $null; fiveHour = $null; weekly = $null } | ConvertTo-Json -Compress
        [IO.File]::WriteAllText($temporary, $snapshot, [Text.UTF8Encoding]::new($false))
            if (Test-CleanupReparse $temporary) { return $false }
            $override = Get-Variable -Name AIBarQuotaCleanupTestReplace -Scope Script -ErrorAction SilentlyContinue
            if ($null -ne $override -and $override.Value -is [scriptblock]) { & $override.Value $temporary $Path }
            elseif (Test-Path -LiteralPath $Path -PathType Leaf) {
                try { [IO.File]::Replace($temporary, $Path, $backup, $false) }
                catch [IO.FileNotFoundException] {
                    if (Test-Path -LiteralPath $Path -PathType Leaf) { throw }
                    [IO.File]::Move($temporary, $Path)
                }
            } else { [IO.File]::Move($temporary, $Path) }
            if (Test-CleanupReparse $Path) { return $false }
            return Test-DisabledSnapshot $Path $snapshot
    } catch { return $false }
        finally {
            if (Test-Path -LiteralPath $temporary -PathType Leaf) { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
            if (Test-Path -LiteralPath $backup -PathType Leaf) { Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue }
        }
}

function Invoke-AiBarQuotaCleanup {
    $path = Get-CleanupPath
    $directory = [IO.Path]::GetDirectoryName($path)
    if ([string]::IsNullOrWhiteSpace($directory) -or ((Test-Path -LiteralPath $directory -PathType Container) -and (Test-CleanupReparse $directory))) { return $false }
    if (-not (Test-Path -LiteralPath $path)) { return $true }
    if (Test-CleanupReparse $path) { return $false }
    try { Remove-ExistingSnapshot $path } catch { return Publish-DisabledSnapshot $path }
    return -not (Test-Path -LiteralPath $path)
}

if ($MyInvocation.InvocationName -ne '.') {
    if ($args.Count -ne 0) { exit 64 }
    if (Invoke-AiBarQuotaCleanup) { exit 0 }
    exit 1
}
