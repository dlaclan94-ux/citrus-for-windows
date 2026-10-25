using Tangerine.Core;

namespace Tangerine.Core.Tests;

public sealed class OutputPathsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tangerine-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Uses_the_source_name_with_the_new_extension()
    {
        var source = Touch("photo.png");

        var output = OutputPaths.Reserve(source, ".jpg");

        Assert.Equal(Path.Combine(_dir, "photo.jpg"), output);
        Assert.True(File.Exists(output));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Adds_a_number_instead_of_overwriting()
    {
        var source = Touch("photo.png");
        Touch("photo.jpg");

        var first = OutputPaths.Reserve(source, ".jpg");
        var second = OutputPaths.Reserve(source, ".jpg");

        Assert.Equal(Path.Combine(_dir, "photo (1).jpg"), first);
        Assert.Equal(Path.Combine(_dir, "photo (2).jpg"), second);
    }

    [Fact]
    public void Two_sources_with_the_same_name_get_different_outputs()
    {
        var mov = Touch("clip.mov");
        var mkv = Touch("clip.mkv");

        var a = OutputPaths.Reserve(mov, ".mp4");
        var b = OutputPaths.Reserve(mkv, ".mp4");

        Assert.NotEqual(a, b);
    }

    string Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        return path;
    }
}
