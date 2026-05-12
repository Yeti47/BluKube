using BluKube.Server.Core.Engine.Display;

namespace BluKube.Server.Core.Engine.Browser;

public interface IYouTubeBrowserLauncher
{
    Task<IYouTubeBrowser> LaunchAsync(IDisplay display, CancellationToken cancellationToken);
}
