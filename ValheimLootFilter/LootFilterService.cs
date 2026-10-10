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
        private static ConfigEntry<bool> _cfgEnabled;
        private static ConfigEntry<string> _cfgExcludedCategories;
        private static ConfigEntry<string> _cfgQuantityLimits;

        /// <summary>Raised when the filter is turned on/off (hotkey, panel or config manager).</summary>
        public static event System.Action<bool> EnabledChanged;

        private static readonly HashSet<string> ExcludedItems = new();
        private static readonly HashSet<ItemDrop.ItemData.ItemType> ExcludedCategories = new();

        // prefab name → keep auto-picking up until the inventory holds this many
        private static readonly Dictionary<string, int> QuantityLimits = new();

        // ── Call this from your Plugin.Awake() ───────────────────────────────────
        public static void Init(ConfigFile config)
        {
            _cfgEnabled = config.Bind(
                section: "General",
                key: "Enabled",
                defaultValue: true,
                description: "Turn the loot filter on or off. When off, everything is auto-picked up as usual.");
            _cfgEnabled.SettingChanged += (_, _) => EnabledChanged?.Invoke(_cfgEnabled.Value);

            _cfgExcluded = config.Bind(
                section: "LootFilter",
                key: "ExcludedItems",
                defaultValue: "",
                description: "Comma-separated list of prefab names excluded from auto-pickup");

            _cfgExcludedCategories = config.Bind(
                section: "LootFilter",
                key: "ExcludedCategories",
                defaultValue: "",
                description: "Comma-separated item categories (e.g. Material, Trophy) excluded from auto-pickup, including items you haven't found yet");

            _cfgQuantityLimits = config.Bind(
                section: "LootFilter",
                key: "QuantityLimits",
                defaultValue: "",
                description: "Comma-separated prefab:amount pairs (e.g. Wood:50). Auto-pickup of that item stops once you carry that many");

            // Load saved rules, and reload them if they're edited in Configuration Manager
            LoadExcludedItems();
            LoadExcludedCategories();
            LoadQuantityLimits();
            _cfgExcluded.SettingChanged += (_, _) => LoadExcludedItems();
            _cfgExcludedCategories.SettingChanged += (_, _) => LoadExcludedCategories();
            _cfgQuantityLimits.SettingChanged += (_, _) => LoadQuantityLimits();
        }

        private static IEnumerable<string> SplitList(string value)
            => (value ?? "")
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0);

        private static void LoadExcludedItems()
        {
            ExcludedItems.Clear();
            foreach (var name in SplitList(_cfgExcluded.Value))
                ExcludedItems.Add(name);
        }

        private static void LoadExcludedCategories()
        {
            ExcludedCategories.Clear();
            foreach (var name in SplitList(_cfgExcludedCategories.Value))
            {
                if (Enum.TryParse(name, ignoreCase: true, out ItemDrop.ItemData.ItemType category))
                    ExcludedCategories.Add(category);
                else
                    Jotunn.Logger.LogWarning($"[LootFilter] Unknown item category '{name}' in ExcludedCategories");
            }
        }

        private static void LoadQuantityLimits()
        {
            QuantityLimits.Clear();
            foreach (var pair in SplitList(_cfgQuantityLimits.Value))
            {
                var parts = pair.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int limit) && limit > 0)
                    QuantityLimits[parts[0].Trim()] = limit;
                else
                    Jotunn.Logger.LogWarning($"[LootFilter] Ignoring invalid quantity limit '{pair}'");
            }
        }

        // ── Query / mutate ────────────────────────────────────────────────────────

        public static bool Enabled
        {
            get => _cfgEnabled == null || _cfgEnabled.Value;
            set { if (_cfgEnabled != null) _cfgEnabled.Value = value; }
        }

        /// <summary>Flip the filter on/off. Returns the new state (true = on).</summary>
        public static bool ToggleEnabled()
        {
            Enabled = !Enabled;
            return Enabled;
        }

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

        // ── Category rules ────────────────────────────────────────────────────────

        public static bool IsCategoryExcluded(ItemDrop.ItemData.ItemType category)
            => ExcludedCategories.Contains(category);

        /// <summary>Flip a whole category. Returns the new state (true = excluded).</summary>
        public static bool ToggleCategoryExcluded(ItemDrop.ItemData.ItemType category)
        {
            bool nowExcluded = ExcludedCategories.Add(category);
            if (!nowExcluded)
                ExcludedCategories.Remove(category);

            if (_cfgExcludedCategories != null)
                _cfgExcludedCategories.Value = string.Join(",", ExcludedCategories);
            return nowExcluded;
        }

        // ── Quantity rules ────────────────────────────────────────────────────────

        /// <summary>Keep-up-to amount for an item, or 0 when it has no limit.</summary>
        public static int GetQuantityLimit(string prefabName)
            => QuantityLimits.TryGetValue(prefabName, out int limit) ? limit : 0;

        /// <summary>Set the keep-up-to amount; 0 or less removes the limit.</summary>
        public static void SetQuantityLimit(string prefabName, int limit)
        {
            if (limit > 0)
                QuantityLimits[prefabName] = limit;
            else
                QuantityLimits.Remove(prefabName);

            if (_cfgQuantityLimits != null)
                _cfgQuantityLimits.Value = string.Join(",", QuantityLimits.Select(kv => $"{kv.Key}:{kv.Value}"));
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
        {
            if (!item.m_autoPickup)
                return false;
            if (!Enabled)
                return true;

            var shared = item.m_itemData?.m_shared;
            if (shared != null && ExcludedCategories.Contains(shared.m_itemType))
                return false;

            string prefabName = GetPrefabName(item);
            if (IsExcluded(prefabName))
                return false;

            // Quantity rule: stop once the inventory already holds the limit.
            // A whole stack is still picked up, so the total can end up a bit over.
            if (shared != null && QuantityLimits.TryGetValue(prefabName, out int limit))
            {
                var inventory = Player.m_localPlayer?.GetInventory();
                if (inventory != null && inventory.CountItems(shared.m_name, -1, false) >= limit)
                    return false;
            }

            return true;
        }

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
