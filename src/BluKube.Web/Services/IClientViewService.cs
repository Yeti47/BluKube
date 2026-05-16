using BluKube.Web.Components.Pages;
using Microsoft.JSInterop;

namespace BluKube.Web.Services;

public interface IClientViewService
{
    ClientView View { get; }
    Task ActivateAsync(DotNetObjectReference<Home>? homeReference);
    Task DeactivateAsync(bool resetSession = true);
    void ClearState();
    void PostKey(string key, bool shift, bool ctrl, bool alt);
}
