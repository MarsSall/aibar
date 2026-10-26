function Get-ReaderNow {
    $value = Get-Variable -Name AIBarQuotaFixtureNow -Scope Script -ErrorAction SilentlyContinue
    if ($null -ne $value -and $value.Value -is [DateTimeOffset]) { return $value.Value.ToUniversalTime() }
    return [DateTimeOffset]::UtcNow
}

function Get-ReaderPath {
    param([string]$Provider)
    $fixture = Get-Variable -Name AIBarQuotaFixturePath -Scope Script -ErrorAction SilentlyContinue
    if ($null -ne $fixture -and -not [string]::IsNullOrWhiteSpace($fixture.Value)) { return $fixture.Value }
    if ($Provider -ceq 'Claude') {
        return (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'AIBar\yasb-claude-quota.json')
    }
    return (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'AIBar\yasb-quota.json')
}

function Get-BlockingWindow($Five, $Weekly) {
    if ($null -eq $Five) { return $Weekly }
    if ($null -eq $Weekly) { return $Five }
    if ($Weekly.percentageUsed -gt $Five.percentageUsed) { return $Weekly }
    if ($Five.percentageUsed -gt $Weekly.percentageUsed) { return $Five }
    # Equal usage: a known reset sorts before an unknown one. Exact ties stay stable on 5h.
    if ($null -ne $Weekly.resetAt -and ($null -eq $Five.resetAt -or $Weekly.resetAt -lt $Five.resetAt)) { return $Weekly }
    return $Five
}

function Get-SafeOutput([string]$State = 'unavailable', $Warning = 'unavailable', $Five = $null, $Weekly = $null, $Source = $null, [Parameter(Mandatory = $true)][DateTimeOffset]$Now) {
    function Slot([string]$Name, $Window) { if ($null -eq $Window) { return "${Name}: --" }; return ('{0}: {1}%' -f $Name, ([double]$Window.percentageUsed).ToString('0.#', [Globalization.CultureInfo]::InvariantCulture)) }
    function Reset($Window) {
        if ($null -eq $Window -or $null -eq $Window.resetAt) { return 'reset: --' }
        $reset = [DateTimeOffset]$Window.resetAt
        if ($reset -le $Now) { return 'reset: due' }
        # Compact countdown like the panel; no seconds and no markup characters in the tooltip.
        $left = $reset - $Now
        $text = if ($left.TotalDays -ge 1) { '{0}d {1}h' -f [math]::Floor($left.TotalDays), $left.Hours }
            elseif ($left.TotalHours -ge 1) { '{0}h {1}m' -f [math]::Floor($left.TotalHours), $left.Minutes }
            elseif ($left.TotalMinutes -ge 1) { '{0}m' -f [math]::Floor($left.TotalMinutes) }
            else { 'under 1m' }
        return "resets in $text"
    }
    $dot = [char]0x00B7
    function WindowLine([string]$Name, $Window) {
        $percentage = ([double]$Window.percentageUsed).ToString('0.#', [Globalization.CultureInfo]::InvariantCulture)
        return ('{0}  {1}% {2} {3}' -f $Name, $percentage, $dot, (Reset $Window))
    }
    $stateText = switch -CaseSensitive ($State) {
        'current' { 'current' }; 'refreshing' { 'refreshing' }; 'stale' { 'stale' }
        'unavailable' { 'unavailable' }; 'disabled' { 'disabled' }
    }
    $warningText = switch -CaseSensitive ($Warning) {
        'refresh-failed' { 'refresh failed' }; 'authentication-failed' { 'authentication failed' }
        'unavailable' { 'service unavailable' }; 'disabled' { 'disabled' }
    }
    $distinctWarning = $null -ne $Warning -and $Warning -cne $State
    $label = @()
    if ($null -ne $Five) { $label += Slot '5h' $Five }
    if ($null -ne $Weekly) { $label += Slot '7d' $Weekly }
    $label += $stateText
    if ($distinctWarning) { $label += $warningText }
    $age = '--'
    if ($null -ne $Source) {
        $span = $Now - ([DateTimeOffset]$Source)
        if ($span.TotalDays -ge 1) { $age = ('{0}d' -f [math]::Floor($span.TotalDays)) }
        elseif ($span.TotalHours -ge 1) { $age = ('{0}h' -f [math]::Floor($span.TotalHours)) }
        else { $age = ('{0}m' -f [math]::Max(0, [math]::Floor($span.TotalMinutes))) }
    }
    $tooltip = @('Codex')
    if ($null -ne $Five) { $tooltip += WindowLine '5h' $Five }
    if ($null -ne $Weekly) { $tooltip += WindowLine 'Weekly' $Weekly }
    $statusLine = @($stateText)
    if ($distinctWarning) { $statusLine += $warningText }
    $statusLine += "age: $age"
    $tooltip += ($statusLine -join " $dot ")
    $hero = Get-BlockingWindow $Five $Weekly
    $glyphCodes = @(0xF0766, 0xF0A9E, 0xF0A9F, 0xF0AA0, 0xF0AA1, 0xF0AA2, 0xF0AA3, 0xF0AA4, 0xF0AA5)
    $glyphIndex = 0
    $color = '#8995A4'
    $compact = @()
    if ($null -ne $hero) {
        $used = [double]$hero.percentageUsed
        $glyphIndex = [int][math]::Min(8, [math]::Floor($used / 12.5))
        # Same 8-step fill and breakpoints; green, yellow, orange and red as usage grows.
        $color = if ($used -lt 50) { '#3DDC84' } elseif ($used -lt 75) { '#FFC531' } elseif ($used -lt 90) { '#FF8A3D' } else { '#FF4D5E' }
        # Name the window after the percentage so the bar shows which limit it is.
        $windowLabel = if ([object]::ReferenceEquals($hero, $Five)) { '5hr' } else { '7d' }
        $compact += $used.ToString('R', [Globalization.CultureInfo]::InvariantCulture) + '% ' + $windowLabel
    }
    if ($null -eq $hero -or $State -ceq 'stale') {
        $compact += $stateText
        if ($distinctWarning) { $compact += $warningText }
    }
    $compactText = $compact -join ' | '
    if ($compactText.Length -gt 64 -or $compactText -cmatch '[<>&{}]') { return Get-SafeOutput -Now $Now }
    [pscustomobject][ordered]@{
        label = ($label -join ' | ')
        compactText = $compactText
        progressGlyph = [char]::ConvertFromUtf32($glyphCodes[$glyphIndex])
        progressColor = $color
        tooltip = ($tooltip -join "`n")
        className = "aibar-quota state-$State$(if ($null -ne $Warning) { " warning-$Warning" })"
    }
}

