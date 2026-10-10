using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ValheimLootFilter
{
    /// <summary>
    /// Shortcut checks for our keybinds.
    /// KeyboardShortcut.IsDown() refuses to fire while ANY other key is held,
    /// so "L" would not open the panel while running with W held. Here only the
    /// shortcut's own modifiers must be held; left/right Ctrl, Shift and Alt
    /// count as the same key.
    /// </summary>
    internal static class Hotkeys
    {
        public static bool IsDown(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None)
                return false;

            return UnityInput.Current.GetKeyDown(shortcut.MainKey)
                   && shortcut.Modifiers.All(IsHeld);
        }

        private static bool IsHeld(KeyCode key)
            => UnityInput.Current.GetKey(key) || UnityInput.Current.GetKey(OtherSide(key));

        private static KeyCode OtherSide(KeyCode key) => key switch
        {
            KeyCode.LeftControl => KeyCode.RightControl,
            KeyCode.RightControl => KeyCode.LeftControl,
            KeyCode.LeftShift => KeyCode.RightShift,
            KeyCode.RightShift => KeyCode.LeftShift,
            KeyCode.LeftAlt => KeyCode.RightAlt,
            KeyCode.RightAlt => KeyCode.LeftAlt,
            _ => key
        };
    }
}
