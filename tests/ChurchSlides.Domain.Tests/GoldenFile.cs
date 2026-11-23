namespace ChurchSlides.Domain.Tests;

/// <summary>
/// Compares output with a hand-reviewed file in Samples/. Set CHURCHSLIDES_UPDATE_GOLDEN=1 to
/// (re)write the files, then review the diff before committing. A mismatch means the saved
/// format changed, which needs a schema version decision (see SchemaVersions).
/// </summary>
internal static class GoldenFile
{
    private static string SamplesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ChurchSlides.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "ChurchSlides.Domain.Tests", "Samples");
    }

    public static string PathFor(string name) => Path.Combine(SamplesDir(), name);

    public static string Read(string name) => File.ReadAllText(PathFor(name)).Replace("\r\n", "\n");

    public static void Check(string name, string actual)
    {
        if (Environment.GetEnvironmentVariable("CHURCHSLIDES_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(SamplesDir());
            File.WriteAllText(PathFor(name), actual); // actual already uses \n and no BOM
            return;
        }

        Assert.True(File.Exists(PathFor(name)), $"Golden file Samples/{name} is missing. Run with CHURCHSLIDES_UPDATE_GOLDEN=1.");
        Assert.Equal(Read(name), actual);
    }
}