function Get-Utc($Value) {
    if ($Value -isnot [string] -or $Value -cnotmatch '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|\+00:00)$') { return $null }
    $text = if ($Value.EndsWith('Z', [StringComparison]::Ordinal)) { $Value.Substring(0, $Value.Length - 1) + '+00:00' } else { $Value }
    $parsed = [DateTimeOffset]::MinValue
    $formats = [string[]]@("yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz")
    if (-not [DateTimeOffset]::TryParseExact($text, $formats, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed)) { return $null }
    if ($parsed.Offset -ne [TimeSpan]::Zero) { return $null }
    return $parsed.ToUniversalTime()
}

function Test-Names($Object, [string[]]$Expected) {
    if ($Object -isnot [pscustomobject]) { return $false }
    $names = @($Object.PSObject.Properties.Name)
    if ($names.Count -ne $Expected.Count) { return $false }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if (-not [string]::Equals([string]$names[$index], $Expected[$index], [StringComparison]::Ordinal)) { return $false }
    }
    return $true
}

function Get-Window($Window, [AllowNull()]$Source) {
    if ($null -eq $Window) { return $null }
    if ($null -ne $Source -and $Source -isnot [DateTimeOffset]) { return $false }
    if (-not (Test-Names $Window @('percentageUsed', 'resetAt'))) { return $false }
    $value = $Window.percentageUsed
    if ($null -eq $value -or $value -is [bool]) { return $false }
    $typeCode = [Type]::GetTypeCode($value.GetType())
    if ($typeCode -notin @([TypeCode]::SByte, [TypeCode]::Byte, [TypeCode]::Int16, [TypeCode]::UInt16, [TypeCode]::Int32, [TypeCode]::UInt32, [TypeCode]::Int64, [TypeCode]::UInt64, [TypeCode]::Single, [TypeCode]::Double, [TypeCode]::Decimal)) { return $false }
    try { $percentage = [double]$value } catch { return $false }
    if ([double]::IsNaN($percentage) -or [double]::IsInfinity($percentage) -or $percentage -lt 0 -or $percentage -gt 100) { return $false }
    $reset = $null
    if ($null -ne $Window.resetAt) {
        $reset = Get-Utc $Window.resetAt
        if ($null -eq $reset) { return $false }
    }
    return [pscustomobject][ordered]@{ percentageUsed = $percentage; resetAt = $reset }
}

