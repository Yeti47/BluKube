namespace BluKube.Web.Services;

public interface IClientViewService
{
    ClientView View { get; }
    Task ActivateAsync();
    Task DeactivateAsync(bool resetSession = true);
    void ClearState();
}
