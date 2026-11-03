using System.Xml.Linq;

namespace gView.Pipe.Core.Tests;

/// <summary>Keeps THIRD-PARTY-NOTICES.md from going stale: every package of the solution has to be named in it.</summary>
public class ThirdPartyNoticesTests
{
    [Fact]
    public void Every_package_is_listed_in_the_notices()
    {
        var root = FindRepositoryRoot();
        var notices = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"));
        var packages = XDocument.Load(Path.Combine(root, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Select(p => (string)p.Attribute("Include")!)
            .ToList();

        // the notices name the Microsoft.Extensions.* packages as a group
        var missing = packages
            .Where(id => !notices.Contains(id, StringComparison.OrdinalIgnoreCase)
                && !(id.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) && notices.Contains(@"Microsoft.Extensions.\*")))
            .ToList();

        Assert.True(missing.Count == 0, "Add to THIRD-PARTY-NOTICES.md: " + string.Join(", ", missing));
    }

    static private string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "gView.Pipe.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("gView.Pipe.slnx not found above " + AppContext.BaseDirectory);
    }
}
