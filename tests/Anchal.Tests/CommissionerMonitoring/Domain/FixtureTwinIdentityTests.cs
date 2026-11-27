using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Domain;

/// The two repos hold the golden fixture as identical copies. The frontend's own suite compares them when
/// the backend repo sits beside it (shape-twin.test.ts); this is the same check from this side, so an edit
/// made to this copy and tested here, with the frontend checked out next to this repo, goes red here too.
/// xUnit 2 cannot skip a test from inside it, so when the frontend is NOT found this passes: that is
/// weaker than the frontend's side, which says so loudly, and ANCHAL_REQUIRE_FRONTEND=1 turns the absence
/// into a failure. Neither side binds the copies in a checkout that has only one repo.
public sealed class FixtureTwinIdentityTests
{
    private const string Fixture = "tests/Anchal.Tests/CommissionerMonitoring/Domain/shape-twin.fixture.json";
    private static readonly string[] FrontendNames = ["work", "frontend", "CommissionerDashboard"];

    private static string? RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Fixture))) return dir.FullName;
        return null;
    }

    private static IEnumerable<string> Candidates(string root)
    {
        var env = Environment.GetEnvironmentVariable("ANCHAL_FRONTEND_DIR");
        if (!string.IsNullOrEmpty(env)) yield return Path.GetFullPath(env);
        foreach (var name in FrontendNames) yield return Path.GetFullPath(Path.Combine(root, "..", name));
    }

    [Fact]
    public void ThisCopyIsByteIdenticalToTheFrontendsWhenTheFrontendIsBesideThisRepo()
    {
        var root = RepoRoot();
        Assert.NotNull(root);
        var mine = File.ReadAllText(Path.Combine(root!, Fixture));
        var theirs = Candidates(root!)
            .Select(d => Path.Combine(d, "tests", "domain", "shape-twin.fixture.json"))
            .FirstOrDefault(File.Exists);

        if (theirs is null)
        {
            Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANCHAL_REQUIRE_FRONTEND")),
                "ANCHAL_REQUIRE_FRONTEND is set and the frontend repo was not found beside this one.");
            Console.Error.WriteLine("FixtureTwinIdentityTests: frontend repo not found; the two fixture copies were NOT compared.");
            return;
        }

        Assert.True(mine == File.ReadAllText(theirs), $"{Fixture} differs from {theirs}; the copies must be byte-identical.");
    }
}
