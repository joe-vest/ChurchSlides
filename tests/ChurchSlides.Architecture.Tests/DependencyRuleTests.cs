using System.Xml.Linq;

namespace ChurchSlides.Architecture.Tests;

/// <summary>
/// Enforces the assembly dependency rule from docs/ARCHITECTURE.md section 3.
/// Reads the .csproj files directly, so it needs no extra packages.
/// </summary>
public class DependencyRuleTests
{
    // Project -> the only ChurchSlides projects it may reference.
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["Domain"] = [],
        ["Storage"] = ["Domain"],
        ["Scripture"] = ["Domain"],
        ["Engine"] = ["Domain", "Storage", "Scripture"],
        ["Audio"] = ["Domain", "Engine"],
        ["Rendering"] = ["Domain", "Engine"],
        ["Remote"] = ["Domain", "Engine"],
        ["App"] = ["Domain", "Storage", "Scripture", "Engine", "Audio", "Rendering", "Remote"],
    };

    // Projects that must stay free of any Windows/WPF dependency.
    private static readonly string[] UiFree = ["Domain", "Storage", "Scripture", "Engine"];

    private static string SrcDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ChurchSlides.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    private static XDocument Load(string project) =>
        XDocument.Load(Path.Combine(SrcDir(), $"ChurchSlides.{project}", $"ChurchSlides.{project}.csproj"));

    public static TheoryData<string> Projects()
    {
        var data = new TheoryData<string>();
        foreach (var p in Allowed.Keys) data.Add(p);
        return data;
    }

    [Fact]
    public void Every_source_project_is_covered_by_the_rule()
    {
        var onDisk = Directory.GetDirectories(SrcDir())
            .Select(d => Path.GetFileName(d)!.Replace("ChurchSlides.", ""))
            .Order().ToArray();
        Assert.Equal(Allowed.Keys.Order().ToArray(), onDisk);
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Project_references_only_allowed_projects(string project)
    {
        var refs = Load(project).Descendants("ProjectReference")
            .Select(e => ((string)e.Attribute("Include")!).Replace('\\', '/').Split('/').Last())
            .Select(file => Path.GetFileNameWithoutExtension(file).Replace("ChurchSlides.", ""))
            .Order().ToArray();

        var disallowed = refs.Except(Allowed[project]).ToArray();
        Assert.True(disallowed.Length == 0,
            $"ChurchSlides.{project} must not reference: {string.Join(", ", disallowed)}");
    }

    [Theory]
    [InlineData("Domain")]
    [InlineData("Storage")]
    [InlineData("Scripture")]
    [InlineData("Engine")]
    public void Ui_free_projects_have_no_windows_or_framework_dependencies(string project)
    {
        Assert.Contains(project, UiFree);
        var doc = Load(project);

        var tfm = doc.Descendants("TargetFramework").Single().Value;
        Assert.DoesNotContain("windows", tfm, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(doc.Descendants("UseWPF"));
        Assert.Empty(doc.Descendants("UseWindowsForms"));
        Assert.Empty(doc.Descendants("FrameworkReference"));
    }

    [Fact]
    public void Domain_has_no_package_references()
    {
        Assert.Empty(Load("Domain").Descendants("PackageReference"));
    }
}
