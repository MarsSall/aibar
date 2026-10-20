BeforeAll {
    $root = Split-Path -Parent $PSScriptRoot
    $reader = Join-Path $root 'read-aibar-quota.ps1'
    $now = [DateTimeOffset]'2026-07-12T12:00:00Z'
    $readerModule = New-Module -ScriptBlock {
        param([string]$readerPath)
        . $readerPath
    } -ArgumentList $reader

    function Read-Fixture([string]$name, [DateTimeOffset]$at = $now) {
        $fixturePath = Join-Path $root "fixtures/$name"
        $before = if (Test-Path -LiteralPath $fixturePath) { (Get-FileHash -LiteralPath $fixturePath).Hash } else { $null }
        $result = & $readerModule {
param([string]$path, [DateTimeOffset]$fixtureNow)
$script:AIBarQuotaFixturePath = $path
$script:AIBarQuotaFixtureNow = $fixtureNow
Invoke-AiBarQuotaReader
        } $fixturePath $at
        $after = if (Test-Path -LiteralPath $fixturePath) { (Get-FileHash -LiteralPath $fixturePath).Hash } else { $null }
        $after | Should -Be $before
        return $result
    }

    function Read-Document($document, [DateTimeOffset]$at = $now) {
        $documentPath = [IO.Path]::GetTempFileName()
        try {
            $json = ConvertTo-Json -InputObject $document -Depth 5 -Compress
            [IO.File]::WriteAllText($documentPath, $json, [Text.UTF8Encoding]::new($false))
            $result = & $readerModule {
param([string]$path, [DateTimeOffset]$fixtureNow)
$script:AIBarQuotaFixturePath = $path
$script:AIBarQuotaFixtureNow = $fixtureNow
Invoke-AiBarQuotaReader
            } $documentPath $at
            [IO.File]::ReadAllText($documentPath) | Should -BeExactly $json
            return $result
        } finally {
            Remove-Item -LiteralPath $documentPath -Force
        }
    }

    function Assert-SafeFields($result) {
        $names = @($result.PSObject.Properties.Name)
        ($names -join ',') | Should -Be 'label,compactText,progressGlyph,progressColor,tooltip,className'
        $result.compactText.Length | Should -BeLessOrEqual 64
        $result.compactText | Should -Not -Match '[<>&{}]'
        ($result.progressColor -cin @('#3DDC84', '#FFC531', '#FF8A3D', '#FF4D5E', '#8995A4')) | Should -Be $true
    }
    function Assert-Compact($result, [string]$text) {
        $result.compactText | Should -BeExactly $text
        if ($text -notmatch '%') {
            $result.progressGlyph | Should -BeExactly ([char]::ConvertFromUtf32(0xF0766))
            $result.progressColor | Should -BeExactly '#8995A4'
        }
    }
}

AfterAll {
    Remove-Module $readerModule -Force
}

