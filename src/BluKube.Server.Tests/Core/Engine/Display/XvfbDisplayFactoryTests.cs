using BluKube.Server.Core.Engine.Display;

namespace BluKube.Server.Tests.Core.Engine.Display;

[Trait("Category", "Unit")]
public sealed class XvfbDisplayFactoryTests : IDisposable
{
    private readonly string _tempDir;

    public XvfbDisplayFactoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"blukube-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetUsedDisplayNumbers_EmptyDirectory_ReturnsEmpty()
    {
        var factory = new XvfbDisplayFactory(_tempDir);

        var result = factory.GetUsedDisplayNumbers();

        Assert.Empty(result);
    }

    [Fact]
    public void GetUsedDisplayNumbers_WithFilesInRange_ReturnsSorted()
    {
        File.WriteAllText(Path.Combine(_tempDir, "X100"), string.Empty);
        File.WriteAllText(Path.Combine(_tempDir, "X150"), string.Empty);
        File.WriteAllText(Path.Combine(_tempDir, "X105"), string.Empty);
        var factory = new XvfbDisplayFactory(_tempDir);

        var result = factory.GetUsedDisplayNumbers();

        Assert.Equal([100, 105, 150], result);
    }

    [Fact]
    public void GetUsedDisplayNumbers_WithFilesOutsideRange_Ignores()
    {
        File.WriteAllText(Path.Combine(_tempDir, "X50"), string.Empty);
        File.WriteAllText(Path.Combine(_tempDir, "X200"), string.Empty);
        File.WriteAllText(Path.Combine(_tempDir, "X120"), string.Empty);
        var factory = new XvfbDisplayFactory(_tempDir);

        var result = factory.GetUsedDisplayNumbers();

        Assert.Equal([120], result);
    }

    [Fact]
    public void GetUsedDisplayNumbers_WithNonNumericFiles_Ignores()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Xabc"), string.Empty);
        File.WriteAllText(Path.Combine(_tempDir, "X100"), string.Empty);
        var factory = new XvfbDisplayFactory(_tempDir);

        var result = factory.GetUsedDisplayNumbers();

        Assert.Equal([100], result);
    }

    [Fact]
    public void GetUsedDisplayNumbers_NonExistentDirectory_ReturnsEmpty()
    {
        var nonExistentDir = Path.Combine(_tempDir, "does-not-exist");
        var factory = new XvfbDisplayFactory(nonExistentDir);

        var result = factory.GetUsedDisplayNumbers();

        Assert.Empty(result);
    }

    [Fact]
    public void GetNextAvailableDisplayNumber_NoneUsed_ReturnsMin()
    {
        var factory = new XvfbDisplayFactory(_tempDir);

        var result = factory.GetNextAvailableDisplayNumber();

        Assert.Equal(XvfbDisplayFactory.MinDisplayNumber, result);
    }

    [Fact]
    public void GetNextAvailableDisplayNumber_SomeUsed_ReturnsFirstGap()
    {
        File.WriteAllText(Path.Combine(_tempDir, "X100"), string.Empty);
        var factory = new XvfbDisplayFactory(_tempDir);

        var result = factory.GetNextAvailableDisplayNumber();

        Assert.Equal(101, result);
    }

    [Fact]
    public void GetNextAvailableDisplayNumber_FirstAvailableInRange_ReturnsCorrect()
    {
        File.WriteAllText(Path.Combine(_tempDir, "X100"), string.Empty);
        File.WriteAllText(Path.Combine(_tempDir, "X101"), string.Empty);
        File.WriteAllText(Path.Combine(_tempDir, "X199"), string.Empty);
        var factory = new XvfbDisplayFactory(_tempDir);

        var result = factory.GetNextAvailableDisplayNumber();

        Assert.Equal(102, result);
    }

    [Fact]
    public void GetNextAvailableDisplayNumber_AllUsed_Throws()
    {
        for (
            var i = XvfbDisplayFactory.MinDisplayNumber;
            i <= XvfbDisplayFactory.MaxDisplayNumber;
            i++
        )
        {
            File.WriteAllText(Path.Combine(_tempDir, $"X{i}"), string.Empty);
        }

        var factory = new XvfbDisplayFactory(_tempDir);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            factory.GetNextAvailableDisplayNumber()
        );
        Assert.Contains("Sequence contains no matching element", ex.Message);
    }
}
