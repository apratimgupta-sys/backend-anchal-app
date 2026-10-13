using System.Xml.Linq;
using Xunit;

namespace Anchal.Tests.Architecture;

public sealed class StandaloneBoundaryTests
{
    [Fact]
    public void BoundaryFileSelectionIncludesStandaloneSourceFiles()
    {
        var repositoryRoot = FindRepositoryRoot();
        var fixturePath = Path.Combine(repositoryRoot, "src", "Anchal.Api", "BoundaryFixture.cs");
        File.WriteAllText(fixturePath, "using Rmc.Ubpms.Application;");

        try
        {
            Assert.Contains(fixturePath, FindBoundaryFiles(repositoryRoot));
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    [Fact]
    public void ApiProjectHasStandaloneAssemblyNameAndNoApnaRanchiReferences()
    {
        var repositoryRoot = FindRepositoryRoot();
        var apiProjectPath = Path.Combine(repositoryRoot, "src", "Anchal.Api", "Anchal.Api.csproj");
        var apiProject = XDocument.Load(apiProjectPath);

        Assert.Equal(
            "Anchal.Api",
            apiProject.Root?
                .Elements("PropertyGroup")
                .Elements("AssemblyName")
                .Single()
                .Value);

        foreach (var file in FindBoundaryFiles(repositoryRoot))
        {
            Assert.DoesNotContain("Rmc.Ubpms", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> FindBoundaryFiles(string repositoryRoot) =>
        Directory
            .EnumerateFiles(repositoryRoot, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.cs", SearchOption.AllDirectories))
            .Where(file =>
            {
                var pathSegments = Path.GetRelativePath(repositoryRoot, file)
                    .Split(Path.DirectorySeparatorChar);

                return !pathSegments.Contains("bin") && !pathSegments.Contains("obj");
            });

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
