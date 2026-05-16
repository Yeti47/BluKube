using Microsoft.JSInterop;

namespace BluKube.Web.Clients;

public sealed class TerminalKeyDispatcher
{
    public event EventHandler<TerminalKeyEventArgs>? KeyReceived;

    [JSInvokable]
    public void OnXtermKey(string key, bool shift, bool ctrl, bool alt)
    {
        KeyReceived?.Invoke(this, new TerminalKeyEventArgs(key, shift, ctrl, alt));
    }
}

public sealed class TerminalKeyEventArgs(string key, bool shift, bool ctrl, bool alt)
    : EventArgs
{
    public string Key { get; } = key;
    public bool Shift { get; } = shift;
    public bool Ctrl { get; } = ctrl;
    public bool Alt { get; } = alt;
}