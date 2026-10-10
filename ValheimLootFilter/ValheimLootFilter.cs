using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
namespace ValheimLootFilter
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    //[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class ValheimLootFilter : BaseUnityPlugin
    {
        public const string PluginGUID = "com.ukie.ValheimLootFilter";
        public const string PluginName = "LootFilter";
        public const string PluginVersion = "1.0.0";

        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private Harmony _harmony;
        private ConfigEntry<KeyboardShortcut> _openPanelKey;
        private ConfigEntry<KeyboardShortcut> _toggleFilterKey;

        private void Awake()
        {
            // 1. Init the service (loads saved exclusions from config)
            LootFilterService.Init(Config);

            // Keybinds live in the BepInEx config so Configuration Manager can rebind them
            _openPanelKey = Config.Bind(
                section: "Keybinds",
                key: "Open panel",
                defaultValue: new KeyboardShortcut(KeyCode.L),
                description: "Open or close the loot filter panel");
            _toggleFilterKey = Config.Bind(
                section: "Keybinds",
                key: "Toggle filter",
                defaultValue: new KeyboardShortcut(KeyCode.L, KeyCode.LeftControl),
                description: "Turn the loot filter on or off");

            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll(typeof(LootFilterService.Patches));
            _harmony.PatchAll(typeof(InventoryGuiPatch));
            _harmony.PatchAll(typeof(LootFilterUI.Patches));
            Jotunn.Logger.LogInfo("LootFilter has landed");
        }

        private void Update()
        {
            if(ZInput.instance == null)
                return;

            // This is called every frame, you can use it to check for input or other things that need to be updated regularly
            LootFilterUI.HandleEscape();

            if (!CanUseHotkeys())
                return;

            var toggleKey = _toggleFilterKey.Value;
            var openKey = _openPanelKey.Value;
            bool toggleHit = Hotkeys.IsDown(toggleKey);
            bool openHit = Hotkeys.IsDown(openKey);

            // Ctrl+L also contains L: when both match, the one with more
            // modifiers is the one the player meant
            if (toggleHit && openHit)
            {
                if (toggleKey.Modifiers.Count() >= openKey.Modifiers.Count())
                    openHit = false;
                else
                    toggleHit = false;
            }

            if (toggleHit)
                ToggleFilter();
            if (openHit)
                LootFilterUI.TogglePanel();
        }

        // Same rules vanilla uses for player hotkeys: not while typing in chat,
        // in menus, in the inventory, or in our own text fields.
        private static bool CanUseHotkeys()
            => !LootFilterUI.IsTyping
               && Player.m_localPlayer != null
               && Player.m_localPlayer.TakeInput();

        private static void ToggleFilter()
        {
            bool on = LootFilterService.ToggleEnabled();
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, on ? "Loot filter on" : "Loot filter off");
        }
    }
}

