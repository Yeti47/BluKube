using BluKube.Server.Core.Engine.Display;

namespace BluKube.Server.Tests.Core.Engine.Display;

[Trait("Category", "Integration")]
public sealed class XvfbDisplayFactoryIntegrationTests
{
    [DockerOnlyFactAttribute]
    public async Task CreateAsync_CreatesDisplayAndCleansUp()
    {
        var factory = new XvfbDisplayFactory();

        IDisplay display;
        int displayNumber = -1;

        await using (display = await factory.CreateAsync(CancellationToken.None))
        {
            Assert.IsType<XvfbDisplay>(display);
            var xvfbDisplay = (XvfbDisplay)display;

            displayNumber = xvfbDisplay.DisplayNumber;
            Assert.InRange(
                displayNumber,
                XvfbDisplayFactory.MinDisplayNumber,
                XvfbDisplayFactory.MaxDisplayNumber
            );
            Assert.True(factory.DisplaySocketExists(displayNumber));
        }

        const int timeoutSeconds = 3;

        var socketRemovalTask = Task.Run(async () =>
        {
            while (factory.DisplaySocketExists(displayNumber))
            {
                await Task.Delay(100);
            }
        });

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
        var completedTask = await Task.WhenAny(socketRemovalTask, timeoutTask);

        Assert.True(
            completedTask == socketRemovalTask,
            $"Display socket for :{displayNumber} was not cleaned up within {timeoutSeconds} seconds"
        );
    }

    [DockerOnlyFactAttribute]
    public async Task CreateAsync_MultipleDisplays_GetUniqueNumbers()
    {
        var factory = new XvfbDisplayFactory();
        await using var display1 = await factory.CreateAsync(CancellationToken.None);
        await using var display2 = await factory.CreateAsync(CancellationToken.None);

        Assert.NotEqual(display1.DisplayValue, display2.DisplayValue);
    }

    [DockerOnlyFactAttribute]
    public void IsXvfbAvailable_ReturnsTrue()
    {
        Assert.True(XvfbDisplayFactory.IsXvfbAvailable());
    }
}
