using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;
namespace ValheimLootFilter
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    //[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class ValheimLootFilter : BaseUnityPlugin
    {
        public const string PluginGUID = "com.ukie.ValheimLootFilter";
        public const string PluginName = "LootFilter";
        public const string PluginVersion = "0.0.2";

        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private Harmony _harmony;
        private ButtonConfig LootFilterShortcutButton;
        private void Awake()
        {
            // 1. Init the service (loads saved exclusions from config)
            LootFilterService.Init(Config);

            LootFilterShortcutButton = new ButtonConfig
            {
                Name = "ValheimLootFiler_ShortcutButton",
                Key = KeyCode.L
            };
            InputManager.Instance.AddButton(PluginGUID, LootFilterShortcutButton);

            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll(typeof(LootFilterService.Patches));
            _harmony.PatchAll(typeof(InventoryGuiPatch));
            Jotunn.Logger.LogInfo("LootFilter has landed");
        }

        private void Update()
        {
            if(ZInput.instance == null)
                return;

            // This is called every frame, you can use it to check for input or other things that need to be updated regularly
            if (ZInput.GetButtonDown(LootFilterShortcutButton.Name))
            {
                LootFilterUI.TogglePanel();
            }
        }
    }
}

