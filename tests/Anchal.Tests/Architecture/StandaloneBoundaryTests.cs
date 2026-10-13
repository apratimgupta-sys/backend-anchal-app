using System.Xml.Linq;
using Xunit;

namespace Anchal.Tests.Architecture;

public sealed class StandaloneBoundaryTests
{
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

        var solutionAndProjectFiles = Directory
            .EnumerateFiles(repositoryRoot, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories));

        foreach (var file in solutionAndProjectFiles)
        {
            Assert.DoesNotContain("Rmc.Ubpms", File.ReadAllText(file), StringComparison.Ordinal);
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
