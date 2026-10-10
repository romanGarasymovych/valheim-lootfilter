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

        /// <summary>
        /// Replacement for the <c>item.m_autoPickup</c> check inside Player.AutoPickup.
        /// Keeps the vanilla flag (e.g. items the player just dropped) and adds the filter.
        /// Must be public: it is called from Valheim's own code via the transpiler.
        /// </summary>
        public static bool AllowAutoPickup(ItemDrop item)
            => item.m_autoPickup && !IsExcluded(GetPrefabName(item));

        private static string GetPrefabName(ItemDrop item)
        {
            // ItemDrop.Awake points m_dropPrefab at the ObjectDB prefab, whose name is
            // exactly the key the UI stores — no string work needed per frame.
            var prefab = item.m_itemData?.m_dropPrefab;
            return prefab != null
                ? prefab.name
                : item.gameObject.name.Replace("(Clone)", "").Trim();
        }

        // ── Harmony patch — intercept the auto-pickup request ────────────────────
        [HarmonyLib.HarmonyPatch]
        public static class Patches
        {
            /// <summary>
            /// Patch Player.AutoPickup so it skips excluded items the same way it
            /// skips items with m_autoPickup == false. Only auto-pickup reads that
            /// flag, so manual pickup (E) keeps working. Patching ItemDrop.CanPickup
            /// instead would also block manual pickup, and makes AutoPickup call
            /// RequestOwn on the item every frame.
            /// </summary>
            [HarmonyLib.HarmonyPatch(typeof(Player), nameof(Player.AutoPickup))]
            [HarmonyLib.HarmonyTranspiler]
            private static IEnumerable<HarmonyLib.CodeInstruction> AutoPickup_Transpiler(
                IEnumerable<HarmonyLib.CodeInstruction> instructions)
            {
                var autoPickupField = HarmonyLib.AccessTools.Field(typeof(ItemDrop), nameof(ItemDrop.m_autoPickup));
                var allowMethod = HarmonyLib.AccessTools.Method(typeof(LootFilterService), nameof(AllowAutoPickup));
                bool patched = false;

                foreach (var instruction in instructions)
                {
                    // `ldfld ItemDrop::m_autoPickup` pops the ItemDrop and pushes a bool;
                    // a static call taking the ItemDrop has the same stack shape.
                    if (!patched && HarmonyLib.CodeInstructionExtensions.LoadsField(instruction, autoPickupField))
                    {
                        instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                        instruction.operand = allowMethod;
                        patched = true;
                    }

                    yield return instruction;
                }

                if (!patched)
                    Jotunn.Logger.LogError("[LootFilter] m_autoPickup check not found in Player.AutoPickup — filter is inactive");
            }
        }
    }
}
