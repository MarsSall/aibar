function Get-ReaderNow {
    $value = Get-Variable -Name AIBarQuotaFixtureNow -Scope Script -ErrorAction SilentlyContinue
    if ($null -ne $value -and $value.Value -is [DateTimeOffset]) { return $value.Value.ToUniversalTime() }
    return [DateTimeOffset]::UtcNow
}

function Get-ReaderPath {
    $fixture = Get-Variable -Name AIBarQuotaFixturePath -Scope Script -ErrorAction SilentlyContinue
    if ($null -ne $fixture -and -not [string]::IsNullOrWhiteSpace($fixture.Value)) { return $fixture.Value }
    return (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'AIBar\yasb-quota.json')
}

function Get-SafeOutput([string]$State = 'unavailable', $Warning = 'unavailable', $Five = $null, $Weekly = $null, $Source = $null, [Parameter(Mandatory = $true)][DateTimeOffset]$Now) {
    function Slot([string]$Name, $Window) { if ($null -eq $Window) { return "${Name}: --" }; return ('{0}: {1}%' -f $Name, ([double]$Window.percentageUsed).ToString('0.#', [Globalization.CultureInfo]::InvariantCulture)) }
    function Reset([string]$Name, $Window) {
        if ($null -eq $Window -or $null -eq $Window.resetAt) { return "$Name reset: --" }
        $reset = [DateTimeOffset]$Window.resetAt
        if ($reset -le $Now) { return "$Name reset: due" }
        return "$Name reset: $($reset.UtcDateTime.ToString('u').TrimEnd('Z'))Z"
    }
    $partial = ($null -eq $Five) -xor ($null -eq $Weekly)
    $status = $State
    if ($partial) { $status += ' partial' }
    if ($null -ne $Warning) { $status += " | warning: $Warning" }
    $age = '--'
    if ($null -ne $Source) {
        $span = $Now - ([DateTimeOffset]$Source)
        if ($span.TotalDays -ge 1) { $age = ('{0}d' -f [math]::Floor($span.TotalDays)) }
        elseif ($span.TotalHours -ge 1) { $age = ('{0}h' -f [math]::Floor($span.TotalHours)) }
        else { $age = ('{0}m' -f [math]::Max(0, [math]::Floor($span.TotalMinutes))) }
    }
    [pscustomobject][ordered]@{
        label = "$(Slot '5h' $Five) | $(Slot '7d' $Weekly) | $status"
        tooltip = "state: $State$(if ($partial) { ' partial' })$(if ($null -ne $Warning) { " | warning: $Warning" }) | $(Reset '5h' $Five) | $(Reset '7d' $Weekly) | age: $age"
        className = "aibar-quota state-$State$(if ($null -ne $Warning) { " warning-$Warning" })$(if ($partial) { ' partial' })"
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

function Invoke-AiBarQuotaReader {
    $now = Get-ReaderNow
    try {
        $path = Get-ReaderPath
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

if ($MyInvocation.InvocationName -ne '.') {
    if ($args.Count -ne 0) { Get-SafeOutput -Now (Get-ReaderNow) | ConvertTo-Json -Compress; exit 1 }
    Invoke-AiBarQuotaReader | ConvertTo-Json -Compress
}
