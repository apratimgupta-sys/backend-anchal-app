using System.Diagnostics;
using Xunit;

namespace Anchal.Tests.Deployment;

public sealed class StandaloneVerificationScriptTests
{
    // Break caught: deploy executes a protected dotenv file or corrupts the semicolon-delimited
    // connection string required by Npgsql before handing it to the publish process.
    [Theory]
    [InlineData("ConnectionStrings__CommissionerMonitoring=Host=staging;Password=secret-value\n", true)]
    [InlineData("ConnectionStrings__CommissionerMonitoring=$(touch {MARKER})\n", false)]
    public void DeployUsesNonEvaluatingDotenvParser(string environmentContents, bool succeeds)
    {
        using var fixture = new DeploymentFixture(environmentContents);

        var result = fixture.Run();

        if (succeeds)
        {
            Assert.Equal(0, result.ExitCode);
            Assert.False(File.Exists(fixture.MarkerPath));
            Assert.Contains("Host=staging;Password=secret-value", fixture.ReadCommandLog(), StringComparison.Ordinal);
        }
        else
        {
            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(fixture.MarkerPath));
            Assert.Empty(fixture.ReadCommandLog());
            Assert.Contains("Invalid Anchal environment file line 1.", result.Output, StringComparison.Ordinal);
        }
    }

    // Break caught: a staging check applies a database migration without the explicit operator opt-in,
    // or writes a configured secret to its terminal output.
    [Fact]
    public void VerifierSkipsMigrationsByDefaultAndRedactsConfiguredValues()
    {
        using var fixture = new VerificationFixture();

        var result = fixture.Run();

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("ef database update", fixture.ReadCommandLog(), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Secret, result.Output, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.Output, StringComparison.Ordinal);
    }

    // Break caught: a staging check ignores the explicit migration opt-in and never applies the
    // isolated Commissioner Monitoring migration.
    [Fact]
    public void VerifierAppliesMigrationsOnlyWhenExplicitlyEnabled()
    {
        using var fixture = new VerificationFixture();

        var result = fixture.Run(allowDatabaseMigration: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ef database update", fixture.ReadCommandLog(), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Secret, result.Output, StringComparison.Ordinal);
    }

    // Break caught: a protected environment file can execute a command while the verifier reads it.
    [Theory]
    [InlineData("$(touch {MARKER})")]
    [InlineData("`touch {MARKER}`")]
    public void VerifierRejectsShellSyntaxInEnvironmentValuesWithoutExecutingIt(string maliciousValue)
    {
        using var fixture = new VerificationFixture($"ConnectionStrings__CommissionerMonitoring={maliciousValue}\n");

        var result = fixture.Run();

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(fixture.MarkerPath));
        Assert.Empty(fixture.ReadCommandLog());
        Assert.Contains("Invalid Anchal environment file line 1.", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-an-assignment")]
    [InlineData("INVALID-NAME=value")]
    [InlineData("ConnectionStrings__CommissionerMonitoring='unterminated")]
    public void VerifierRejectsMalformedEnvironmentLinesBeforeStartingCommands(string malformedLine)
    {
        using var fixture = new VerificationFixture(malformedLine + "\n");

        var result = fixture.Run();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(fixture.ReadCommandLog());
        Assert.Contains("Invalid Anchal environment file line 1.", result.Output, StringComparison.Ordinal);
    }

    private sealed class VerificationFixture : IDisposable
    {
        private readonly string _root = FindRepositoryRoot();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "anchal-verify-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        public VerificationFixture(string? environmentContents = null)
        {
            Directory.CreateDirectory(_directory);
            Directory.CreateDirectory(Path.Combine(_directory, "bin"));
            File.WriteAllText(
                Path.Combine(_directory, "anchal.env"),
                (environmentContents ?? $"ConnectionStrings__CommissionerMonitoring='{Secret}'\n").Replace("{MARKER}", MarkerPath, StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(_directory, "command.log"), string.Empty);
            WriteShim("dotnet", "printf '%s\\n' \"$*\" >> \"$ANCHAL_TEST_COMMAND_LOG\"\nprintf '%s\\n' \"$ConnectionStrings__CommissionerMonitoring\"\n");
            WriteShim("python3", $"if [ \"${{1:-}}\" = \"-c\" ] || [ \"${{1:-}}\" = \"-\" ]; then\n    exec '{FindSystemPython()}' \"$@\"\nfi\nprintf '%s\\n' \"$*\" >> \"$ANCHAL_TEST_COMMAND_LOG\"\nprintf '%s\\n' \"$ConnectionStrings__CommissionerMonitoring\"\n");
        }

        public string Secret => "Host=staging;Password=secret-value";

        public string MarkerPath => Path.Combine(_directory, "executed-marker");

        public string ReadCommandLog() => File.ReadAllText(Path.Combine(_directory, "command.log"));

        public (int ExitCode, string Output) Run(bool allowDatabaseMigration = false)
        {
            var start = new ProcessStartInfo("sh")
            {
                WorkingDirectory = _root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add("deploy/verify-standalone.sh");
            start.ArgumentList.Add("--base-url");
            start.ArgumentList.Add("https://anchal.example");
            start.Environment["ANCHAL_ENV_FILE"] = Path.Combine(_directory, "anchal.env");
            start.Environment["ANCHAL_TEST_COMMAND_LOG"] = Path.Combine(_directory, "command.log");
            start.Environment["PATH"] = Path.Combine(_directory, "bin") + Path.PathSeparator + _originalPath;
            if (allowDatabaseMigration)
            {
                start.Environment["ALLOW_DATABASE_MIGRATION"] = "true";
            }

            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }

        private void WriteShim(string name, string body)
        {
            var path = Path.Combine(_directory, "bin", name);
            File.WriteAllText(path, "#!/usr/bin/env sh\nset -eu\n" + body);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        private string FindSystemPython()
        {
            foreach (var directory in _originalPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory, "python3");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("python3 was not found on the original PATH.");
        }
    }

    private sealed class DeploymentFixture : IDisposable
    {
        private readonly string _root = FindRepositoryRoot();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "anchal-deploy-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        public DeploymentFixture(string environmentContents)
        {
            Directory.CreateDirectory(_directory);
            Directory.CreateDirectory(Path.Combine(_directory, "bin"));
            Directory.CreateDirectory(Path.Combine(_directory, "releases"));
            File.WriteAllText(Path.Combine(_directory, "anchal.env"),
                environmentContents.Replace("{MARKER}", MarkerPath, StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(_directory, "command.log"), string.Empty);
            WriteShim("dotnet", "printf '%s\\n' \"$ConnectionStrings__CommissionerMonitoring\" >> \"$ANCHAL_TEST_COMMAND_LOG\"\n");
            WriteShim("pm2", "exit 0\n");
            WriteShim("python3", $"if [ \"${{1:-}}\" = \"deploy/anchal-env.py\" ]; then\n    exec '{FindSystemPython()}' \"$@\"\nfi\nexit 0\n");
        }

        public string MarkerPath => Path.Combine(_directory, "executed-marker");

        public string ReadCommandLog() => File.ReadAllText(Path.Combine(_directory, "command.log"));

        public (int ExitCode, string Output) Run()
        {
            var start = new ProcessStartInfo("sh")
            {
                WorkingDirectory = _root,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add("deploy/deploy-anchal.sh");
            start.Environment["ANCHAL_DEPLOY_DIR"] = _directory;
            start.Environment["ANCHAL_BASE_URL"] = "https://anchal.example";
            start.Environment["ANCHAL_ENV_FILE"] = Path.Combine(_directory, "anchal.env");
            start.Environment["ANCHAL_TEST_COMMAND_LOG"] = Path.Combine(_directory, "command.log");
            start.Environment["PATH"] = Path.Combine(_directory, "bin") + Path.PathSeparator + _originalPath;

            using var process = Process.Start(start)!;
            process.StandardInput.WriteLine("apply-anchal-migrations");
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        private void WriteShim(string name, string body)
        {
            var path = Path.Combine(_directory, "bin", name);
            File.WriteAllText(path, "#!/usr/bin/env sh\nset -eu\n" + body);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        private string FindSystemPython()
        {
            foreach (var directory in _originalPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory, "python3");
                if (File.Exists(candidate)) return candidate;
            }

            throw new FileNotFoundException("python3 was not found on the original PATH.");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
