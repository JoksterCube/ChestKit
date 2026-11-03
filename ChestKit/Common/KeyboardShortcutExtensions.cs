using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace JoksterCube.ChestKit.Common;

public static class KeyboardShortcutExtensions
{
    private static readonly KeyCode[] ModifierKeys =
    [
        KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl,
        KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.AltGr, KeyCode.LeftCommand, KeyCode.RightCommand,
        KeyCode.LeftWindows, KeyCode.RightWindows
    ];

    // KeyboardShortcut.IsDown fails while any other key is held, so walking (WASD) blocks every shortcut. Only
    // extra modifiers block here: Ctrl+H does not fire inside Ctrl+Shift+H, but H fires while moving.
    public static bool IsDownWhileMoving(this KeyboardShortcut shortcut)
    {
        KeyCode mainKey = shortcut.MainKey;
        if (mainKey == KeyCode.None || !Input.GetKeyDown(mainKey)) return false;
        KeyCode[] modifiers = shortcut.Modifiers.ToArray();
        if (!modifiers.All(Input.GetKey)) return false;
        return ModifierKeys.All(key => key == mainKey || modifiers.Contains(key) || !Input.GetKey(key));
    }

    // "Ctrl + H", in the game's names for the keys.
    public static string DisplayName(this KeyboardShortcut shortcut)
    {
        if (shortcut.MainKey == KeyCode.None) return "";
        string text = KeyName(shortcut.MainKey);
        foreach (KeyCode modifier in shortcut.Modifiers.Reverse())
            if (modifier != shortcut.MainKey) text = KeyName(modifier) + " + " + text;
        return text;
    }

    public static string KeyName(KeyCode key)
    {
        string name = ZInput.KeyCodeToDisplayName(key);
        return string.IsNullOrEmpty(name) || name.StartsWith("$") ? key.ToString() : name;
    }
}
