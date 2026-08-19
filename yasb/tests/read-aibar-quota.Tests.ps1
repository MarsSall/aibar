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
