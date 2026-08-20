$root = Split-Path -Parent $PSScriptRoot
$reader = Join-Path $root 'read-aibar-quota.ps1'
$now = [DateTimeOffset]'2026-07-12T12:00:00Z'

function Read-Fixture([string]$name, [DateTimeOffset]$at = $now) {
    . $reader
    $script:AIBarQuotaFixturePath = Join-Path $root "fixtures/$name"
    $script:AIBarQuotaFixtureNow = $at
    Invoke-AiBarQuotaReader
}

function Assert-SafeFields($result) {
    $names = @($result.PSObject.Properties.Name)
    $names.Count | Should Be 3
    $names[0] | Should Be 'label'
    $names[1] | Should Be 'tooltip'
    $names[2] | Should Be 'className'
}

Describe 'AIBar YASB reader' {
    It 'has an empty production parameter surface, one trusted clock read, and bounded read-only behavior' {
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($reader, [ref]$tokens, [ref]$errors)
        $source = Get-Content -Raw $reader
        $errors.Count | Should Be 0
        ($null -eq $ast.ParamBlock) | Should Be $true
        ([regex]::Matches($source, '\[DateTimeOffset\]::UtcNow')).Count | Should Be 1
        $source | Should Match 'catch\s*\{\s*return Get-SafeOutput -Now \$now\s*\}'
        $source | Should Match 'return Get-SafeOutput .* -Now \$now'
        $source | Should Match 'AIBar\\yasb-quota\.json'
        $source | Should Match '\[IO\.FileAccess\]::Read'
        $source | Should Match '16385'
        $source | Should Match '-gt 16384'
        $source | Should Match 'StringComparison\]::Ordinal'
        $source | Should Match 'switch -CaseSensitive'
        $source | Should Match '-cnotin'
        $source | Should Not Match 'Start-Process|Invoke-WebRequest|Remove-Item|Set-Content|Add-Content|WriteAll|CreateDirectory'
    }

    It 'renders every valid fixture as its exact state, warning, text, and class' {
        $cases = @(
            @{ Name = 'current.json'; Label = '5h: 12.5% | 7d: 40% | current'; Tooltip = 'state: current | 5h reset: 2026-07-12 13:00:00Z | 7d reset: 2026-07-14 10:00:00Z | age: 2h'; ClassName = 'aibar-quota state-current' },
            @{ Name = 'refreshing.json'; Label = '5h: 12% | 7d: -- | refreshing partial'; Tooltip = 'state: refreshing partial | 5h reset: 2026-07-12 13:00:00Z | 7d reset: -- | age: 2h'; ClassName = 'aibar-quota state-refreshing partial' },
            @{ Name = 'stale-refresh.json'; Label = '5h: 12% | 7d: 40% | stale | warning: refresh-failed'; Tooltip = 'state: stale | warning: refresh-failed | 5h reset: due | 7d reset: 2026-07-14 10:00:00Z | age: 2h'; ClassName = 'aibar-quota state-stale warning-refresh-failed' },
            @{ Name = 'stale-auth.json'; Label = '5h: 12% | 7d: 40% | stale | warning: authentication-failed'; Tooltip = 'state: stale | warning: authentication-failed | 5h reset: -- | 7d reset: 2026-07-14 10:00:00Z | age: 2h'; ClassName = 'aibar-quota state-stale warning-authentication-failed' },
            @{ Name = 'unavailable.json'; Label = '5h: -- | 7d: -- | unavailable | warning: unavailable'; Tooltip = 'state: unavailable | warning: unavailable | 5h reset: -- | 7d reset: -- | age: --'; ClassName = 'aibar-quota state-unavailable warning-unavailable' },
            @{ Name = 'disabled.json'; Label = '5h: -- | 7d: -- | disabled | warning: disabled'; Tooltip = 'state: disabled | warning: disabled | 5h reset: -- | 7d reset: -- | age: --'; ClassName = 'aibar-quota state-disabled warning-disabled' },
            @{ Name = 'partial.json'; Label = '5h: -- | 7d: 40% | current partial'; Tooltip = 'state: current partial | 5h reset: -- | 7d reset: 2026-07-14 10:00:00Z | age: 2h'; ClassName = 'aibar-quota state-current partial' },
            @{ Name = 'nullable-reset.json'; Label = '5h: 12% | 7d: 40% | current'; Tooltip = 'state: current | 5h reset: -- | 7d reset: 2026-07-14 10:00:00Z | age: 2h'; ClassName = 'aibar-quota state-current' },
            @{ Name = 'reset-equal.json'; Label = '5h: 12% | 7d: -- | current partial'; Tooltip = 'state: current partial | 5h reset: due | 7d reset: -- | age: 0m'; ClassName = 'aibar-quota state-current partial' }
        )
        foreach ($case in $cases) {
            $result = Read-Fixture $case.Name
            Assert-SafeFields $result
            $result.label | Should Be $case.Label
            $result.tooltip | Should Be $case.Tooltip
            $result.className | Should Be $case.ClassName
        }
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
            $result.label | Should Be '5h: -- | 7d: -- | unavailable | warning: unavailable'
            $result.tooltip | Should Be 'state: unavailable | warning: unavailable | 5h reset: -- | 7d reset: -- | age: --'
            $result.className | Should Be 'aibar-quota state-unavailable warning-unavailable'
        }
        $rollback = Read-Fixture 'clock-rollback.json' ([DateTimeOffset]'2026-07-12T08:00:00Z')
        Assert-SafeFields $rollback
        $rollback.label | Should Be '5h: -- | 7d: -- | unavailable | warning: unavailable'
        $rollback.tooltip | Should Be 'state: unavailable | warning: unavailable | 5h reset: -- | 7d reset: -- | age: --'
        $rollback.className | Should Be 'aibar-quota state-unavailable warning-unavailable'
    }

    It 'uses valid YAML single-quoted Windows command scalars and exact stock keys' {
        $yaml = Get-Content -Raw (Join-Path $root 'custom-widget.example.yaml')
        $expected = @'
aibar_quota:
  type: "yasb.custom.CustomWidget"
  options:
    label: "{data[label]}"
    label_alt: "{data[label]}"
    class_name: "{data[className]}"
    tooltip: "{data[tooltip]}"
    exec_options:
      run_cmd: 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "<AIBarRoot>\yasb\read-aibar-quota.ps1"'
      run_interval: 60000
      return_format: "json"
    callbacks:
      on_left: 'exec cmd /c "<AIBarRoot>\yasb\show-aibar.cmd"'
'@
        $actualYaml = ($yaml -replace "`r`n", "`n").TrimEnd()
        $expectedYaml = ($expected -replace "`r`n", "`n").TrimEnd()
        $actualYaml | Should Be $expectedYaml
        $yaml | Should Match '(?m)^    exec_options:\r?$'
        $yaml | Should Match '(?m)^      run_cmd: ''powershell\.exe .*<AIBarRoot>\\yasb\\read-aibar-quota\.ps1"''\r?$'
        $yaml | Should Match '(?m)^      run_interval: 60000\r?$'
        $yaml | Should Match '(?m)^      return_format: "json"\r?$'
        $yaml | Should Not Match '(?m)^    (exec|update_interval|return_type):'
        $yaml | Should Not Match '(?m)^\s*(run_cmd|on_left):\s*"[^\r\n]*\\'
        $yaml | Should Not Match 'on_right|on_middle'
        $module = Get-Module -ListAvailable -Name powershell-yaml | Select-Object -First 1
        if ($null -ne $module) {
            Import-Module $module.Path -ErrorAction Stop
            $parsed = ConvertFrom-Yaml $yaml
            $parsed['aibar_quota']['options']['exec_options']['run_interval'] | Should Be 60000
            $parsed['aibar_quota']['options']['exec_options']['return_format'] | Should Be 'json'
            $parsed['aibar_quota']['options']['exec_options']['run_cmd'] | Should Be 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "<AIBarRoot>\yasb\read-aibar-quota.ps1"'
            $parsed['aibar_quota']['options']['callbacks']['on_left'] | Should Be 'exec cmd /c "<AIBarRoot>\yasb\show-aibar.cmd"'
        }
    }

    It 'keeps the stock launcher and CSS bounded' {
        $cmd = Get-Content -Raw (Join-Path $root 'show-aibar.cmd')
        $css = Get-Content -Raw (Join-Path $root 'custom-widget.example.css')
        $cmd | Should Match 'AIBar\.Desktop\.exe" --show'
        $cmd | Should Not Match '%\*|where |Get-Process'
        $css | Should Match 'state-stale'
        $css | Should Match 'warning-authentication-failed'
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
        $errors.Count | Should Be 0
        $ast.ParamBlock.Parameters.Count | Should Be 0
        $source = Get-Content -LiteralPath $cleanup -Raw
        $source.IndexOf('if ($args.Count -ne 0)') | Should BeLessThan $source.IndexOf('if (Invoke-AiBarQuotaCleanup)')
        & powershell.exe -NoProfile -NonInteractive -File $cleanup unexpected
        $LASTEXITCODE | Should Be 64
    }

    It 'deletes a valued nullable-reset snapshot and treats absence as a no-op' {
        '{"schemaVersion":1,"generatedAt":"2026-07-12T12:00:00Z","state":"current","warning":null,"sourceRetrievedAt":"2026-07-12T11:00:00Z","fiveHour":{"percentageUsed":12.5,"resetAt":null},"weekly":null}' | Set-Content -LiteralPath $script:cleanupPath -NoNewline
        (Invoke-AiBarQuotaCleanup) | Should Be $true
        (Test-Path -LiteralPath $script:cleanupPath) | Should Be $false
        (Invoke-AiBarQuotaCleanup) | Should Be $true
    }

    It 'atomically replaces a delete-denied snapshot with exact BOM-less canonical bytes' {
        '{"valued":true}' | Set-Content -LiteralPath $script:cleanupPath -NoNewline
        $script:AIBarQuotaCleanupTestDelete = { param($Path) throw 'denied' }
        (Invoke-AiBarQuotaCleanup) | Should Be $true
        $bytes = [IO.File]::ReadAllBytes($script:cleanupPath)
        ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) | Should Be $true
        $snapshot = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        (Test-DisabledSnapshot $script:cleanupPath $snapshot) | Should Be $true
        @(Get-ChildItem -LiteralPath $script:cleanupRoot -Filter '.yasb-quota-cleanup-*.tmp').Count | Should Be 0
        @(Get-ChildItem -LiteralPath $script:cleanupRoot -Filter '.yasb-quota-cleanup-*.bak').Count | Should Be 0
    }

    It 'rejects byte-equal boolean schema and malformed or non-UTC generatedAt snapshots' {
        foreach ($snapshot in @(
        '{"schemaVersion":true,"generatedAt":"2026-07-12T12:00:00.0000000+00:00","state":"disabled","warning":"disabled","sourceRetrievedAt":null,"fiveHour":null,"weekly":null}',
        '{"schemaVersion":1,"generatedAt":"invalid","state":"disabled","warning":"disabled","sourceRetrievedAt":null,"fiveHour":null,"weekly":null}',
        '{"schemaVersion":1,"generatedAt":"2026-07-12T12:00:00.0000000+01:00","state":"disabled","warning":"disabled","sourceRetrievedAt":null,"fiveHour":null,"weekly":null}'
        )) {
        [IO.File]::WriteAllText($script:cleanupPath, $snapshot, [Text.UTF8Encoding]::new($false))
        (Test-DisabledSnapshot $script:cleanupPath $snapshot) | Should Be $false
        }
    }

    It 'fails closed after post-replacement reparse substitution and cleans bounded temps' {
        '{"valued":true}' | Set-Content -LiteralPath $script:cleanupPath -NoNewline
        $script:AIBarQuotaCleanupTestDelete = { param($Path) throw 'denied' }
        $script:destinationChecks = 0
        $script:AIBarQuotaCleanupTestReplace = { param($Temporary, $Path) [IO.File]::Delete($Path); [IO.File]::Move($Temporary, $Path) }
        $script:AIBarQuotaCleanupTestReparse = { param($Path) if ($Path -ceq $script:cleanupPath) { $script:destinationChecks++; return $script:destinationChecks -ge 3 }; return $false }
        (Invoke-AiBarQuotaCleanup) | Should Be $false
        $script:destinationChecks | Should Be 3
        (Test-Path -LiteralPath $script:cleanupPath) | Should Be $true
        @(Get-ChildItem -LiteralPath $script:cleanupRoot -Filter '.yasb-quota-cleanup-*.tmp').Count | Should Be 0
        @(Get-ChildItem -LiteralPath $script:cleanupRoot -Filter '.yasb-quota-cleanup-*.bak').Count | Should Be 0
    }

    It 'fails closed and cleans bounded temps for precommit replacement and exact read-back mutation' {
        '{"valued":true}' | Set-Content -LiteralPath $script:cleanupPath -NoNewline
        $script:AIBarQuotaCleanupTestDelete = { param($Path) throw 'denied' }
        $script:AIBarQuotaCleanupTestReplace = { param($Temporary, $Path) throw 'replace failed' }
        (Invoke-AiBarQuotaCleanup) | Should Be $false
        $script:AIBarQuotaCleanupTestReplace = { param($Temporary, $Path) [IO.File]::Delete($Path); [IO.File]::Move($Temporary, $Path); [IO.File]::AppendAllText($Path, ' ') }
        (Invoke-AiBarQuotaCleanup) | Should Be $false
        [IO.File]::ReadAllText($script:cleanupPath).EndsWith(' ') | Should Be $true
        @(Get-ChildItem -LiteralPath $script:cleanupRoot -Filter '.yasb-quota-cleanup-*.tmp').Count | Should Be 0
        @(Get-ChildItem -LiteralPath $script:cleanupRoot -Filter '.yasb-quota-cleanup-*.bak').Count | Should Be 0
    }
}
