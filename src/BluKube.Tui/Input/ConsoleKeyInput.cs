using System.Threading.Channels;
using BluKube.Tui.Rendering;

namespace BluKube.Tui.Input;

/// <summary>
/// Reads keystrokes from <see cref="Console.ReadKey(bool)"/> on a
/// dedicated thread and surfaces them as an async stream.
/// </summary>
public sealed class ConsoleKeyInput : IKeyInput
{
    public async IAsyncEnumerable<KeyPress> ReadKeysAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var ch = Channel.CreateUnbounded<KeyPress>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var reader = new Thread(() =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!Console.KeyAvailable)
                    {
                        Thread.Sleep(20);
                        continue;
                    }
                    var raw = Console.ReadKey(intercept: true);
                    ch.Writer.TryWrite(Translate(raw));
                }
            }
            catch (InvalidOperationException) { /* redirected console */ }
            finally { ch.Writer.TryComplete(); }
        }) { IsBackground = true, Name = "blukube-input" };
        reader.Start();

        try
        {
            await foreach (var key in ch.Reader.ReadAllAsync(ct))
            {
                yield return key;
            }
        }
        finally
        {
            ch.Writer.TryComplete();
        }
    }

    private static KeyPress Translate(ConsoleKeyInfo k)
    {
        var shift = (k.Modifiers & ConsoleModifiers.Shift) != 0;
        var ctrl = (k.Modifiers & ConsoleModifiers.Control) != 0;
        var key = k.Key switch
        {
            ConsoleKey.Spacebar => Key.Space,
            ConsoleKey.Enter => Key.Enter,
            ConsoleKey.Escape => Key.Escape,
            ConsoleKey.LeftArrow => Key.LeftArrow,
            ConsoleKey.RightArrow => Key.RightArrow,
            ConsoleKey.UpArrow => Key.UpArrow,
            ConsoleKey.DownArrow => Key.DownArrow,
            ConsoleKey.OemPlus => Key.Plus,
            ConsoleKey.Add => Key.Plus,
            ConsoleKey.OemMinus => Key.Minus,
            ConsoleKey.Subtract => Key.Minus,
            ConsoleKey.Q => Key.Q,
            _ when k.KeyChar == '+' => Key.Plus,
            _ when k.KeyChar == '-' => Key.Minus,
            _ when k.KeyChar != '\0' => Key.Char,
            _ => Key.Other,
        };
        return new KeyPress(key, k.KeyChar, shift, ctrl);
    }
}
