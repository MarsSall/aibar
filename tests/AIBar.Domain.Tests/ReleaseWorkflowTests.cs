using System.IO;
using System.Text.RegularExpressions;

namespace AIBar.Domain.Tests;

public sealed class ReleaseWorkflowTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AIBar.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    private static string Workflow => File.ReadAllText(Path.Combine(Root, ".github", "workflows", "release.yml")).Replace("\r\n", "\n");

    private sealed record Step(string Name, string Text)
    {
        public string Run => Regex.Replace(
            Regex.Match(Text, @"(?ms)^        run: (.*?)(?=^        \S|\z)").Groups[1].Value,
            @"\A[|>][-+]?\n", "");
        public string Env => Regex.Match(Text, @"(?ms)^        env:\n(.*?)(?=^        \S|\z)").Groups[1].Value;
    }

    private static Step[] Steps(string text) => Regex.Matches(text,
        @"(?ms)^      - name: ([^\n]+)\n(.*?)(?=^      - name:|\z)")
        .Select(match => new Step(match.Groups[1].Value, match.Value)).ToArray();

    private static Step NamedStep(string name) => Assert.Single(Steps(Workflow), step => step.Name == name);

    private static void InOrder(string text, params string[] fragments)
    {
        var cursor = 0;
        foreach (var fragment in fragments)
        {
            var index = text.IndexOf(fragment, cursor, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Missing or out-of-order fragment: {fragment}");
            cursor = index + fragment.Length;
        }
    }

    [Fact]
    public void Guard_lists_all_releases_including_drafts_and_fails_closed_before_create()
    {
        var guard = NamedStep("Reject existing releases including drafts");
        InOrder(guard.Run, "gh api", "--paginate", "--slurp", "$LASTEXITCODE -ne 0", "throw", "ConvertFrom-Json", "foreach ($page in $pages)", "foreach ($release in $page)", "$release.tag_name -ceq $env:GITHUB_REF_NAME", "throw");
        Assert.Contains("repos/$env:GITHUB_REPOSITORY/releases?per_page=100", guard.Run);
        Assert.DoesNotContain("gh release view", guard.Run);
        Assert.DoesNotContain(".draft", guard.Run); // Never filter drafts out of the release list.
        Assert.True(Array.FindIndex(Steps(Workflow), step => step.Name == guard.Name) <
                    Array.FindIndex(Steps(Workflow), step => step.Name == "Create draft release with four assets"));
    }

    [Fact]
    public void Token_is_only_in_guard_and_create_step_environments_and_runs_have_no_expressions()
    {
        var text = Workflow;
        var steps = Steps(text);
        Assert.DoesNotContain("GITHUB_TOKEN", text[..text.IndexOf("      - name:", StringComparison.Ordinal)]);
        foreach (var step in steps)
        {
            Assert.DoesNotContain("${{", step.Run); // Empty allowlist: use process environment variables.
            if (step.Name is "Reject existing releases including drafts" or "Create draft release with four assets")
            {
                Assert.Contains("GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}", step.Env);
                Assert.Single(Regex.Matches(step.Text, "GITHUB_TOKEN"));
            }
            else
                Assert.DoesNotContain("GITHUB_TOKEN", step.Text);
        }
        Assert.Equal(2, steps.Count(step => step.Env.Contains("secrets.GITHUB_TOKEN", StringComparison.Ordinal)));
        Assert.DoesNotMatch(@"gh release (edit|delete|upload)\b|--clobber", text);
    }

    [Fact]
    public void Compiler_is_hashed_before_execution_and_revalidated_after_install()
    {
        var run = NamedStep("Ensure Inno Setup 6.7.3 and build installer").Run;
        InOrder(run, "Invoke-WebRequest", "Get-FileHash -LiteralPath $installer -Algorithm SHA256",
            "if ($actualHash -cne $expectedHash)", "throw", "Start-Process -FilePath $installer",
            "$process.ExitCode -ne 0", "if (-not (Test-PinnedCompiler $compiler))", "throw", "Build-Installer.ps1");
        Assert.Contains("-eq '6.7.3'", run);
        // ISCC.exe and the compiler DLLs carry no version resource (0.0.0.0); the installed uninstaller does.
        Assert.Contains("'unins000.exe'", run);
        Assert.Contains("ProductVersion", run);
        Assert.DoesNotContain("FileMajorPart", run);
    }

    [Fact]
    public void Packaging_output_is_runner_temp_and_preceding_steps_do_not_write_source_files()
    {
        var steps = Steps(Workflow);
        var package = NamedStep("Package portable release");
        Assert.Contains("-OutputDirectory $env:RELEASE_DIRECTORY", package.Run);
        var preceding = steps.TakeWhile(step => step.Name != package.Name).ToArray();
        var validation = Assert.Single(preceding, step => step.Name == "Validate tag against project version");
        Assert.Contains("RELEASE_DIRECTORY=$env:RUNNER_TEMP\\aibar-release", validation.Run);
        foreach (var step in preceding)
        {
            // Build outputs are ignored bin/obj; explicitly generated files must not dirty the checkout.
            Assert.DoesNotMatch(@"(?i)Set-Content|Out-File|WriteAllText|Copy-Item|New-Item|Invoke-WebRequest", step.Run);
            foreach (Match redirect in Regex.Matches(step.Run, @">> ?([^\r\n]+)"))
                Assert.Equal("$env:GITHUB_ENV", redirect.Groups[1].Value);
            Assert.DoesNotMatch(@"(?m)(?<!>)>(?!>)", step.Run);
        }
        Assert.Contains("--results-directory \"$env:RUNNER_TEMP", NamedStep("Test").Run);
    }

    [Fact]
    public void Notes_replacements_cover_every_template_placeholder()
    {
        var run = NamedStep("Create draft release with four assets").Run;
        var template = File.ReadAllText(Path.Combine(Root, "packaging", "release-notes.md"));
        var replacements = Regex.Matches(run, @"\.Replace\('([^']+)', ([^)]+)\)");
        Assert.NotEmpty(replacements);
        var placeholders = Regex.Matches(template, @"\{\{[^{}]+\}\}").Select(match => match.Value).Distinct().Order().ToArray();
        Assert.Equal(placeholders, replacements.Select(match => match.Groups[1].Value).Distinct().Order().ToArray());
        foreach (Match replacement in replacements)
        {
            Assert.Contains(replacement.Groups[2].Value, new[] { "$version", "$env:GITHUB_SHA" });
            template = template.Replace(replacement.Groups[1].Value, "rendered-value", StringComparison.Ordinal);
        }
        Assert.DoesNotMatch(@"\{\{.*?\}\}", template);
    }

    [Fact]
    public void Trigger_is_only_v_tags_and_runs_are_serialized_per_tag()
    {
        var text = Workflow;
        Assert.Equal("on:\n  push:\n    tags:\n      - 'v*'", text.Split("\n\n")[1]);
        Assert.DoesNotMatch("(?m)^  (pull_request|workflow_dispatch|schedule|branches):", text);
        Assert.DoesNotContain("pull_request_target", text);
        Assert.Contains("group: ${{ github.workflow }}-${{ github.ref }}", text);
        Assert.Contains("cancel-in-progress: false", text);
        Assert.Contains("runs-on: windows-latest", text);
        Assert.Contains("timeout-minutes:", text);
    }

    [Fact]
    public void Permissions_actions_and_token_are_bounded()
    {
        var text = Workflow;
        Assert.Matches("(?m)^permissions:\\n  contents: read$", text);
        Assert.Single(Regex.Matches(text, "contents: write"));
        Assert.Matches("(?m)^  release:\\n(?:.*\\n)*?    permissions:\\n      contents: write", text);
        Assert.Contains("gh release create", text);
        var uses = Regex.Matches(text, @"(?m)^\s+uses: (\S+)");
        Assert.NotEmpty(uses);
        foreach (Match use in uses)
            Assert.Matches(@"^actions/(checkout|setup-dotnet|upload-artifact)@[0-9a-f]{40}$", use.Groups[1].Value);
        Assert.Contains("persist-credentials: false", text);
        Assert.Contains("global-json-file: global.json", text);
        foreach (Match secret in Regex.Matches(text, @"secrets\.([A-Za-z_][A-Za-z0-9_]*)"))
            Assert.Equal("GITHUB_TOKEN", secret.Groups[1].Value);
        Assert.Equal(2, Regex.Matches(text, @"secrets\.GITHUB_TOKEN").Count);
    }

    [Fact]
    public void Tag_is_checked_before_build_and_packaging_uses_exact_source()
    {
        var text = Workflow;
        Assert.Contains("src/AIBar.Desktop/AIBar.Desktop.csproj", text);
        Assert.Contains("$project.Project.PropertyGroup.Version", text);
        Assert.Contains("$env:GITHUB_REF_NAME -cne \"v$version\"", text);
        Assert.True(text.IndexOf("$env:GITHUB_REF_NAME -cne", StringComparison.Ordinal) < text.IndexOf("dotnet restore", StringComparison.Ordinal));
        Assert.Contains("dotnet restore AIBar.sln --nologo", text);
        Assert.Contains("dotnet build AIBar.sln --no-restore --configuration Debug --nologo", text);
        Assert.Contains("dotnet test AIBar.sln --no-build --no-restore --configuration Debug", text);
        Assert.Contains("-Release -ExpectedSourceCommit $env:GITHUB_SHA", text);
        Assert.Contains("$env:RUNNER_TEMP", text);
        Assert.Contains("Build-Installer.ps1 -ReleaseDirectory", text);
    }

    [Fact]
    public void Compiler_download_and_installed_version_are_pinned()
    {
        var text = Workflow;
        Assert.Contains("https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe", text);
        Assert.Contains("9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732", text);
        Assert.Contains("Get-FileHash -LiteralPath $installer -Algorithm SHA256", text);
        Assert.Contains("-eq '6.7.3'", text);
        Assert.Contains("/CURRENTUSER", text);
        Assert.Contains("$process.ExitCode -ne 0", text);
        Assert.Contains("-IsccPath $compiler", text);
    }

    [Fact]
    public void Release_is_a_new_draft_with_exact_assets_and_rendered_notes()
    {
        var text = Workflow;
        Assert.Contains("gh release create", text);
        Assert.Contains("--draft", text);
        Assert.Contains("--verify-tag", text);
        Assert.Contains("'--title', \"AIBar $version\"", text);
        Assert.Contains("$version.Contains('-')", text);
        Assert.Contains("--prerelease", text);
        Assert.DoesNotContain("--clobber", text);
        Assert.DoesNotContain("gh release upload", text);
        Assert.Contains("AIBar-$version-win-x64-setup.exe", text);
        Assert.Contains("AIBar-$version-win-x64.zip", text);
        Assert.Contains("release-manifest.json", text);
        Assert.Contains("SHA256SUMS.txt", text);
        var assetBlock = Regex.Match(text, @"(?s)\$assets = @\((.*?)\n          \)").Groups[1].Value;
        Assert.Equal(4, Regex.Matches(assetBlock, "Join-Path").Count);
        Assert.Contains("gh release create $env:GITHUB_REF_NAME @assets @arguments", text);
        Assert.Contains("packaging/release-notes.md", text);
        Assert.Contains(".Replace('{{VERSION}}', $version)", text);
        Assert.Contains(".Replace('{{SOURCE_COMMIT}}', $env:GITHUB_SHA)", text);
        var notes = File.ReadAllText(Path.Combine(Root, "packaging", "release-notes.md"));
        foreach (var required in new[] { "{{VERSION}}", "{{SOURCE_COMMIT}}", "More info", "Run anyway", "Get-FileHash", "SHA256SUMS.txt", "Español" })
            Assert.Contains(required, notes);
    }
}
