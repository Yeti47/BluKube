using YtCliRadio.Browser;
using YtCliRadio.Configuration;

namespace YtCliRadio.Tests;

public sealed class AppOptionsTests
{
    [Fact]
    public void Parse_WithDefaults_UsesExpectedDefaults()
    {
        var options = AppOptions.Parse([]);
        Assert.Equal("lofi hip hop", options.Query);
        Assert.Equal(8, options.ResultLimit);
        Assert.False(options.DryRun);
    }

    [Fact]
    public void Parse_WithArguments_UsesProvidedValues()
    {
        var options = AppOptions.Parse(["--query", "synthwave", "--limit", "5", "--dry-run"]);
        Assert.Equal("synthwave", options.Query);
        Assert.Equal(5, options.ResultLimit);
        Assert.True(options.DryRun);
    }

    [Fact]
    public void Parse_WithOutOfRangeLimit_Throws()
    {
        Assert.Throws<ArgumentException>(() => AppOptions.Parse(["--limit", "0"]));
    }
}

public sealed class BravePathResolverTests
{
    [Fact]
    public void Resolve_WithExplicitPath_ReturnsExplicitPath()
    {
        const string path = "/tmp/custom-brave";
        Assert.Equal(path, BravePathResolver.Resolve(path));
    }
}
