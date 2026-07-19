using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimLootFilter
{
    /// <summary>
    /// Injects a "Loot Filter" button into the InventoryGui top bar, next to
    /// the Trophies button.
    ///
    /// Key facts from the actual InventoryGui fields:
    ///   - m_trophiesPanel  : GameObject  — the panel that shows/hides
    ///   - m_pvp            : Toggle      — the friendly-fire toggle
    ///   - There is NO m_trophiesButton field; the button that opens the
    ///     trophies panel is a plain child GameObject in the hierarchy, and its
    ///     click handler is a PERSISTENT (serialized) UnityEvent listener.
    ///
    /// Strategy:
    ///   1. Find the trophies-open button and clone it for vanilla styling.
    ///   2. Replace onClick with a fresh event — RemoveAllListeners() does NOT
    ///      remove persistent listeners, so the clone would still open trophies.
    ///   3. Swap the cloned icon sprite for our own (borrowed from ObjectDB).
    ///   4. Re-lay out every Selectable in the bar with uniform spacing so the
    ///      row stays tidy even when other mods add their own buttons.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
    public static class InventoryGuiPatch
    {
        private const string ButtonName = "lootfilter_button";

        // Item whose inventory icon we borrow for the button. Coins reads as
        // "loot"; change to any prefab name in ObjectDB.
        private const string IconItemPrefab = "Coins";

        // Fallback spacing (local units) if the row has no measurable gap.
        private const float FallbackGap = 8f;

        // Margin (local units) kept between the row and the section edges.
        private const float SideMargin = 8f;

        private static readonly Vector3[] Corners = new Vector3[4];

        [HarmonyPostfix]
        public static void Postfix(InventoryGui __instance)
        {
            var pvpToggle = __instance.m_pvp;
            if (pvpToggle == null)
            {
                Jotunn.Logger.LogWarning("[LootFilter] m_pvp not found — skipping button injection");
                return;
            }

            Transform bar = pvpToggle.transform.parent;   // the top-bar container

            // Guard: only inject once per InventoryGui instance
            if (bar.Find(ButtonName) != null)
                return;

            Button trophiesBtn = FindTrophiesButton(bar, pvpToggle);
            if (trophiesBtn == null)
            {
                Jotunn.Logger.LogWarning("[LootFilter] Trophies button not found — skipping button injection");
                return;
            }

            // ── 1. Clone the trophies button for pixel-perfect vanilla style ─────
            var newBtnGO = Object.Instantiate(trophiesBtn.gameObject, bar);
            newBtnGO.name = ButtonName;

            // ── 2. Wire up click ─────────────────────────────────────────────────
            // The cloned onClick still carries the serialized listener that opens
            // the trophies panel; RemoveAllListeners() would NOT clear it.
            // Replacing the whole event object does.
            var btn = newBtnGO.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(LootFilterUI.TogglePanel);

            // Update label if the button has one (TMP first, legacy Text fallback)
            var tmpLabel = newBtnGO.GetComponentInChildren<TMP_Text>();
            if (tmpLabel != null)
                tmpLabel.text = "Loot Filter";
            else
            {
                var legacyLabel = newBtnGO.GetComponentInChildren<Text>();
                if (legacyLabel != null) legacyLabel.text = "Loot Filter";
            }

            // Update hover tooltip if present
            var tooltip = newBtnGO.GetComponentInChildren<UITooltip>();
            if (tooltip != null)
            {
                tooltip.m_topic = "";
                tooltip.m_text = "Loot Filter";
            }

            // ── 3. Swap the icon sprite ──────────────────────────────────────────
            var iconImage = FindIconImage(newBtnGO);
            if (iconImage != null)
            {
                // If the button swaps sprites on hover/press it would flash the
                // trophy sprites again — fall back to a colour tint instead.
                if (btn.transition == Selectable.Transition.SpriteSwap && btn.targetGraphic == iconImage)
                    btn.transition = Selectable.Transition.ColorTint;

                __instance.StartCoroutine(SwapIconWhenReady(iconImage));
            }
            else
            {
                Jotunn.Logger.LogWarning("[LootFilter] No icon Image found on cloned button — keeping trophy icon");
            }

            // ── 4. Re-lay out the whole row with uniform spacing ─────────────────
            LayoutRow(bar, trophiesBtn.GetComponent<RectTransform>(), newBtnGO.GetComponent<RectTransform>());

            Jotunn.Logger.LogInfo("[LootFilter] Loot Filter button injected into InventoryGui");
        }

        // ── Find the button that opens the trophies panel ────────────────────────
        //   a) direct child of bar whose name contains "Trophies"
        //   b) fallback: the Button (world-space) closest to the left of m_pvp
        private static Button FindTrophiesButton(Transform bar, Toggle pvpToggle)
        {
            Button nearestLeftOfPvp = null;
            float nearestDistance = float.MaxValue;
            float pvpX = WorldLeft(pvpToggle.GetComponent<RectTransform>());

            foreach (Transform child in bar)
            {
                var b = child.GetComponent<Button>();
                if (b == null) continue;

                if (child.name.IndexOf("Trophies", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return b;

                float right = WorldRight((RectTransform)child);
                float distance = pvpX - right;
                if (distance > 0f && distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestLeftOfPvp = b;
                }
            }

            return nearestLeftOfPvp;
        }

        // ── Pick the Image that draws the button icon ────────────────────────────
        // Vanilla icon buttons draw the icon on a child Image; take the last child
        // Image (deepest in draw order) and fall back to the root Image.
        private static Image FindIconImage(GameObject buttonGO)
        {
            Image best = null;
            foreach (var img in buttonGO.GetComponentsInChildren<Image>(true))
            {
                if (img.gameObject == buttonGO) continue;
                best = img;
            }

            return best != null ? best : buttonGO.GetComponent<Image>();
        }

        // ── Swap the icon once ObjectDB has items ────────────────────────────────
        // InventoryGui.Awake can run before ObjectDB is populated, so wait for it.
        private static IEnumerator SwapIconWhenReady(Image iconImage)
        {
            while (ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0)
                yield return null;

            if (iconImage == null)
                yield break;

            var prefab = ObjectDB.instance.GetItemPrefab(IconItemPrefab);
            var sprite = prefab != null
                ? prefab.GetComponent<ItemDrop>()?.m_itemData?.GetIcon()
                : null;

            if (sprite != null)
                iconImage.sprite = sprite;
            else
                Jotunn.Logger.LogWarning($"[LootFilter] Could not resolve icon sprite from prefab '{IconItemPrefab}'");
        }

        // ── Uniform row layout ───────────────────────────────────────────────────
        // Collects every active Selectable in the bar, sorts them by on-screen x,
        // inserts our button right after the trophies button, then fits the row
        // inside the bar's own rect: total button width plus side margins is the
        // budget, and the gap is whatever spacing that budget allows (capped at
        // the row's natural spacing so a sparse row doesn't spread out). The row
        // is right-aligned inside the section, like vanilla.
        private static void LayoutRow(Transform bar, RectTransform trophiesRT, RectTransform insertedRT)
        {
            var items = new List<RectTransform>();
            foreach (Transform child in bar)
            {
                if (!child.gameObject.activeSelf) continue;
                if (child == insertedRT.transform) continue;
                if (child.GetComponent<Selectable>() == null) continue;
                items.Add((RectTransform)child);
            }

            if (items.Count == 0)
                return;

            items.Sort((a, b) => WorldLeft(a).CompareTo(WorldLeft(b)));

            float scale = bar.lossyScale.x;
            if (scale <= 0f) scale = 1f;

            // Median gap between existing neighbours = the row's natural spacing
            var gaps = new List<float>();
            for (int i = 1; i < items.Count; i++)
                gaps.Add(WorldLeft(items[i]) - WorldRight(items[i - 1]));
            gaps.Sort();
            float naturalGap = gaps.Count > 0 ? gaps[gaps.Count / 2] : FallbackGap * scale;

            // Slot our button in right after the trophies button
            int idx = items.IndexOf(trophiesRT);
            items.Insert(idx >= 0 ? idx + 1 : items.Count, insertedRT);

            float totalWidth = 0f;
            foreach (var item in items)
                totalWidth += WorldRight(item) - WorldLeft(item);

            // The bar's rect is the section — its width is our layout budget.
            var barRT = (RectTransform)bar;
            float barLeft = WorldLeft(barRT);
            float barRight = WorldRight(barRT);
            float barWidth = barRight - barLeft;

            // Shrink the margins if even they don't fit; gap fills what's left.
            float margin = Mathf.Min(SideMargin * scale, Mathf.Max(0f, (barWidth - totalWidth) * 0.5f));
            float budgetGap = items.Count > 1
                ? (barWidth - 2f * margin - totalWidth) / (items.Count - 1)
                : 0f;
            float gap = Mathf.Max(0f, Mathf.Min(naturalGap, budgetGap));

            // Right-align: last item's right edge sits `margin` inside the bar.
            float cursor = barRight - margin;
            if (barWidth < totalWidth)
            {
                // Degenerate container rect (e.g. zero-size holder) — keep the
                // current rightmost edge as the anchor instead of the rect.
                Jotunn.Logger.LogWarning(
                    $"[LootFilter] Bar rect ({barWidth / scale:F0}) narrower than buttons ({totalWidth / scale:F0}) — ignoring section bounds");
                cursor = WorldRight(items[items.Count - 1]);
            }

            for (int i = items.Count - 1; i >= 0; i--)
            {
                float left = WorldLeft(items[i]);
                float width = WorldRight(items[i]) - left;
                float desiredLeft = cursor - width;

                var pos = items[i].anchoredPosition;
                pos.x += (desiredLeft - left) / scale;
                items[i].anchoredPosition = pos;

                cursor = desiredLeft - gap;
            }
        }

        private static float WorldLeft(RectTransform rt)
        {
            rt.GetWorldCorners(Corners);
            return Corners[0].x;
        }

        private static float WorldRight(RectTransform rt)
        {
            rt.GetWorldCorners(Corners);
            return Corners[2].x;
        }
    }
}
