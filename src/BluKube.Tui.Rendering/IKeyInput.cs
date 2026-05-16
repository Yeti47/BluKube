namespace BluKube.Tui.Rendering;

/// <summary>
/// Terminal-agnostic key abstraction. The host (TUI binary or
/// xterm.js bridge) implements this however it can read keystrokes.
/// </summary>
public interface IKeyInput
{
    /// <summary>
    /// Yields keys until <paramref name="ct"/> is cancelled. Implementations
    /// must be cancellation-friendly and must not block when no key is
    /// available — typically via async polling or an internal channel.
    /// </summary>
    IAsyncEnumerable<KeyPress> ReadKeysAsync(CancellationToken ct);
}

public readonly record struct KeyPress(Key Key, char Character, bool Shift, bool Ctrl, bool Alt)
{
    public bool TryGetInputCharacter(out char character)
    {
        if (Key is Key.Char or Key.Q && !char.IsControl(Character) && Character != '\0')
        {
            character = Character;
            return true;
        }

        character = default;
        return false;
    }

    public bool IsCharacter(char character) =>
        !Ctrl && !Alt && char.ToUpperInvariant(Character) == char.ToUpperInvariant(character);

    public bool IsAltCharacter(char character) =>
        Alt && !Ctrl && char.ToUpperInvariant(Character) == char.ToUpperInvariant(character);
}

public enum Key
{
    Other,
    Char,
    Space,
    Enter,
    Backspace,
    Escape,
    LeftArrow,
    RightArrow,
    UpArrow,
    DownArrow,
    Q,
}
