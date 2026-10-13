using Xunit;

namespace Anchal.Tests.Deployment;

public sealed class NginxTemplateTests
{
    // Break caught: the Anchal deployment template either stops routing its API to the isolated loopback process
    // or broadens its scope into a shared Nginx catch-all route.
    [Fact]
    public void AnchalTemplateRoutesOnlyTheIsolatedApiAndHealthPathsToLoopback()
    {
        var template = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "deploy", "nginx-anchal.conf.template"));

        var anchalApi = template.IndexOf("location /v1/api/anchal/", StringComparison.Ordinal);
        var genericApi = template.IndexOf("location /api/", StringComparison.Ordinal);

        Assert.True(anchalApi >= 0, "The Anchal API location is required.");
        Assert.True(genericApi < 0 || anchalApi < genericApi, "The Anchal API location must precede generic API routes.");
        Assert.Contains("proxy_pass http://127.0.0.1:8090", template, StringComparison.Ordinal);
        Assert.Contains("proxy_set_header Host $host", template, StringComparison.Ordinal);
        Assert.Contains("proxy_set_header X-Forwarded-Host $host", template, StringComparison.Ordinal);
        Assert.Contains("proxy_set_header X-Forwarded-Proto $scheme", template, StringComparison.Ordinal);
        Assert.Contains("location /health/anchal", template, StringComparison.Ordinal);
        Assert.Contains("location /anchal/", template, StringComparison.Ordinal);
        Assert.DoesNotContain("location /api/", template, StringComparison.Ordinal);
        Assert.DoesNotContain("location / {", template, StringComparison.Ordinal);
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
