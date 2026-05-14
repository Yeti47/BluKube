using System.Diagnostics;

namespace BluKube.Server.Core.Engine.Display;

public sealed class XvfbDisplay : IDisplay
{
    private readonly Process _process;
    private readonly FileInfo _socketFile;
    private bool _disposed;

    public string DisplayValue { get; }
    public int DisplayNumber { get; }

    public XvfbDisplay(Process process, int displayNumber, FileInfo socketFile)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(displayNumber, nameof(displayNumber));

        _process = process;
        _socketFile = socketFile;

        DisplayNumber = displayNumber;
        DisplayValue = $":{displayNumber}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
            // Best-effort cleanup.
        }

        try
        {
            _socketFile.Delete();
        }
        catch
        {
            // Best-effort cleanup.
        }

        _process.Dispose();
    }
}
