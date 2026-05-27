namespace WebReaper.Cdp;

/// <summary>
/// Maps a Playwright-style key string to the four CDP
/// <c>Input.dispatchKeyEvent</c> fields (ADR-0074). Pure static: no I/O,
/// no state, deterministic output.
/// <para>
/// Key string format (Playwright): single printable characters
/// (<c>"a"</c>, <c>"A"</c>, <c>"1"</c>), named keys (<c>"Enter"</c>,
/// <c>"Tab"</c>, <c>"Escape"</c>, etc.), and modifier-prefixed combos
/// (<c>"Control+A"</c>, <c>"Shift+Tab"</c>, <c>"Meta+C"</c>,
/// <c>"Control+Shift+K"</c>). Multiple modifiers are supported via
/// successive <c>+</c>-separated prefixes.
/// </para>
/// <para>
/// Modifier bitmask per CDP spec: Alt=1, Ctrl=2, Meta=4, Shift=8.
/// </para>
/// </summary>
public static class CdpKeyMapper
{
    // Static lookup for named keys. Printable single characters and modifier
    // combos are handled programmatically; everything else goes here.
    private static readonly Dictionary<string, CdpKey> NamedKeys =
        new(StringComparer.Ordinal)
        {
            // Whitespace / editing
            { "Enter",     new CdpKey("Enter",     "Enter",       13, 0) },
            { "Tab",       new CdpKey("Tab",        "Tab",          9, 0) },
            { "Escape",    new CdpKey("Escape",     "Escape",      27, 0) },
            { "Backspace", new CdpKey("Backspace",  "Backspace",    8, 0) },
            { "Delete",    new CdpKey("Delete",     "Delete",      46, 0) },
            { "Space",     new CdpKey(" ",          "Space",       32, 0) },

            // Navigation
            { "Home",      new CdpKey("Home",       "Home",        36, 0) },
            { "End",       new CdpKey("End",        "End",         35, 0) },
            { "PageUp",    new CdpKey("PageUp",     "PageUp",      33, 0) },
            { "PageDown",  new CdpKey("PageDown",   "PageDown",    34, 0) },
            { "ArrowUp",   new CdpKey("ArrowUp",    "ArrowUp",     38, 0) },
            { "ArrowDown", new CdpKey("ArrowDown",  "ArrowDown",   40, 0) },
            { "ArrowLeft", new CdpKey("ArrowLeft",  "ArrowLeft",   37, 0) },
            { "ArrowRight",new CdpKey("ArrowRight", "ArrowRight",  39, 0) },

            // Function keys
            { "F1",  new CdpKey("F1",  "F1",  112, 0) },
            { "F2",  new CdpKey("F2",  "F2",  113, 0) },
            { "F3",  new CdpKey("F3",  "F3",  114, 0) },
            { "F4",  new CdpKey("F4",  "F4",  115, 0) },
            { "F5",  new CdpKey("F5",  "F5",  116, 0) },
            { "F6",  new CdpKey("F6",  "F6",  117, 0) },
            { "F7",  new CdpKey("F7",  "F7",  118, 0) },
            { "F8",  new CdpKey("F8",  "F8",  119, 0) },
            { "F9",  new CdpKey("F9",  "F9",  120, 0) },
            { "F10", new CdpKey("F10", "F10", 121, 0) },
            { "F11", new CdpKey("F11", "F11", 122, 0) },
            { "F12", new CdpKey("F12", "F12", 123, 0) },
        };

    // Modifier names and their bitmask values (CDP: Alt=1, Ctrl=2, Meta=4, Shift=8).
    private static readonly Dictionary<string, int> ModifierBits =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "Alt",     1 },
            { "Control", 2 },
            { "Ctrl",    2 },
            { "Meta",    4 },
            { "Shift",   8 },
        };

    /// <summary>
    /// Map a Playwright-style <paramref name="keyString"/> to the four CDP
    /// <c>Input.dispatchKeyEvent</c> fields.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="keyString"/> is unknown (not a printable character, a
    /// recognised named key, or a valid modifier-prefixed combo).
    /// </exception>
    public static CdpKey Map(string keyString)
    {
        ArgumentNullException.ThrowIfNull(keyString);

        // Strip modifier prefixes if present ("Control+Shift+K" -> ("K", Ctrl|Shift)).
        var modifiers = 0;
        var remaining = keyString;

        while (true)
        {
            var plus = remaining.IndexOf('+');
            if (plus < 0) break;

            var candidate = remaining[..plus];
            if (!ModifierBits.TryGetValue(candidate, out var bit)) break;

            modifiers |= bit;
            remaining = remaining[(plus + 1)..];
        }

        // After stripping modifier prefixes, resolve the base key.
        var baseKey = MapBase(remaining, keyString);

        // OR in the modifier bits accumulated above, preserving any the base
        // key already carries (e.g. uppercase letters carry Shift=8 on their own).
        return baseKey with { Modifiers = baseKey.Modifiers | modifiers };
    }

    private static CdpKey MapBase(string key, string original)
    {
        // Named keys.
        if (NamedKeys.TryGetValue(key, out var named)) return named;

        // Single character.
        if (key.Length == 1)
        {
            var ch = key[0];

            // Lowercase letter: "a"-"z" -> Key=ch, Code="KeyA"-"KeyZ", VKC=upper-char.
            if (ch >= 'a' && ch <= 'z')
                return new CdpKey(key, "Key" + char.ToUpperInvariant(ch), ch - 32, 0);

            // Uppercase letter: "A"-"Z" -> Key=ch, Code="KeyA"-"KeyZ", VKC=ch, Shift.
            if (ch >= 'A' && ch <= 'Z')
                return new CdpKey(key, "Key" + ch, ch, 8);

            // Digit: "0"-"9" -> Key=ch, Code="Digit0"-"Digit9", VKC=ch.
            if (ch >= '0' && ch <= '9')
                return new CdpKey(key, "Digit" + ch, ch, 0);

            // Other printable ASCII (punctuation, symbols) — emit a best-effort
            // mapping: Key=char, Code=char string, VKC=ASCII code, no modifiers.
            if (ch >= 32 && ch <= 126)
                return new CdpKey(key, key, ch, 0);
        }

        throw new ArgumentException($"Unknown key: '{original}'", nameof(original));
    }
}

/// <summary>
/// The four CDP <c>Input.dispatchKeyEvent</c> fields resolved by
/// <see cref="CdpKeyMapper.Map"/>.
/// </summary>
/// <param name="Key">CDP <c>key</c> field (DOM key value, e.g. <c>"Enter"</c>).</param>
/// <param name="Code">CDP <c>code</c> field (physical key code, e.g. <c>"KeyA"</c>).</param>
/// <param name="WindowsVirtualKeyCode">CDP <c>windowsVirtualKeyCode</c> field.</param>
/// <param name="Modifiers">CDP <c>modifiers</c> bitmask (Alt=1, Ctrl=2, Meta=4, Shift=8).</param>
public readonly record struct CdpKey(
    string Key,
    string Code,
    int WindowsVirtualKeyCode,
    int Modifiers);
