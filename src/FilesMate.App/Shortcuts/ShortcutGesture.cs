namespace FilesMate.App.Shortcuts;

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Control = 1,
    Menu = 2,
    Shift = 4,
    Windows = 8,
}

public enum ShortcutKey
{
    None = 0,
    Back = 8,
    Enter = 13,
    Shift = 16,
    Control = 17,
    Menu = 18,
    Escape = 27,
    Space = 32,
    Left = 37,
    Up = 38,
    Right = 39,
    Down = 40,
    Delete = 46,
    Number0 = 48,
    Number1 = 49,
    Number2 = 50,
    Number3 = 51,
    Number4 = 52,
    Number5 = 53,
    Number6 = 54,
    Number7 = 55,
    Number8 = 56,
    Number9 = 57,
    A = 65,
    B = 66,
    C = 67,
    D = 68,
    E = 69,
    F = 70,
    G = 71,
    H = 72,
    I = 73,
    J = 74,
    K = 75,
    L = 76,
    M = 77,
    N = 78,
    O = 79,
    P = 80,
    Q = 81,
    R = 82,
    S = 83,
    T = 84,
    U = 85,
    V = 86,
    W = 87,
    X = 88,
    Y = 89,
    Z = 90,
    LeftWindows = 91,
    RightWindows = 92,
    F1 = 112,
    F2 = 113,
    F3 = 114,
    F4 = 115,
    F5 = 116,
    F6 = 117,
    F7 = 118,
    F8 = 119,
    F9 = 120,
    F10 = 121,
    F11 = 122,
    F12 = 123,
}

public readonly record struct ShortcutGesture(ShortcutKey Key, ShortcutModifiers Modifiers)
{
    public bool IsValid => Key is not (
        ShortcutKey.None or
        ShortcutKey.Control or
        ShortcutKey.Menu or
        ShortcutKey.Shift or
        ShortcutKey.LeftWindows or
        ShortcutKey.RightWindows);

    public string DisplayText
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(ShortcutModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(ShortcutModifiers.Menu))
            {
                parts.Add("Alt");
            }

            if (Modifiers.HasFlag(ShortcutModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (Modifiers.HasFlag(ShortcutModifiers.Windows))
            {
                parts.Add("Win");
            }

            parts.Add(FormatKey(Key));
            return string.Join('+', parts);
        }
    }

    private static string FormatKey(ShortcutKey key)
    {
        if (key is >= ShortcutKey.A and <= ShortcutKey.Z)
        {
            return ((char)('A' + (int)key - (int)ShortcutKey.A)).ToString();
        }

        if (key is >= ShortcutKey.Number0 and <= ShortcutKey.Number9)
        {
            return ((char)('0' + (int)key - (int)ShortcutKey.Number0)).ToString();
        }

        return key switch
        {
            ShortcutKey.Delete => "Delete",
            ShortcutKey.Enter => "Enter",
            ShortcutKey.Back => "Backspace",
            ShortcutKey.Space => "Space",
            ShortcutKey.Left => "←",
            ShortcutKey.Right => "→",
            ShortcutKey.Up => "↑",
            ShortcutKey.Down => "↓",
            _ => key.ToString(),
        };
    }
}
