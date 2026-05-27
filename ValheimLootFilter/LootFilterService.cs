using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ValheimLootFilter
{
    using System.Collections.Generic;
    using System.Linq;
    using BepInEx;
    using BepInEx.Configuration;
    using Jotunn.Managers;

    /// <summary>
    /// Manages the excluded-items set and the auto-pickup patch.
    /// Wire up the Harmony patch in your Plugin.Awake() like:
    ///   harmony.PatchAll(typeof(LootFilterService.Patches));
    /// </summary>
    public static class LootFilterService
    {
        // ── Persistence ───────────────────────────────────────────────────────────
        // Stored as a comma-separated string in BepInEx config so it survives restarts.
        private static ConfigEntry<string> _cfgExcluded;

        private static readonly HashSet<string> ExcludedItems = new();

        // ── Call this from your Plugin.Awake() ───────────────────────────────────
        public static void Init(ConfigFile config)
        {
            _cfgExcluded = config.Bind(
                section: "LootFilter",
                key: "ExcludedItems",
                defaultValue: "",
                description: "Comma-separated list of prefab names excluded from auto-pickup");

            // Load saved exclusions
            foreach (var name in _cfgExcluded.Value
                         .Split(',')
                         .Select(s => s.Trim())
                         .Where(s => s.Length > 0))
            {
                ExcludedItems.Add(name);
            }
        }

        // ── Query / mutate ────────────────────────────────────────────────────────

        public static bool IsExcluded(string prefabName)
            => ExcludedItems.Contains(prefabName);

        /// <summary>Flip the exclusion state. Returns the new state (true = excluded).</summary>
        public static bool ToggleExcluded(string prefabName)
        {
            bool nowExcluded;
            if (ExcludedItems.Contains(prefabName))
            {
                ExcludedItems.Remove(prefabName);
                nowExcluded = false;
            }
            else
            {
                ExcludedItems.Add(prefabName);
                nowExcluded = true;
            }

            Save();
            return nowExcluded;
        }

        private static void Save()
        {
            if (_cfgExcluded != null)
                _cfgExcluded.Value = string.Join(",", ExcludedItems);
        }

        // ── Item discovery ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns every registered ItemDrop in the game.
        /// Jötunn's ObjectDB wrapper makes this straightforward.
        /// </summary>
        public static List<ItemDrop> GetAllItems()
        {
            return ObjectDB.instance == null
                ? new List<ItemDrop>()
                : ObjectDB.instance.m_items
                    .Select(go => go.GetComponent<ItemDrop>())
                    .Where(id => id != null)
                    .ToList();
        }

        // ── Harmony patch — intercept the auto-pickup request ────────────────────
        [HarmonyLib.HarmonyPatch]
        public static class Patches
        {
            /// <summary>
            /// Patch Player.AutoPickup (called every frame when near an item).
            /// If the item's prefab name is in the excluded set, skip it.
            /// </summary>
            [HarmonyLib.HarmonyPatch(typeof(Player), nameof(Player.AutoPickup))]
            [HarmonyLib.HarmonyPrefix]
            private static bool AutoPickup_Prefix(Player __instance)
            {
                // We let the method run; we block individual items inside the
                // original method via the ItemDrop.CanPickup patch below.
                return true;
            }

            /// <summary>
            /// Patch ItemDrop.CanPickup so excluded items always return false.
            /// This is the cleanest intercept point — it's checked by the base
            /// auto-pickup loop before the item is touched.
            /// </summary>
            [HarmonyLib.HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.CanPickup))]
            [HarmonyLib.HarmonyPostfix]
            private static void CanPickup_Postfix(ItemDrop __instance, ref bool __result)
            {
                if (!__result) return;   // already blocked for another reason

                string prefabName = __instance.gameObject.name
                                             .Replace("(Clone)", "")
                                             .Trim();

                if (IsExcluded(prefabName))
                    __result = false;
            }
        }
    }
}