function Invoke-AiBarQuotaReaderCore {
    param([string]$Provider, [DateTimeOffset]$now)
    try {
        $path = Get-ReaderPath -Provider $Provider
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            $bytes = New-Object byte[] 16385
            $count = $stream.Read($bytes, 0, $bytes.Length)
            if ($count -eq 0 -or $count -gt 16384) { throw 'invalid' }
            $document = [Text.Encoding]::UTF8.GetString($bytes, 0, $count) | ConvertFrom-Json -ErrorAction Stop
        } finally { $stream.Dispose() }
        if (-not (Test-Names $document @('schemaVersion', 'generatedAt', 'state', 'warning', 'sourceRetrievedAt', 'fiveHour', 'weekly'))) { throw 'invalid' }
        if ($document.schemaVersion -isnot [int] -or $document.schemaVersion -ne 1) { throw 'invalid' }
        if ($document.state -cnotin @('current', 'refreshing', 'stale', 'unavailable', 'disabled')) { throw 'invalid' }
        if ($null -ne $document.warning -and $document.warning -cnotin @('refresh-failed', 'authentication-failed', 'unavailable', 'disabled')) { throw 'invalid' }
        $generated = Get-Utc $document.generatedAt
        if ($null -eq $generated -or $generated -gt $now) { throw 'invalid' }
        $source = if ($null -eq $document.sourceRetrievedAt) { $null } else { Get-Utc $document.sourceRetrievedAt }
        if ($null -ne $document.sourceRetrievedAt -and ($null -eq $source -or $source -gt $generated)) { throw 'invalid' }
        $five = Get-Window $document.fiveHour $source; $weekly = Get-Window $document.weekly $source
        if ($five -eq $false -or $weekly -eq $false) { throw 'invalid' }
        $values = $null -ne $five -or $null -ne $weekly
        $valid = switch -CaseSensitive ($document.state) {
            'current' { $null -eq $document.warning -and $values -and $null -ne $source }
            'refreshing' { $null -eq $document.warning -and ($values -eq ($null -ne $source)) }
            'stale' { $document.warning -cin @('refresh-failed', 'authentication-failed', 'unavailable') -and $values -and $null -ne $source }
            'unavailable' { $document.warning -cin @($null, 'authentication-failed', 'unavailable') -and -not $values -and $null -eq $source }
            'disabled' { $document.warning -ceq 'disabled' -and -not $values -and $null -eq $source }
        }
        if (-not $valid) { throw 'invalid' }
        return Get-SafeOutput -State $document.state -Warning $document.warning -Five $five -Weekly $weekly -Source $source -Now $now
    } catch { return Get-SafeOutput -Now $now }
}

function Get-YasbColorMode([string]$QuotaPath) {
    # Optional, bounded, read-only preference. Failure must never affect quota output.
    try {
        $path = Join-Path ([IO.Path]::GetDirectoryName($QuotaPath)) 'appearance.json'
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        try {
            $bytes = New-Object byte[] 4097
            $count = $stream.Read($bytes, 0, $bytes.Length)
            if ($count -eq 0 -or $count -gt 4096) { return 'usage' }
            $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes, 0, $count) | ConvertFrom-Json -ErrorAction Stop
        } finally { $stream.Dispose() }
        if ($json -isnot [pscustomobject]) { return 'usage' }
        $names = @($json.PSObject.Properties.Name)
        if ($names -cnotcontains 'schema' -or $names -cnotcontains 'yasbColors') { return 'usage' }
        if ($json.schema -isnot [int] -or $json.schema -ne 1 -or $json.yasbColors -isnot [string]) { return 'usage' }
        if ($json.yasbColors -ceq 'brand') { return 'brand' }
    } catch { return 'usage' }
    return 'usage'
}

function Invoke-AiBarQuotaReader {
    param([string]$Provider)
    $now = Get-ReaderNow
    if ($Provider -cne '' -and $Provider -cne 'Claude') { return Get-SafeOutput -Now $now }
    $result = Invoke-AiBarQuotaReaderCore -Provider $Provider -now $now
    if ($result.progressColor -cne '#8995A4' -and (Get-YasbColorMode (Get-ReaderPath -Provider $Provider)) -ceq 'brand') {
        $result.progressColor = if ($Provider -ceq 'Claude') { '#E0876A' } else { '#2FBF98' }
    }
    if ($Provider -ceq 'Claude') {
        # The widget shows the Claude mark; the tooltip names the provider on its own line.
        $result.label = 'Claude ' + $result.label
        $result.tooltip = $result.tooltip -creplace '^Codex\n', "Claude`n"
    }
    return $result
}

if ($MyInvocation.InvocationName -ne '.') {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    # No script parameter block: accept only this exact, case-sensitive opt-in pair.
    if ($args.Count -eq 0) { Invoke-AiBarQuotaReader | ConvertTo-Json -Compress }
    elseif ($args.Count -eq 2 -and $args[0] -ceq '-Provider' -and $args[1] -ceq 'Claude') {
        Invoke-AiBarQuotaReader -Provider Claude | ConvertTo-Json -Compress
    }
    else { Get-SafeOutput -Now (Get-ReaderNow) | ConvertTo-Json -Compress; exit 1 }
}
