namespace BluKube.Web.Clients;

public interface IClientView
{
    ClientView View { get; }
    Task ActivateAsync();
    Task DeactivateAsync(bool resetSession = true);
    void ClearState();
}
