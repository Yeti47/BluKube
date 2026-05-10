namespace BluKube.Server.Core.Engine.Display;

public interface IDisplayFactory
{
    Task<IDisplay> CreateAsync(CancellationToken cancellationToken);
}
