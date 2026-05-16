namespace BluKube.Server.Core.Engine.Browser;

public interface IBraveProfileProvisioner
{
    Task<BraveProfileLease> CreateAsync(CancellationToken cancellationToken);
}
