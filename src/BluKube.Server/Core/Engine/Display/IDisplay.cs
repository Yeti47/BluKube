namespace BluKube.Server.Core.Engine.Display;

public interface IDisplay : IAsyncDisposable
{
    string DisplayValue { get; }
}
