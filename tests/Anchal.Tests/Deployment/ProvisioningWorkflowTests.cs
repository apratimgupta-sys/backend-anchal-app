using Anchal.Api.Provisioning;
using Xunit;

namespace Anchal.Tests.Deployment;

public sealed class ProvisioningWorkflowTests
{
    [Fact]
    public void CredentialsFileRequiresEachApprovedAccountExactlyOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), "anchal-provision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "credentials.env");
        try
        {
            File.WriteAllText(path, "anchal1=SecureProvisioningPass12!\n");

            var error = Assert.Throws<InvalidOperationException>(() => AnchalProvisioningCredentials.Read(path));

            Assert.Contains("seven approved monitoring accounts", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProvisioningScriptAppliesMigrationsBeforeInvokingProvisionCommand()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "deploy", "provision-anchal.sh"));

        var migration = script.IndexOf("deploy/apply-migrations.sh", StringComparison.Ordinal);
        var provision = script.IndexOf(" provision --credentials-file ", StringComparison.Ordinal);

        Assert.True(migration >= 0);
        Assert.True(provision > migration);
        Assert.Contains("anchal-env.py", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeScriptChecksOnlyThePublicAnchalHealthEndpointAndIdentity()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "deploy", "anchal-smoke.py"));

        Assert.Contains("/health/anchal", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/health/commissioner-monitoring", script, StringComparison.Ordinal);
        Assert.Contains("payload.get(\"service\") != \"anchal\"", script, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
