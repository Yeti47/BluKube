using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BluKube.Tui.Rendering;

namespace BluKube.Web.Xterm;

/// <summary>
/// <see cref="IKeyInput"/> implementation that is fed by xterm.js keyboard
/// events forwarded from the browser via JS interop. Call
/// <see cref="Post"/> from the Blazor component's <c>[JSInvokable]</c> method.
/// </summary>
public sealed class XtermKeyInput : IKeyInput
{
    private readonly Channel<KeyPress> _channel = Channel.CreateUnbounded<KeyPress>(
        new UnboundedChannelOptions { SingleWriter = true, AllowSynchronousContinuations = false });

    /// <summary>
    /// Enqueues a key event forwarded from xterm.js's <c>onKey</c> handler.
    /// </summary>
    /// <param name="domKey">
    /// The <c>KeyboardEvent.key</c> value (e.g. "Enter", "ArrowUp", "a").
    /// </param>
    public void Post(string domKey, bool shift, bool ctrl) =>
        _channel.Writer.TryWrite(Map(domKey, shift, ctrl));

    /// <summary>Signals end-of-input; causes <see cref="ReadKeysAsync"/> to complete.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    public async IAsyncEnumerable<KeyPress> ReadKeysAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var kp in _channel.Reader.ReadAllAsync(ct))
            yield return kp;
    }

    private static KeyPress Map(string domKey, bool shift, bool ctrl)
    {
        var (key, ch) = domKey switch
        {
            "Enter"      => (Key.Enter,      '\n'),
            "Backspace"  => (Key.Backspace,   '\b'),
            "Escape"     => (Key.Escape,      '\x1b'),
            "ArrowUp"    => (Key.UpArrow,     '\0'),
            "ArrowDown"  => (Key.DownArrow,   '\0'),
            "ArrowLeft"  => (Key.LeftArrow,   '\0'),
            "ArrowRight" => (Key.RightArrow,  '\0'),
            " "          => (Key.Space,       ' '),
            "q" or "Q"   => (Key.Q,           domKey[0]),
            _            => domKey.Length == 1
                                ? (Key.Char, domKey[0])
                                : (Key.Other, '\0')
        };
        return new KeyPress(key, ch, shift, ctrl);
    }
}
