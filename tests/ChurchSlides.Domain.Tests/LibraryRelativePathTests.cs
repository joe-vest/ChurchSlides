using ChurchSlides.Domain;

namespace ChurchSlides.Domain.Tests;

// LIB-003: absolute paths must never be stored.
public class LibraryRelativePathTests
{
    [Theory]
    [InlineData("media/images/a.png", "media/images/a.png")]
    [InlineData(@"media\images\a.png", "media/images/a.png")]
    [InlineData("  media/audio/song.mp3  ", "media/audio/song.mp3")]
    [InlineData("a.png", "a.png")]
    public void Accepts_relative_paths_and_normalizes_separators(string input, string expected)
    {
        var path = LibraryRelativePath.Create(input);
        Assert.Equal(expected, path.Value);
        Assert.Equal(expected, path.ToString());
    }

    [Theory]
    [InlineData(@"C:\x.png")]
    [InlineData("C:/x.png")]
    [InlineData(@"\\server\share\x.png")]
    [InlineData("/abs/x.png")]
    [InlineData(@"..\x.png")]
    [InlineData("media/../../x.png")]
    [InlineData("media//x.png")]
    [InlineData("media/./x.png")]
    [InlineData("media/x?.png")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_absolute_escaping_or_malformed_paths(string? input)
    {
        Assert.False(LibraryRelativePath.TryCreate(input, out _));
        Assert.Throws<ArgumentException>(() => LibraryRelativePath.Create(input));
    }

    [Fact]
    public void Resolves_against_a_library_root()
    {
        var path = LibraryRelativePath.Create("media/images/a.png");
        var root = Path.Combine(Path.GetTempPath(), "lib");
        var full = path.ResolveUnder(root);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "media", "images", "a.png")), full);
    }
}
