using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace FilesMate.App.Tests.VisualRegression;

public sealed class VisualCaptureContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string CaptureScript = Path.Combine(RepoRoot, "scripts", "capture-ui.ps1");

    [Fact]
    public void Capture_script_declares_the_complete_scene_contract_without_pre_body_business_validation()
    {
        Assert.True(File.Exists(CaptureScript), $"Missing visual capture script: {CaptureScript}");

        var script = File.ReadAllText(CaptureScript);
        Assert.Contains("#Requires -Version 7", script, StringComparison.OrdinalIgnoreCase);

        var outputDeclaration = GetParameterDeclaration(script, "OutputPath");
        Assert.Contains("Parameter(Mandatory", outputDeclaration, StringComparison.OrdinalIgnoreCase);

        foreach (var parameter in new[] { "WindowTitle", "Width", "Height", "Theme" })
        {
            var declaration = GetParameterDeclaration(script, parameter);
            Assert.DoesNotContain("Parameter(Mandatory", declaration, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[Validate", declaration, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[AllowNull()]", declaration, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[AllowEmptyString()]", declaration, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Output_path_has_no_default_that_can_write_to_the_repository_root()
    {
        Assert.True(File.Exists(CaptureScript), $"Missing visual capture script: {CaptureScript}");

        var script = File.ReadAllText(CaptureScript);
        Assert.DoesNotMatch(
            new Regex(@"\[string\]\s*\$OutputPath\s*=", RegexOptions.IgnoreCase),
            script);
        Assert.DoesNotContain("$OutputPath = $repoRoot", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$OutputPath = $PSScriptRoot", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capture_is_window_only_and_treats_native_capture_failure_as_fatal()
    {
        Assert.True(File.Exists(CaptureScript), $"Missing visual capture script: {CaptureScript}");

        var script = File.ReadAllText(CaptureScript);
        Assert.Contains("SetWindowPos", script, StringComparison.Ordinal);
        Assert.Contains("GetWindowRect", script, StringComparison.Ordinal);
        Assert.Contains("PrintWindow", script, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyFromScreen", script, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"if\s*\(\s*-not\s+[^\r\n]*PrintWindow", RegexOptions.IgnoreCase),
                script);
    }

    [Fact]
    public void Window_resolution_enumerates_all_top_level_windows_and_requires_one_qualified_match()
    {
        Assert.True(File.Exists(CaptureScript), $"Missing visual capture script: {CaptureScript}");

        var script = File.ReadAllText(CaptureScript);
        Assert.Contains("EnumWindows", script, StringComparison.Ordinal);
        Assert.DoesNotContain("FindWindowW", script, StringComparison.Ordinal);
        Assert.Contains("Resolve-UniqueTargetWindow", script, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"\$qualifiedMatches\.Count\s*-gt\s*1", RegexOptions.IgnoreCase),
            script);
        Assert.Contains("Multiple visible FilesMate Release windows", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_resolution_authenticates_the_known_release_executable()
    {
        Assert.True(File.Exists(CaptureScript), $"Missing visual capture script: {CaptureScript}");

        var script = File.ReadAllText(CaptureScript);
        Assert.Contains("Test-FilesMateReleaseProcessPath", script, StringComparison.Ordinal);
        Assert.Contains("FilesMate.App.exe", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FilesMate.App", script, StringComparison.Ordinal);
        Assert.Contains("GetRelativePath", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(@"\.Path\s+-notmatch[^\r\n]*Release", RegexOptions.IgnoreCase),
            script);
        Assert.DoesNotMatch(
            new Regex(@"StartsWith\([^\r\n]*StringComparison\]::Ordinal\)", RegexOptions.IgnoreCase),
            script);
        Assert.True(
            Regex.Matches(script, @"StartsWith\([^\r\n]*StringComparison\]::OrdinalIgnoreCase\)").Count >= 3);
    }

    [Fact]
    public void Capture_outputs_are_committed_as_a_pair_and_cleaned_on_every_failure()
    {
        Assert.True(File.Exists(CaptureScript), $"Missing visual capture script: {CaptureScript}");

        var script = File.ReadAllText(CaptureScript);
        Assert.DoesNotContain("[ValidateRange(", script, StringComparison.Ordinal);
        Assert.Contains("Remove-CaptureFilesBestEffort", script, StringComparison.Ordinal);
        Assert.Contains("$temporaryMetadata", script, StringComparison.Ordinal);

        var lockAcquisition = script.IndexOf("Enter-CaptureOutputLock -ResolvedOutput", StringComparison.Ordinal);
        var initialCleanup = script.IndexOf("Remove-CaptureFilesBestEffort -Paths", StringComparison.Ordinal);
        var dimensionValidation = script.IndexOf("Width must be between", StringComparison.Ordinal);
        var windowResolution = script.IndexOf("Resolve-UniqueTargetWindow -WindowTitle", StringComparison.Ordinal);
        Assert.True(lockAcquisition >= 0 && lockAcquisition < initialCleanup);
        Assert.True(initialCleanup < dimensionValidation);
        Assert.True(initialCleanup < windowResolution);

        var metadataCommit = script.IndexOf("[IO.File]::Move($temporaryMetadata", StringComparison.Ordinal);
        var pngCommit = script.IndexOf("[IO.File]::Move($temporaryOutput", StringComparison.Ordinal);
        Assert.True(metadataCommit >= 0 && metadataCommit < pngCommit);
        Assert.Contains("Cleanup/finalization failures", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "900")]
    [InlineData("0", "900")]
    [InlineData("-1", "900")]
    [InlineData("not-a-number", "900")]
    [InlineData("7681", "900")]
    [InlineData("999999999999999", "900")]
    [InlineData("1440", "")]
    [InlineData("1440", "0")]
    [InlineData("1440", "-1")]
    [InlineData("1440", "not-a-number")]
    [InlineData("1440", "4321")]
    [InlineData("1440", "999999999999999")]
    public async Task Invalid_dimensions_remove_stale_final_and_temporary_artifacts(string width, string height)
    {
        await AssertInvalidRequestCleansArtifacts(
            windowTitle: "FilesMate-Invalid-Dimension-Contract",
            width,
            height,
            theme: "Dark",
            mixedCaseTemporaryNames: false);
    }

    [Fact]
    public void Empty_business_values_reach_body_validation_instead_of_parameter_binding()
    {
        var script = File.ReadAllText(CaptureScript);
        foreach (var parameter in new[] { "WindowTitle", "Width", "Height", "Theme" })
        {
            var declaration = GetParameterDeclaration(script, parameter);
            Assert.DoesNotContain("[Validate", declaration, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[AllowEmptyString()]", declaration, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("", "Dark")]
    [InlineData("FilesMate", "")]
    [InlineData("FilesMate", "Sepia")]
    public async Task Invalid_title_or_theme_cleans_case_insensitive_stale_artifacts(string windowTitle, string theme)
    {
        await AssertInvalidRequestCleansArtifacts(
            windowTitle,
            width: "1440",
            height: "900",
            theme,
            mixedCaseTemporaryNames: true);
    }

    [Fact]
    public async Task Lock_and_dpi_contract_is_present_in_the_powershell_ast()
    {
        var quotedScript = QuotePowerShell(CaptureScript);
        var command = $$"""
            $tokens = $null
            $errors = $null
            $ast = [Management.Automation.Language.Parser]::ParseFile({{quotedScript}}, [ref]$tokens, [ref]$errors)
            if ($errors.Count -ne 0) { throw ($errors -join [Environment]::NewLine) }
            $functions = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $true))
            foreach ($required in @('Get-CaptureMutexName', 'Enter-CaptureOutputLock', 'Exit-CaptureOutputLock', 'Enter-PerMonitorV2DpiContext', 'Exit-PerMonitorV2DpiContext')) {
                if ($required -notin $functions.Name) { throw "Missing function: $required" }
            }
            $invoke = $functions | Where-Object Name -eq 'Invoke-WindowCapture'
            $body = $invoke.Body.Extent.Text
            $lock = $body.IndexOf('Enter-CaptureOutputLock', [StringComparison]::Ordinal)
            $cleanup = $body.IndexOf('Remove-CaptureFilesBestEffort', [StringComparison]::Ordinal)
            $dpi = $body.IndexOf('Enter-PerMonitorV2DpiContext', [StringComparison]::Ordinal)
            $enumerate = $body.IndexOf('Resolve-UniqueTargetWindow', [StringComparison]::Ordinal)
            $print = $body.IndexOf('PrintWindow', [StringComparison]::Ordinal)
            if ($lock -lt 0 -or $cleanup -le $lock) { throw 'Cleanup is not structurally after lock acquisition.' }
            if ($dpi -lt 0 -or $enumerate -le $dpi -or $print -le $enumerate) { throw 'DPI context does not cover enumeration through PrintWindow.' }
            if ($body -notmatch 'finally[\s\S]*Exit-PerMonitorV2DpiContext[\s\S]*Exit-CaptureOutputLock') { throw 'DPI and mutex restoration are not in finally.' }
            'AST-CONTRACT-OK'
            """;

        var result = await RunPwshCommandAsync(command, TimeSpan.FromSeconds(15));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("AST-CONTRACT-OK", result.StandardOutput, StringComparison.Ordinal);

        var script = File.ReadAllText(CaptureScript);
        Assert.Contains("SHA256", script, StringComparison.Ordinal);
        Assert.Contains("Local\\FilesMate", script, StringComparison.Ordinal);
        Assert.Contains("WaitOne", script, StringComparison.Ordinal);
        Assert.Contains("SetThreadDpiAwarenessContext", script, StringComparison.Ordinal);
        Assert.Contains("DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2", script, StringComparison.Ordinal);
        Assert.Contains("physicalClientWidth", script, StringComparison.Ordinal);
        Assert.Contains("physicalClientHeight", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_requests_for_the_same_output_wait_before_cleanup()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "FilesMate.VisualCaptureConcurrency", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        Process? holder = null;
        Process? contender = null;
        try
        {
            var output = Path.Combine(testRoot, "capture.png");
            var metadata = $"{output}.capture.json";
            var transactionId = Guid.NewGuid().ToString("N");
            var temporaryPng = Path.Combine(testRoot, $".capture.png.{transactionId}.tmp.png");
            var temporaryMetadata = Path.Combine(testRoot, $".capture.png.{transactionId}.tmp.capture.json");
            var seeded = new[] { output, metadata, temporaryPng, temporaryMetadata };
            foreach (var path in seeded)
            {
                await File.WriteAllTextAsync(path, "active-owner");
            }

            var quotedScript = QuotePowerShell(CaptureScript);
            var quotedOutput = QuotePowerShell(output);
            var holderCommand = $$"""
                . {{quotedScript}} -OutputPath {{quotedOutput}}
                $mutex = Enter-CaptureOutputLock -ResolvedOutput ([IO.Path]::GetFullPath({{quotedOutput}})) -TimeoutMilliseconds 5000
                try {
                    [Console]::Out.WriteLine('LOCKED')
                    [Console]::Out.Flush()
                    Start-Sleep -Milliseconds 1500
                }
                finally {
                    Exit-CaptureOutputLock -Mutex $mutex
                }
                """;
            holder = StartPwshCommand(holderCommand);
            var holderErrorTask = holder.StandardError.ReadToEndAsync();
            var ready = await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("LOCKED", ready);

            contender = StartCaptureProcess(output, "FilesMate", "0", "900", "Dark");
            var contenderOutputTask = contender.StandardOutput.ReadToEndAsync();
            var contenderErrorTask = contender.StandardError.ReadToEndAsync();
            await Task.Delay(300);
            Assert.False(contender.HasExited);
            Assert.All(seeded, path => Assert.True(File.Exists(path), $"A waiting capture deleted {path}."));

            using var holderTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var contenderTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await WaitForExitOrKillAsync(holder, holderTimeout.Token);
            await WaitForExitOrKillAsync(contender, contenderTimeout.Token);
            var holderError = await holderErrorTask;
            var contenderOutput = await contenderOutputTask;
            var contenderError = await contenderErrorTask;
            Assert.True(holder.ExitCode == 0, holderError);
            Assert.True(contender.ExitCode != 0, $"stdout: {contenderOutput} stderr: {contenderError}");
            Assert.Empty(Directory.EnumerateFiles(testRoot));
        }
        finally
        {
            KillProcessTree(holder);
            KillProcessTree(contender);
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Cleanup_attempts_every_artifact_when_one_file_is_locked()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "FilesMate.VisualCaptureCleanup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        var output = Path.Combine(testRoot, "capture.png");
        var metadata = $"{output}.capture.json";
        var transactionId = Guid.NewGuid().ToString("N");
        var temporaryPng = Path.Combine(testRoot, $".capture.png.{transactionId}.tmp.png");
        var temporaryMetadata = Path.Combine(testRoot, $".capture.png.{transactionId}.tmp.capture.json");
        foreach (var path in new[] { output, metadata, temporaryPng, temporaryMetadata })
        {
            await File.WriteAllTextAsync(path, "stale");
        }

        try
        {
            await using (var lockedOutput = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            using (var process = StartCaptureProcess(output, "FilesMate", "0", "900", "Dark"))
            {
                var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                var standardErrorTask = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await WaitForExitOrKillAsync(process, timeout.Token);
                var standardOutput = await standardOutputTask;
                var standardError = await standardErrorTask;
                Assert.True(process.ExitCode != 0, standardOutput);
                Assert.Contains("cleanup", standardError, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(output));
                Assert.False(File.Exists(metadata));
                Assert.False(File.Exists(temporaryPng));
                Assert.False(File.Exists(temporaryMetadata));
            }
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static async Task AssertInvalidRequestCleansArtifacts(
        string windowTitle,
        string width,
        string height,
        string theme,
        bool mixedCaseTemporaryNames)
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "FilesMate.VisualCaptureContract", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            var output = Path.Combine(testRoot, "capture.png");
            var metadata = $"{output}.capture.json";
            var transactionId = Guid.NewGuid().ToString("N");
            var staleArtifactName = mixedCaseTemporaryNames ? "Capture.PNG" : "capture.png";
            var temporaryPng = Path.Combine(testRoot, $".{staleArtifactName}.{transactionId}.tmp.png");
            var temporaryMetadata = Path.Combine(testRoot, $".{staleArtifactName.ToUpperInvariant()}.{transactionId}.tmp.capture.json");
            foreach (var path in new[] { output, metadata, temporaryPng, temporaryMetadata })
            {
                await File.WriteAllTextAsync(path, "stale");
            }

            var startInfo = new ProcessStartInfo("pwsh")
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (var argument in new[]
                     {
                         "-NoProfile",
                         CaptureScript,
                         "-WindowTitle", windowTitle,
                         "-Width", width,
                         "-Height", height,
                         "-Theme", theme,
                         "-OutputPath", output,
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start pwsh.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await WaitForExitOrKillAsync(process, timeout.Token);
            var standardOutput = await outputTask;
            var standardError = await errorTask;

            Assert.True(
                process.ExitCode != 0,
                $"Invalid capture request unexpectedly succeeded. stdout: {standardOutput} stderr: {standardError}");
            Assert.Empty(Directory.EnumerateFiles(testRoot));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static Process StartCaptureProcess(
        string output,
        string windowTitle,
        string width,
        string height,
        string theme)
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile",
                     CaptureScript,
                     "-WindowTitle", windowTitle,
                     "-Width", width,
                     "-Height", height,
                     "-Theme", theme,
                     "-OutputPath", output,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start pwsh.");
    }

    private static Process StartPwshCommand(string command)
    {
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var startInfo = new ProcessStartInfo("pwsh")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encodedCommand);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start pwsh.");
    }

    private static async Task<ProcessResult> RunPwshCommandAsync(string command, TimeSpan timeout)
    {
        using var process = StartPwshCommand(command);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        await WaitForExitOrKillAsync(process, cancellation.Token);
        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static async Task WaitForExitOrKillAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync();
            throw new TimeoutException($"Process {process.Id} exceeded its test timeout and was terminated.", exception);
        }
    }

    private static void KillProcessTree(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill.
        }
    }

    private static string QuotePowerShell(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private static string GetParameterDeclaration(string script, string parameter)
    {
        var match = Regex.Match(
            script,
            $@"(?<declaration>(?:\s*\[[^\]]+\])+\s*\${Regex.Escape(parameter)}\b)",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"Could not find declaration for -{parameter}.");
        return match.Groups["declaration"].Value;
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FilesMate.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "scripts")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root from {AppContext.BaseDirectory}.");
    }
}
