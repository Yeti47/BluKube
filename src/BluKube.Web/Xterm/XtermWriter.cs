using System.Text;
using System.Threading.Channels;

namespace BluKube.Web.Xterm;

/// <summary>
/// A <see cref="TextWriter"/> that enqueues every write into an unbounded
/// channel. The Blazor component drains the channel and forwards each chunk
/// to xterm.js via JS interop.
/// </summary>
public sealed class XtermWriter : TextWriter
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false }
    );

    public ChannelReader<string> Output => _channel.Reader;

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(string? value)
    {
        if (value is not null)
            _channel.Writer.TryWrite(value);
    }

    public override void Write(char value) => _channel.Writer.TryWrite(value.ToString());

    public override void WriteLine(string? value) =>
        _channel.Writer.TryWrite((value ?? string.Empty) + "\r\n");

    public override void WriteLine() => _channel.Writer.TryWrite("\r\n");

    public override void Flush() { } // unbuffered — every write goes straight to the channel

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _channel.Writer.TryComplete();
        base.Dispose(disposing);
    }
}