Describe 'AIBar YASB reader' {
    It 'has an empty production parameter surface, one trusted clock read, and bounded read-only behavior' {
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($reader, [ref]$tokens, [ref]$errors)
        $source = Get-Content -Raw $reader
        $errors.Count | Should -Be 0
        ($null -eq $ast.ParamBlock) | Should -Be $true
        ([regex]::Matches($source, '\[DateTimeOffset\]::UtcNow')).Count | Should -Be 1
        $source | Should -Match 'catch\s*\{\s*return Get-SafeOutput -Now \$now\s*\}'
        $source | Should -Match 'return Get-SafeOutput .* -Now \$now'
        $source | Should -Match 'AIBar\\yasb-quota\.json'
        $source | Should -Match '\[IO\.FileAccess\]::Read'
        $source | Should -Match '16385'
        $source | Should -Match '-gt 16384'
        $source | Should -Match 'StringComparison\]::Ordinal'
        $source | Should -Match 'switch -CaseSensitive'
        $source | Should -Match '-cnotin'
        $source | Should -Not -Match 'Start-Process|Invoke-WebRequest|Remove-Item|Set-Content|Add-Content|WriteAll|CreateDirectory'
        $ci = Get-Content -Raw (Join-Path (Split-Path -Parent $root) '.github/workflows/ci.yml')
        $ci | Should -Match '\$result.TotalCount -ne 24'
        $ci | Should -Match 'Expected 24 passing YASB tests'
        ([regex]::Matches((Get-Content -Raw (Join-Path $root 'tests/read-aibar-quota.Tests.ps1')), '(?m)^\s*It\s+')).Count | Should -Be 24
        $stderrPath = [IO.Path]::GetTempFileName()
        try {
            $start = [Diagnostics.ProcessStartInfo]::new('powershell.exe', ('-NoProfile -NonInteractive -File "{0}" unexpected' -f $reader))
            $start.UseShellExecute = $false
            $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
            $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
            $process = [Diagnostics.Process]::Start($start)
            $output = $process.StandardOutput.ReadToEnd(); $errors = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            $process.ExitCode | Should -Be 1
            $fallback = '{"label":"unavailable","compactText":"unavailable","progressGlyph":"' + [char]::ConvertFromUtf32(0xF0766) + '","progressColor":"#8995A4","tooltip":"unavailable | age: --","className":"aibar-quota state-unavailable warning-unavailable"}'
            $output | Should -BeExactly ($fallback + "`r`n")
            $errors | Should -Be ''
            $process.Dispose()
        } finally { Remove-Item -LiteralPath $stderrPath -Force }
    }

    It 'renders every valid fixture as its exact state, warning, text, and class' {
        $cases = @(
            @{ Name = 'current.json'; Label = '5h: 12.5% | 7d: 40% | current'; Tooltip = 'current | 5h resets in 1h 0m | 7d resets in 1d 22h | age: 2h'; ClassName = 'aibar-quota state-current' },
            @{ Name = 'refreshing.json'; Label = '5h: 12% | refreshing'; Tooltip = 'refreshing | 5h resets in 1h 0m | age: 2h'; ClassName = 'aibar-quota state-refreshing' },
            @{ Name = 'stale-refresh.json'; Label = '5h: 12% | 7d: 40% | stale | refresh failed'; Tooltip = 'stale | refresh failed | 5h reset: due | 7d resets in 1d 22h | age: 2h'; ClassName = 'aibar-quota state-stale warning-refresh-failed' },
            @{ Name = 'stale-auth.json'; Label = '5h: 12% | 7d: 40% | stale | authentication failed'; Tooltip = 'stale | authentication failed | 5h reset: -- | 7d resets in 1d 22h | age: 2h'; ClassName = 'aibar-quota state-stale warning-authentication-failed' },
            @{ Name = 'unavailable.json'; Label = 'unavailable'; Tooltip = 'unavailable | age: --'; ClassName = 'aibar-quota state-unavailable warning-unavailable' },
            @{ Name = 'disabled.json'; Label = 'disabled'; Tooltip = 'disabled | age: --'; ClassName = 'aibar-quota state-disabled warning-disabled' },
            @{ Name = 'partial.json'; Label = '7d: 40% | current'; Tooltip = 'current | 7d resets in 1d 22h | age: 2h'; ClassName = 'aibar-quota state-current' },
            @{ Name = 'nullable-reset.json'; Label = '5h: 12% | 7d: 40% | current'; Tooltip = 'current | 5h reset: -- | 7d resets in 1d 22h | age: 2h'; ClassName = 'aibar-quota state-current' },
            @{ Name = 'reset-equal.json'; Label = '5h: 12% | current'; Tooltip = 'current | 5h reset: due | age: 0m'; ClassName = 'aibar-quota state-current' }
        )
        $compact = @('40%', '12%', '40% | stale | refresh failed', '40% | stale | authentication failed', 'unavailable', 'disabled', '40%', '40%', '12%')
        $index = 0
        foreach ($case in $cases) {
            $result = Read-Fixture $case.Name
            Assert-Compact $result $compact[$index++]
            Assert-SafeFields $result
            $result.label | Should -Be $case.Label
            $result.tooltip | Should -Be $case.Tooltip
            $result.className | Should -Be $case.ClassName
        }

        $five = [ordered]@{ percentageUsed = 12.5; resetAt = '2026-07-12T13:00:00Z' }
        $weekly35 = [ordered]@{ percentageUsed = 35; resetAt = '2026-07-14T10:00:00Z' }
        $weekly25 = [ordered]@{ percentageUsed = 25; resetAt = '2026-07-14T10:00:00Z' }
        $source = '2026-07-12T10:00:00Z'
        $nullableCases = @(
            @{ Name = 'both windows'; Five = $five; Weekly = $weekly35; State = 'current'; Warning = $null; Source = $source; Label = '5h: 12.5% | 7d: 35% | current'; Tooltip = 'current | 5h resets in 1h 0m | 7d resets in 1d 22h | age: 2h'; ClassName = 'aibar-quota state-current' },
            @{ Name = 'weekly-only 35'; Five = $null; Weekly = $weekly35; State = 'current'; Warning = $null; Source = $source; Label = '7d: 35% | current'; Tooltip = 'current | 7d resets in 1d 22h | age: 2h'; ClassName = 'aibar-quota state-current' },
            @{ Name = 'weekly-only 25'; Five = $null; Weekly = $weekly25; State = 'current'; Warning = $null; Source = $source; Label = '7d: 25% | current'; Tooltip = 'current | 7d resets in 1d 22h | age: 2h'; ClassName = 'aibar-quota state-current' },
            @{ Name = 'five-hour-only'; Five = $five; Weekly = $null; State = 'current'; Warning = $null; Source = $source; Label = '5h: 12.5% | current'; Tooltip = 'current | 5h resets in 1h 0m | age: 2h'; ClassName = 'aibar-quota state-current' },
            @{ Name = 'stale weekly-only'; Five = $null; Weekly = $weekly35; State = 'stale'; Warning = 'refresh-failed'; Source = $source; Label = '7d: 35% | stale | refresh failed'; Tooltip = 'stale | refresh failed | 7d resets in 1d 22h | age: 2h'; ClassName = 'aibar-quota state-stale warning-refresh-failed' },
            @{ Name = 'stale service unavailable'; Five = $null; Weekly = $weekly35; State = 'stale'; Warning = 'unavailable'; Source = $source; Label = '7d: 35% | stale | service unavailable'; Tooltip = 'stale | service unavailable | 7d resets in 1d 22h | age: 2h'; ClassName = 'aibar-quota state-stale warning-unavailable' },
            @{ Name = 'stale five-hour-only'; Five = $five; Weekly = $null; State = 'stale'; Warning = 'authentication-failed'; Source = $source; Label = '5h: 12.5% | stale | authentication failed'; Tooltip = 'stale | authentication failed | 5h resets in 1h 0m | age: 2h'; ClassName = 'aibar-quota state-stale warning-authentication-failed' },
            @{ Name = 'refreshing without windows'; Five = $null; Weekly = $null; State = 'refreshing'; Warning = $null; Source = $null; Label = 'refreshing'; Tooltip = 'refreshing | age: --'; ClassName = 'aibar-quota state-refreshing' },
            @{ Name = 'unavailable without warning'; Five = $null; Weekly = $null; State = 'unavailable'; Warning = $null; Source = $null; Label = 'unavailable'; Tooltip = 'unavailable | age: --'; ClassName = 'aibar-quota state-unavailable' },
            @{ Name = 'zero-window current fails closed'; Five = $null; Weekly = $null; State = 'current'; Warning = $null; Source = $source; Label = 'unavailable'; Tooltip = 'unavailable | age: --'; ClassName = 'aibar-quota state-unavailable warning-unavailable' },
            @{ Name = 'malformed-present sibling fails closed'; Five = [ordered]@{ percentageUsed = -1; resetAt = $null }; Weekly = $weekly35; State = 'current'; Warning = $null; Source = $source; Label = 'unavailable'; Tooltip = 'unavailable | age: --'; ClassName = 'aibar-quota state-unavailable warning-unavailable' }
        )
        $compact = @('35%', '35%', '25%', '12.5%', '35% | stale | refresh failed', '35% | stale | service unavailable', '12.5% | stale | authentication failed', 'refreshing', 'unavailable', 'unavailable', 'unavailable')
        $index = 0
        foreach ($case in $nullableCases) {
            $document = [ordered]@{
                schemaVersion = 1
                generatedAt = '2026-07-12T12:00:00Z'
                state = $case.State
                warning = $case.Warning
                sourceRetrievedAt = $case.Source
                fiveHour = $case.Five
                weekly = $case.Weekly
            }
            $result = Read-Document $document
            Assert-Compact $result $compact[$index++]
            Assert-SafeFields $result
            $result.label | Should -Be $case.Label
            $result.tooltip | Should -Be $case.Tooltip
            $result.className | Should -Be $case.ClassName
            $result.label | Should -Not -Match '5h: --|7d: --|partial'
            $result.tooltip | Should -Not -Match 'state:|warning:'
            $result.className | Should -Not -Match 'partial'
            if ($null -eq $case.Five) { $result.tooltip | Should -Not -Match '5h reset' }
            if ($null -eq $case.Weekly) { $result.tooltip | Should -Not -Match '7d reset' }
        }
        $stages = @(
            @(0, 12.5, 0xF0766), @(12.5, 25, 0xF0A9E), @(25, 37.5, 0xF0A9F),
            @(37.5, 50, 0xF0AA0), @(50, 62.5, 0xF0AA1), @(62.5, 75, 0xF0AA2),
            @(75, 87.5, 0xF0AA3), @(87.5, 100, 0xF0AA4), @(100, 100, 0xF0AA5)
        )
        $culture = [Threading.Thread]::CurrentThread.CurrentCulture
        try {
            [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]'fr-FR'
            foreach ($stage in $stages) {
                $values = if ($stage[0] -eq 100) { @(100) } else { @($stage[0], ($stage[0] + 0.000001), ($stage[1] - 0.000001)) }
                foreach ($value in $values) {
                    $color = if ($value -lt 50) { '#3DDC84' } elseif ($value -lt 75) { '#FFC531' } elseif ($value -lt 90) { '#FF8A3D' } else { '#FF4D5E' }
                    foreach ($weeklyHero in @($true, $false)) {
                        $document = [ordered]@{ schemaVersion = 1; generatedAt = '2026-07-12T12:00:00Z'; state = 'refreshing'; warning = $null; sourceRetrievedAt = $source; fiveHour = [ordered]@{ percentageUsed = $value; resetAt = $null }; weekly = $null }
                        if ($weeklyHero) { $document.weekly = $document.fiveHour; $document.fiveHour = $five }
                        $result = Read-Document $document
                        Assert-SafeFields $result
                        Assert-Compact $result (([double]$value).ToString('R', [Globalization.CultureInfo]::InvariantCulture) + '%')
                        $result.progressGlyph | Should -BeExactly ([char]::ConvertFromUtf32($stage[2]))
                        $result.progressColor | Should -BeExactly $color
                    }
                }
            }
        } finally { [Threading.Thread]::CurrentThread.CurrentCulture = $culture }
        foreach ($value in @(89.999999, 90, 90.000001)) {
            $document.fiveHour.percentageUsed = $value
            (Read-Document $document).progressColor | Should -BeExactly $(if ($value -lt 90) { '#FF8A3D' } else { '#FF4D5E' })
        }
        $document = [ordered]@{ schemaVersion = 1; generatedAt = '2026-07-12T12:00:00Z'; state = 'unavailable'; warning = 'authentication-failed'; sourceRetrievedAt = $null; fiveHour = $null; weekly = $null }
        Assert-Compact (Read-Document $document) 'unavailable | authentication failed'
    }

    It 'fails closed with the exact fallback for malformed or unsupported contracts' {
        $invalid = @(
            'missing.json', 'empty.json', 'oversized.json', 'malformed.json', 'schema-2.json', 'schema-bool.json', 'schema-float.json',
            'extra-field.json', 'property-order.json', 'property-case.json', 'bad-enum.json', 'state-case.json', 'warning-case.json',
            'bad-value.json', 'percentage-bool.json', 'percentage-nan.json', 'percentage-infinity.json', 'future-time.json',
            'source-after-generated.json', 'non-utc-generated.json', 'non-utc-source.json', 'non-utc-reset.json'
        )
        foreach ($name in $invalid) {
            $result = Read-Fixture $name
            Assert-SafeFields $result
            $result.label | Should -Be 'unavailable'
            Assert-Compact $result 'unavailable'
            $result.tooltip | Should -Be 'unavailable | age: --'
            $result.className | Should -Be 'aibar-quota state-unavailable warning-unavailable'
        }
        $rollback = Read-Fixture 'clock-rollback.json' ([DateTimeOffset]'2026-07-12T08:00:00Z')
        Assert-SafeFields $rollback
        $rollback.label | Should -Be 'unavailable'
        Assert-Compact $rollback 'unavailable'
        $rollback.tooltip | Should -Be 'unavailable | age: --'
        $rollback.className | Should -Be 'aibar-quota state-unavailable warning-unavailable'
    }

    It 'uses valid YAML single-quoted Windows command scalars and exact stock keys' {
        $yaml = Get-Content -Raw (Join-Path $root 'custom-widget.example.yaml')
        $expected = @'
aibar_quota:
  type: "yasb.custom.CustomWidget"
  options:
    label: "<img src='<AIBarRoot>\\yasb\\icons\\openai.svg' style='vertical-align: middle'/> <font color='{data[progressColor]}'>{data[progressGlyph]}</font> {data[compactText]}"
    label_alt: "{data[label]}"
    class_name: "aibar-quota"
    tooltip: true
    tooltip_label: "{data[tooltip]}"
    exec_options:
      run_cmd: 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "<AIBarRoot>\yasb\read-aibar-quota.ps1"'
      run_interval: 60000
      return_format: "json"
    callbacks:
      on_left: 'exec "<AIBarRoot>\AIBar.Desktop.exe" --show'
'@
        $blocks = [regex]::Split(($yaml -replace "`r`n", "`n").TrimEnd(), '(?m)(?=^aibar_claude_quota:)')
        $blocks.Count | Should -Be 2
        $actualYaml = $blocks[0].TrimEnd()
        $expectedYaml = ($expected -replace "`r`n", "`n").TrimEnd()
        $actualYaml | Should -BeExactly $expectedYaml
        $expectedClaude = $expectedYaml.Replace('aibar_quota:', 'aibar_claude_quota:').Replace('read-aibar-quota.ps1"''', 'read-aibar-quota.ps1" -Provider Claude''').Replace('icons\\openai.svg', 'icons\\claude.svg')
        $blocks[1].TrimEnd() | Should -BeExactly $expectedClaude
        $yaml | Should -Match '(?m)^    exec_options:\r?$'
        $yaml | Should -Match '(?m)^      run_cmd: ''powershell\.exe .*<AIBarRoot>\\yasb\\read-aibar-quota\.ps1"''\r?$'
        $yaml | Should -Match '(?m)^      run_interval: 60000\r?$'
        $yaml | Should -Match '(?m)^      return_format: "json"\r?$'
        $yaml | Should -Not -Match '(?m)^    (exec|update_interval|return_type):'
        $yaml | Should -Not -Match '(?m)^\s*(run_cmd|on_left):\s*"[^\r\n]*\\'
        $yaml | Should -Not -Match 'on_right|on_middle'
        $module = Get-Module -ListAvailable -Name powershell-yaml | Select-Object -First 1
        if ($null -ne $module) {
            Import-Module $module.Path -ErrorAction Stop
            $parsed = ConvertFrom-Yaml $yaml
            $parsed['aibar_quota']['options']['tooltip'] | Should -BeOfType [bool]
            $parsed['aibar_quota']['options']['tooltip'] | Should -Be $true
            $parsed['aibar_quota']['options']['tooltip_label'] | Should -Be '{data[tooltip]}'
            $parsed['aibar_quota']['options']['class_name'] | Should -Be 'aibar-quota'
            $parsed['aibar_quota']['options']['exec_options']['run_interval'] | Should -Be 60000
            $parsed['aibar_quota']['options']['exec_options']['return_format'] | Should -Be 'json'
            $parsed['aibar_quota']['options']['exec_options']['run_cmd'] | Should -Be 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "<AIBarRoot>\yasb\read-aibar-quota.ps1"'
            $parsed['aibar_quota']['options']['callbacks']['on_left'] | Should -Be 'exec "<AIBarRoot>\AIBar.Desktop.exe" --show'
        }
    }

    It 'shows each reset as a compact countdown without seconds' {
        $cases = @(
            @('2026-07-12T12:00:30Z', '5h resets in under 1m'), @('2026-07-12T12:05:59Z', '5h resets in 5m'),
            @('2026-07-12T15:53:00Z', '5h resets in 3h 53m'), @('2026-07-13T11:59:59Z', '5h resets in 23h 59m'),
            @('2026-07-13T12:00:00Z', '5h resets in 1d 0h'), @('2026-07-15T20:30:00Z', '5h resets in 3d 8h'),
            @('2026-07-12T12:00:00Z', '5h reset: due'), @('2026-07-12T11:00:00Z', '5h reset: due')
        )
        foreach ($case in $cases) {
            $document = [ordered]@{ schemaVersion = 1; generatedAt = '2026-07-12T12:00:00Z'; state = 'current'; warning = $null; sourceRetrievedAt = '2026-07-12T10:00:00Z'; fiveHour = [ordered]@{ percentageUsed = 12; resetAt = $case[0] }; weekly = $null }
            $result = Read-Document $document
            $result.tooltip | Should -BeExactly ('current | ' + $case[1] + ' | age: 2h')
            # Tooltips may render as rich text: no seconds, no UTC suffix, no markup characters.
            $result.tooltip | Should -Not -Match '[0-9]{2}:[0-9]{2}:[0-9]{2}|Z\b|[<>&]'
        }
    }

    It 'ships both official marks as fixed-size local SVG images' {
        foreach ($name in @('openai.svg', 'claude.svg')) {
            $path = Join-Path $root (Join-Path 'icons' $name)
            Test-Path -LiteralPath $path -PathType Leaf | Should -Be $true
            [xml]$svg = Get-Content -Raw -LiteralPath $path
            $svg.svg.width | Should -BeExactly '14'
            $svg.svg.height | Should -BeExactly '14'
            $svg.svg.viewBox | Should -BeExactly '0 0 24 24'
            @($svg.svg.ChildNodes | Where-Object { $_.LocalName -ne 'path' }).Count | Should -Be 0
            $svg.svg.NamespaceURI | Should -BeExactly 'http://www.w3.org/2000/svg'
            # Only the SVG namespace itself may name a URL; no scripts, links or external references.
            (Get-Content -Raw -LiteralPath $path).Replace('xmlns="http://www.w3.org/2000/svg"', '') | Should -Not -Match '(?i)<script|href|https?:|xlink|on[a-z]+='
        }
    }

    It 'keeps the stock launcher and CSS bounded' {
        $cmd = Get-Content -Raw (Join-Path $root 'show-aibar.cmd')
        $css = Get-Content -Raw (Join-Path $root 'custom-widget.example.css')
        $cmd | Should -Match 'AIBar\.Desktop\.exe" --show'
        $cmd | Should -Not -Match '%\*|where |Get-Process'
        $css | Should -Match 'state-stale'
        $css | Should -Match 'warning-authentication-failed'
        $expectedCss = @'
/* Copy and customize; state/warning text remains visible in the widget. */
.aibar-quota { padding: 0 8px; border-radius: 6px; }
.aibar-quota.state-current { background: #244f36; }
.aibar-quota.state-refreshing { background: #29496b; }
.aibar-quota.state-stale { background: #6a5121; }
.aibar-quota.state-unavailable, .aibar-quota.state-disabled { background: #5b3030; }
.aibar-quota.warning-refresh-failed { border-bottom: 2px solid #e0ad45; }
.aibar-quota.warning-authentication-failed { border-bottom: 2px solid #e07b45; }
.aibar-quota.warning-unavailable { border-bottom: 2px solid #e0ad45; }
'@
        foreach ($actualCss in @(($css -replace "`r`n", "`n"), ($css -replace "`r?`n", "`r`n"))) {
            foreach ($expected in @(($expectedCss -replace "`r`n", "`n"), ($expectedCss -replace "`r?`n", "`r`n"))) {
                ($actualCss -replace "`r`n", "`n").TrimEnd() | Should -Be ($expected -replace "`r`n", "`n")
            }
        }
        $css | Should -Not -Match '\.partial|opacity'
    }
}

Describe 'Claude opt-in YASB contract (S4-S5)' {
    BeforeAll {
        function Read-ClaudeFixture([string]$name) {
            $path = Join-Path $root "fixtures/$name"
            $before = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path).Hash } else { $null }
            $result = & $readerModule {
                param($path, $at)
                $script:AIBarQuotaFixturePath = $path
                $script:AIBarQuotaFixtureNow = $at
                Invoke-AiBarQuotaReader -Provider Claude
            } $path $now
            if ($null -ne $before) { (Get-FileHash -LiteralPath $path).Hash | Should -BeExactly $before }
            Assert-SafeFields $result
            return $result
        }
        function Read-ClaudeDocument($document) {
            $path = Join-Path $TestDrive 'claude-quota.json'
            $json = ConvertTo-Json -InputObject $document -Depth 5 -Compress
            [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
            $result = & $readerModule {
                param($path, $at)
                $script:AIBarQuotaFixturePath = $path
                $script:AIBarQuotaFixtureNow = $at
                Invoke-AiBarQuotaReader -Provider Claude
            } $path $now
            [IO.File]::ReadAllText($path) | Should -BeExactly $json
            Assert-SafeFields $result
            return $result
        }
    }

    It 'selects the fixed Claude path without changing the default Codex path' {
        $paths = & $readerModule {
            $prior = $script:AIBarQuotaFixturePath
            try {
                $script:AIBarQuotaFixturePath = $null
                @(Get-ReaderPath; Get-ReaderPath -Provider Claude)
            } finally { $script:AIBarQuotaFixturePath = $prior }
        }
        $local = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
        $paths[0] | Should -BeExactly (Join-Path $local 'AIBar\yasb-quota.json')
        $paths[1] | Should -BeExactly (Join-Path $local 'AIBar\yasb-claude-quota.json')
    }

    It 'names Claude in the alternate label and tooltip while the compact text matches Codex' {
        $cases = @(
            @('current.json', '40%'), @('partial.json', '40%'), @('nullable-reset.json', '40%'),
            @('refreshing.json', '12%'), @('stale-refresh.json', '40% | stale | refresh failed'),
            @('stale-auth.json', '40% | stale | authentication failed'),
            @('unavailable.json', 'unavailable'), @('disabled.json', 'disabled')
        )
        foreach ($case in $cases) {
            $codex = Read-Fixture $case[0]
            $claude = Read-ClaudeFixture $case[0]
            $claude.compactText | Should -BeExactly $case[1]
            $claude.label | Should -BeExactly ('Claude ' + $codex.label)
            $claude.tooltip | Should -BeExactly ('Claude ' + $codex.tooltip)
            $claude.progressGlyph | Should -BeExactly $codex.progressGlyph
            $claude.progressColor | Should -BeExactly $codex.progressColor
            # Selecting Claude must not mutate the module's subsequent default provider.
            ((Read-Fixture $case[0]) | ConvertTo-Json -Compress) | Should -BeExactly ($codex | ConvertTo-Json -Compress)
        }
    }

    It 'fails closed for malformed missing oversized and unsupported Claude contracts' {
        foreach ($name in @('missing.json', 'empty.json', 'oversized.json', 'malformed.json',
            'schema-2.json', 'schema-bool.json', 'schema-float.json', 'extra-field.json',
            'property-order.json', 'property-case.json', 'bad-enum.json', 'state-case.json',
            'warning-case.json', 'bad-value.json', 'percentage-bool.json', 'percentage-nan.json',
            'percentage-infinity.json', 'future-time.json', 'source-after-generated.json',
            'non-utc-generated.json', 'non-utc-source.json', 'non-utc-reset.json')) {
            $result = Read-ClaudeFixture $name
            $result.label | Should -BeExactly 'Claude unavailable'
            Assert-Compact $result 'unavailable'
            $result.tooltip | Should -BeExactly 'Claude unavailable | age: --'
        }
    }

    It 'retains weekly-first glyph and color boundaries including five-hour-only output' {
        $culture = [Threading.Thread]::CurrentThread.CurrentCulture
        try {
            [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]'fr-FR'
            foreach ($value in @(0, 12.499999, 12.5, 25, 37, 37.5, 49.999999, 50, 62.5, 74.999999, 75, 87.5, 89.999999, 90, 100)) {
                foreach ($weekly in @($true, $false)) {
                    $window = [ordered]@{ percentageUsed = $value; resetAt = $null }
                    $document = [ordered]@{ schemaVersion = 1; generatedAt = '2026-07-12T12:00:00Z'; state = 'current'; warning = $null; sourceRetrievedAt = '2026-07-12T10:00:00Z'; fiveHour = $window; weekly = $null }
                    if ($weekly) { $document.weekly = $window; $document.fiveHour = [ordered]@{ percentageUsed = 99; resetAt = $null } }
                    $result = Read-ClaudeDocument $document
                    Assert-Compact $result (([double]$value).ToString('R', [Globalization.CultureInfo]::InvariantCulture) + '%')
                    $glyphs = @(0xF0766, 0xF0A9E, 0xF0A9F, 0xF0AA0, 0xF0AA1, 0xF0AA2, 0xF0AA3, 0xF0AA4, 0xF0AA5)
                    $result.progressGlyph | Should -BeExactly ([char]::ConvertFromUtf32($glyphs[[int][math]::Min(8, [math]::Floor($value / 12.5))]))
                    $result.progressColor | Should -BeExactly $(if ($value -lt 50) { '#3DDC84' } elseif ($value -lt 75) { '#FFC531' } elseif ($value -lt 90) { '#FF8A3D' } else { '#FF4D5E' })
                }
            }
        } finally { [Threading.Thread]::CurrentThread.CurrentCulture = $culture }
    }

    It 'accepts only the exact opt-in CLI pair and returns one safe JSON object without stderr' {
        $start = [Diagnostics.ProcessStartInfo]::new('powershell.exe', ('-NoProfile -NonInteractive -File "{0}" -Provider Claude' -f $reader))
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
        $process = [Diagnostics.Process]::Start($start)
        try {
            $output = $process.StandardOutput.ReadToEnd(); $stderr = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            $process.ExitCode | Should -Be 0
            $stderr | Should -BeExactly ''
            $result = $output | ConvertFrom-Json -ErrorAction Stop
            Assert-SafeFields $result
            $result.label | Should -Match '^Claude '
            $result.compactText | Should -Not -Match 'Claude'
            $result.tooltip | Should -Match '^Claude '
        } finally { $process.Dispose() }
    }

    It 'rejects unknown provider values missing values duplicate switches and extra CLI arguments safely' {
        # Case-sensitive allowlist: the only accepted nonempty argument list is -Provider Claude.
        foreach ($arguments in @('-Provider Other', '-Provider Codex', '-Provider claude', '-Provider',
            '-provider Claude', '-Provider Claude extra', '-Provider Claude -Provider Claude', '-Provider "<secret>&{}"')) {
            $start = [Diagnostics.ProcessStartInfo]::new('powershell.exe', ('-NoProfile -NonInteractive -File "{0}" {1}' -f $reader, $arguments))
            $start.UseShellExecute = $false
            $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
            $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
            $process = [Diagnostics.Process]::Start($start)
            try {
                $output = $process.StandardOutput.ReadToEnd(); $stderr = $process.StandardError.ReadToEnd()
                $process.WaitForExit()
                $process.ExitCode | Should -Be 1
                $stderr | Should -BeExactly ''
                $result = $output | ConvertFrom-Json -ErrorAction Stop
                Assert-SafeFields $result
                Assert-Compact $result 'unavailable'
                $result.label | Should -BeExactly 'unavailable'
                $result.tooltip | Should -BeExactly 'unavailable | age: --'
                $output | Should -Not -Match 'secret|Other|extra|<|>|&'
            } finally { $process.Dispose() }
        }
    }

    It 'adds exactly the second widget with identical options and the same panel callback' {
        $yaml = ((Get-Content -Raw (Join-Path $root 'custom-widget.example.yaml')) -replace "`r`n", "`n").TrimEnd()
        $parts = [regex]::Split($yaml, '(?m)(?=^aibar_claude_quota:)')
        $parts.Count | Should -Be 2
        $codex = $parts[0].TrimEnd()
        $claude = $parts[1].TrimEnd()
        $expected = $codex.Replace('aibar_quota:', 'aibar_claude_quota:').Replace('read-aibar-quota.ps1"''', 'read-aibar-quota.ps1" -Provider Claude''').Replace('icons\\openai.svg', 'icons\\claude.svg')
        $claude | Should -BeExactly $expected
        ([regex]::Matches($yaml, 'on_left: ''exec "<AIBarRoot>\\AIBar.Desktop.exe" --show''')).Count | Should -Be 2
    }

    It 'never echoes credential token plan or unsafe text from Claude documents' {
        foreach ($attack in @('token-canary', 'C:\private\credentials.json', 'plan-canary', '<font>&{}', ('x' * 256))) {
            $document = [ordered]@{ schemaVersion = 1; generatedAt = '2026-07-12T12:00:00Z'; state = 'current'; warning = $null; sourceRetrievedAt = '2026-07-12T10:00:00Z'; fiveHour = $null; weekly = [ordered]@{ percentageUsed = 37; resetAt = $null }; plan = $attack }
            $result = Read-ClaudeDocument $document
            Assert-Compact $result 'unavailable'
            $document.Remove('plan')
            $document.weekly.percentageUsed = $attack
            $result = Read-ClaudeDocument $document
            Assert-Compact $result 'unavailable'
            foreach ($field in @($result.label, $result.compactText, $result.tooltip)) {
                $field | Should -Not -Match 'canary|credential|C:|<|>|&|\{|\}'
            }
        }
    }
}

Describe 'Settings YASB colors' {
    BeforeAll {
        function Read-WithAppearance([string]$appearance, [string]$provider = '') {
            $path = Join-Path $TestDrive 'yasb-quota.json'
            Copy-Item (Join-Path $root 'fixtures/current.json') $path -Force
            [IO.File]::WriteAllText((Join-Path $TestDrive 'appearance.json'), $appearance)
            return & $readerModule {
                param($path, $at, $provider)
                $script:AIBarQuotaFixturePath = $path
                $script:AIBarQuotaFixtureNow = $at
                Invoke-AiBarQuotaReader -Provider $provider
            } $path $now $provider
        }
    }
    It 'uses exact provider brand colors while retaining six fields and usage text' {
        foreach ($provider in @('', 'Claude')) {
            $result = Read-WithAppearance '{"schema":1,"barColors":"brand","yasbColors":"brand"}' $provider
            $result.progressColor | Should -BeExactly $(if ($provider -ceq 'Claude') { '#E0876A' } else { '#2FBF98' })
            $result.compactText | Should -BeExactly '40%'
            (@($result.PSObject.Properties.Name) -join ',') | Should -BeExactly 'label,compactText,progressGlyph,progressColor,tooltip,className'
        }
    }
    It 'strictly falls back to the usage ramp for missing invalid or unsupported preferences' {
        foreach ($json in @('', '{', '{}', '{"schema":2,"yasbColors":"brand"}', '{"schema":true,"yasbColors":"brand"}', '{"schema":1,"yasbColors":"Brand"}', '{"schema":1,"yasbColors":true}', '{"schema":1,"yasbColors":"<unsafe>"}', ('x' * 4097), '{"schema":1,"yasbColors":"usage"}', '{"schema":1}')) {
            $result = Read-WithAppearance $json
            Assert-SafeFields $result
            $result.progressColor | Should -BeExactly '#3DDC84'
            $result.compactText | Should -BeExactly '40%'
        }
    }
    It 'keeps unavailable gray in brand mode and reads settings without modifying bytes' {
        $path = Join-Path $TestDrive 'yasb-quota.json'
        $settings = Join-Path $TestDrive 'appearance.json'
        Copy-Item (Join-Path $root 'fixtures/unavailable.json') $path -Force
        [IO.File]::WriteAllText($settings, '{"schema":1,"yasbColors":"brand"}')
        $before = (Get-FileHash $settings).Hash
        $result = & $readerModule {
            param($path, $at)
            $script:AIBarQuotaFixturePath = $path; $script:AIBarQuotaFixtureNow = $at
            Invoke-AiBarQuotaReader
        } $path $now
        $result.progressColor | Should -BeExactly '#8995A4'
        (Get-FileHash $settings).Hash | Should -BeExactly $before
    }
}

Describe 'AIBar YASB rollback cleanup' {
    BeforeEach {
        $script:cleanupRoot = Join-Path ([IO.Path]::GetTempPath()) ('aibar-yasb-cleanup-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:cleanupRoot | Out-Null
        $script:cleanupPath = Join-Path $script:cleanupRoot 'yasb-quota.json'
        . (Join-Path $root 'remove-aibar-quota.ps1')
        $script:AIBarQuotaCleanupFixturePath = $script:cleanupPath
        $script:AIBarQuotaCleanupTestDelete = $null
        $script:AIBarQuotaCleanupTestReplace = $null
        $script:AIBarQuotaCleanupTestReparse = $null
    }

    AfterEach { if (Test-Path -LiteralPath $script:cleanupRoot) { Remove-Item -LiteralPath $script:cleanupRoot -Recurse -Force } }

    It 'uses no public parameters and rejects arguments before fixed-path access' {
        $cleanup = Join-Path $root 'remove-aibar-quota.ps1'
        $tokens = $null; $errors = $null; $ast = [Management.Automation.Language.Parser]::ParseFile($cleanup, [ref]$tokens, [ref]$errors)
        $errors.Count | Should -Be 0
        $ast.ParamBlock.Parameters.Count | Should -Be 0
        $source = Get-Content -LiteralPath $cleanup -Raw
        $source.IndexOf('if ($args.Count -ne 0)') | Should -BeLessThan $source.IndexOf('if (Invoke-AiBarQuotaCleanup)')
        & powershell.exe -NoProfile -NonInteractive -File $cleanup unexpected
        $LASTEXITCODE | Should -Be 64
    }

    It 'deletes a valued nullable-reset snapshot and treats absence as a no-op' {
        '{"schemaVersion":1,"generatedAt":"2026-07-12T12:00:00Z","state":"current","warning":null,"sourceRetrievedAt":"2026-07-12T11:00:00Z","fiveHour":{"percentageUsed":12.5,"resetAt":null},"weekly":null}' | Set-Content -LiteralPath $script:cleanupPath -NoNewline
        (Invoke-AiBarQuotaCleanup) | Should -Be $true
        (Test-Path -LiteralPath $script:cleanupPath) | Should -Be $false
        (Invoke-AiBarQuotaCleanup) | Should -Be $true
    }

    It 'atomically replaces a delete-denied snapshot with exact BOM-less canonical bytes' {
        '{"valued":true}' | Set-Content -LiteralPath $script:cleanupPath -NoNewline
        $script:AIBarQuotaCleanupTestDelete = { param($Path) throw 'denied' }
        (Invoke-AiBarQuotaCleanup) | Should -Be $true
        $bytes = [IO.File]::ReadAllBytes($script:cleanupPath)
        ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) | Should -Be $true
        $snapshot = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        (Test-DisabledSnapshot $script:cleanupPath $snapshot) | Should -Be $true
        @(Get-ChildItem -LiteralPath $script:cleanupRoot -Filter '.yasb-quota-cleanup-*.tmp').Count | Should -Be 0
        @(Get-ChildItem -LiteralPath $script:cleanupRoot -Filter '.yasb-quota-cleanup-*.bak').Count | Should -Be 0
    }

    It 'rejects byte-equal boolean schema and malformed or non-UTC generatedAt snapshots' {
        foreach ($snapshot in @(
        '{"schemaVersion":true,"generatedAt":"2026-07-12T12:00:00.0000000+00:00","state":"disabled","warning":"disabled","sourceRetrievedAt":null,"fiveHour":null,"weekly":null}',
        '{"schemaVersion":1,"generatedAt":"invalid","state":"disabled","warning":"disabled","sourceRetrievedAt":null,"fiveHour":null,"weekly":null}',
        '{"schemaVersion":1,"generatedAt":"2026-07-12T12:00:00.0000000+01:00","state":"disabled","warning":"disabled","sourceRetrievedAt":null,"fiveHour":null,"weekly":null}'
        )) {
        [IO.File]::WriteAllText($script:cleanupPath, $snapshot, [Text.UTF8Encoding]::new($false))
        (Test-DisabledSnapshot $script:cleanupPath $snapshot) | Should -Be $false
        }
    }

    It 'fails closed after post-replacement reparse substitution and cleans bounded temps' {
        '{"valued":true}' | Set-Content -LiteralPath $script:cleanupPath -NoNewline
        $script:AIBarQuotaCleanupTestDelete = { param($Path) throw 'denied' }
        $script:destinationChecks = 0
        $script:AIBarQuotaCleanupTestReplace = { param($Temporary, $Path) [IO.File]::Delete($Path); [IO.File]::Move($Temporary, $Path) }
        $script:AIBarQuotaCleanupTestReparse = { param($Path) if ($Path -ceq $script:cleanupPath) { $script:destinationChecks++; return $script:destinationChecks -ge 3 }; return $false }
        (Invoke-AiBarQuotaCleanup) | Should -Be $false
        $script:destinationChecks | Should -Be 3
        (Test-Path -LiteralPath $script:cleanupPath) | Should -Be $true
        @(Get-ChildItem -LiteralPath $script:cleanupRoot -Filter '.yasb-quota-cleanup-*.tmp').Count | Should -Be 0
        @(Get-ChildItem -LiteralPath $script:cleanupRoot -Filter '.yasb-quota-cleanup-*.bak').Count | Should -Be 0
    }

    It 'fails closed and cleans bounded temps for precommit replacement and exact read-back mutation' {
        '{"valued":true}' | Set-Content -LiteralPath $script:cleanupPath -NoNewline
        $script:AIBarQuotaCleanupTestDelete = { param($Path) throw 'denied' }
        $script:AIBarQuotaCleanupTestReplace = { param($Temporary, $Path) throw 'replace failed' }
        (Invoke-AiBarQuotaCleanup) | Should -Be $false
        $script:AIBarQuotaCleanupTestReplace = { param($Temporary, $Path) [IO.File]::Delete($Path); [IO.File]::Move($Temporary, $Path); [IO.File]::AppendAllText($Path, ' ') }
        (Invoke-AiBarQuotaCleanup) | Should -Be $false
        [IO.File]::ReadAllText($script:cleanupPath).EndsWith(' ') | Should -Be $true
        @(Get-ChildItem -LiteralPath $script:cleanupRoot -Filter '.yasb-quota-cleanup-*.tmp').Count | Should -Be 0
        @(Get-ChildItem -LiteralPath $script:cleanupRoot -Filter '.yasb-quota-cleanup-*.bak').Count | Should -Be 0
    }
}
