using Jotunn.GUI;
using Jotunn.Managers;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ValheimLootFilter
{
    /// <summary>
    /// Loot Filter UI — Trophy-style grid panel.
    /// Groups every discovered ItemDrop by category, shows icons,
    /// and lets the player toggle auto-pickup exclusion per item.
    /// </summary>
    public static class LootFilterUI
    {
        //  Panel root 
        private static GameObject _filtersPanel;

        //  Scroll view internals 
        private static GameObject _scrollView;
        private static Transform _contentRoot;   // the ScrollRect.content transform

        //  Search 
        private static InputField _searchField;

        // Every cell with its lowercase display name, and its section, so the
        // search can hide/show cells without rebuilding the grid.
        private static readonly List<(GameObject cell, string name, GameObject section)> _cells = new();

        // Frame in which Esc closed the panel, so vanilla doesn't also act on it
        private static int _closedByEscapeFrame = -1;

        //  Sprite cache so we only load each icon once 
        private static readonly Dictionary<string, Sprite> _iconCache = new();

        //  Colours 
        private static readonly Color ColNormal = new Color(1f, 1f, 1f, 1f);   // white  — not excluded
        private static readonly Color ColExcluded = new Color(1f, 0.2f, 0.2f, 1f); // red    — excluded

        //  Layout constants 
        private const float PanelW = 1050f;
        private const float PanelH = 800f;
        private const float ScrollW = 970f;
        private const float ScrollH = 600f;
        private const float CellSize = 64f;
        private const float CellSpacing = 8f;
        private const float GroupHeaderH = 30f;
        private const float GroupHeaderFontSize = 18f;

        /// <summary>
        /// Open or close the filter panel.
        /// </summary>
        public static void TogglePanel()
        {
            if (!_filtersPanel)
                BuildPanel();

            // BuildPanel bails out if the GUI isn't ready yet (e.g. loading screen)
            if (!_filtersPanel)
                return;

            bool next = !_filtersPanel.activeSelf;
            _filtersPanel.SetActive(next);
            GUIManager.BlockInput(next);

            if (next)
                RefreshContent();
            else if (_searchField)
                _searchField.DeactivateInputField();
        }

        public static bool IsOpen => _filtersPanel && _filtersPanel.activeSelf;

        /// <summary>True while the player is typing in the search box.</summary>
        public static bool IsSearchFocused => _searchField && _searchField.isFocused;

        /// <summary>
        /// Close the panel if Esc (or gamepad B) was pressed. Call once per frame.
        /// </summary>
        public static void HandleEscape()
        {
            if (!IsOpen)
                return;

            if (ZInput.GetKeyDown(KeyCode.Escape, true) || ZInput.GetButtonDown("JoyButtonB"))
            {
                _closedByEscapeFrame = Time.frameCount;
                TogglePanel();
            }
        }

        /// <summary>
        /// Stops the pause menu and the inventory from reacting to the same Esc
        /// press that just closed our panel.
        /// </summary>
        [HarmonyLib.HarmonyPatch]
        public static class Patches
        {
            [HarmonyLib.HarmonyPatch(typeof(Menu), nameof(Menu.Update))]
            [HarmonyLib.HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
            [HarmonyLib.HarmonyPrefix]
            private static bool SkipOnEscapeFrame()
                => _closedByEscapeFrame != Time.frameCount;
        }

        /// <summary>
        /// Create the woodpanel, and populate the content
        /// </summary>
        private static void BuildPanel()
        {
            if (GUIManager.Instance == null || !GUIManager.CustomGUIFront)
            {
                Jotunn.Logger.LogError("GUIManager not ready");
                return;
            }

            // root wood panel 
            _filtersPanel = GUIManager.Instance.CreateWoodpanel(
                parent: GUIManager.CustomGUIFront.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: Vector2.zero,
                width: PanelW,
                height: PanelH,
                draggable: true);
            _filtersPanel.SetActive(false);

            // DragWindowCntrl is added by CreateWoodpanel; adding it again would
            // duplicate it — only add if somehow missing.
            if (!_filtersPanel.GetComponent<DragWindowCntrl>())
                _filtersPanel.AddComponent<DragWindowCntrl>();

            // Title 
            var title = GUIManager.Instance.CreateText(
                text: "Loot Filter",
                parent: _filtersPanel.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(0f, -20f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 30,
                color: GUIManager.Instance.ValheimOrange,
                outline: true,
                outlineColor: Color.black,
                width: 400f,
                height: 40f,
                addContentSizeFitter: false);
            title.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);

            //  Subtitle / legend 
            var legend = GUIManager.Instance.CreateText(
                text: "Click an item to toggle auto-pickup  [ red = excluded ]",
                parent: _filtersPanel.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(0f, -56f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 13,
                color: Color.white,
                outline: true,
                outlineColor: Color.black,
                width: 700f,
                height: 24f,
                addContentSizeFitter: false);
            legend.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);

            //  Search box 
            var search = GUIManager.Instance.CreateInputField(
                parent: _filtersPanel.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(0f, -86f),
                contentType: InputField.ContentType.Standard,
                placeholderText: "Search items...",
                fontSize: 16,
                width: 400f,
                height: 32f);
            search.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
            _searchField = search.GetComponent<InputField>();
            _searchField.onValueChanged.AddListener(ApplySearch);

            //  Scroll view 
            // Position it so there's room for the title, legend and search above
            // and Close below.
            _scrollView = GUIManager.Instance.CreateScrollView(
                parent: _filtersPanel.transform,
                showHorizontalScrollbar: false,
                showVerticalScrollbar: true,
                handleSize: 5f,
                handleDistanceToBorder: 2f,
                handleColors: GUIManager.Instance.ValheimScrollbarHandleColorBlock,
                slidingAreaBackgroundColor: GUIManager.Instance.ValheimBeige,
                width: ScrollW,
                height: ScrollH);

            // Anchor the scroll view inside the panel
            var svRT = _scrollView.GetComponent<RectTransform>();
            svRT.anchorMin = new Vector2(0.5f, 1f);
            svRT.anchorMax = new Vector2(0.5f, 1f);
            svRT.pivot = new Vector2(0.5f, 1f);
            svRT.anchoredPosition = new Vector2(0f, -126f);   // below title, legend, search

            //  Resolve the *actual* content transform 
            // CreateScrollView returns the ScrollView GameObject; the content lives
            // inside ScrollRect.content, NOT directly on _scrollView.transform.
            var sr = _scrollView.GetComponentInChildren<ScrollRect>();
            if (sr == null)
            {
                Jotunn.Logger.LogError("ScrollRect not found inside CreateScrollView result");
                return;
            }
            _contentRoot = sr.content;

            // Add a VerticalLayoutGroup + ContentSizeFitter to the content so items
            // stack automatically and the scroll view resizes correctly.
            var vlg = _contentRoot.gameObject.GetOrAddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlHeight = false;
            vlg.childControlWidth = false;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = false;
            vlg.spacing = 10f;
            vlg.padding = new RectOffset(10, 10, 10, 10);

            var csf = _contentRoot.gameObject.GetOrAddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            //  Close button 
            var closeBtn = GUIManager.Instance.CreateButton(
                text: "Close",
                parent: _filtersPanel.transform,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position: new Vector2(0f, 40f),
                width: 250f,
                height: 60f);
            closeBtn.SetActive(true);
            closeBtn.GetComponent<Button>().onClick.AddListener(TogglePanel);
        }


        /// <summary>
        /// Rebuild the grid, grouping items by ItemDrop.ItemData.ItemType.
        /// </summary>
        private static void RefreshContent()
        {
            if (_contentRoot == null)
            {
                Jotunn.Logger.LogError("Content root is null — panel was not built correctly");
                return;
            }

            // Destroy previous children
            foreach (Transform child in _contentRoot)
                Object.Destroy(child.gameObject);
            _cells.Clear();

            var allItems = LootFilterService.GetAllItems();
            Jotunn.Logger.LogInfo($"Found {allItems.Count} total items in ObjectDB");

            // Filter to only items the player has discovered.
            var knownNames = Player.m_localPlayer != null
                ? Player.m_localPlayer.m_knownMaterial
                : new HashSet<string>();

            Jotunn.Logger.LogInfo($"{knownNames.Count} items are known");
            var groups = allItems
                .GroupBy(i => i.m_itemData?.m_shared?.m_itemType ?? ItemDrop.ItemData.ItemType.Misc)
                .OrderBy(g => g.Key.ToString());
            Jotunn.Logger.LogInfo($"{groups.Count()} groups will be created");

            foreach (var group in groups)
            {
                // Only include items the player has encountered (trophy UI behaviour)
                var discovered = group
                    .Where(i => knownNames.Count == 0 || knownNames.Contains(i.m_itemData?.m_shared?.m_name))
                    .ToList();

                if (discovered.Count == 0)
                {
                    Jotunn.Logger.LogInfo($"{group.Key} group has no discovered items, skipping...");
                    continue;
                }

                CreateGroupSection(group.Key.ToString(), discovered);
            }

            // Re-apply the search the player left in the box last time
            ApplySearch(_searchField ? _searchField.text : "");
        }

        /// <summary>
        /// Show only cells whose name contains the query (case-insensitive) and
        /// hide sections left with no visible cells.
        /// </summary>
        private static void ApplySearch(string query)
        {
            query = (query ?? "").Trim().ToLowerInvariant();

            var visibleSections = new HashSet<GameObject>();
            foreach (var (cell, name, section) in _cells)
            {
                bool match = query.Length == 0 || name.Contains(query);
                cell.SetActive(match);
                if (match) visibleSections.Add(section);
            }

            foreach (var (_, _, section) in _cells)
                section.SetActive(visibleSections.Contains(section));

            // Force layout rebuild so the scroll view height is correct
            if (_contentRoot)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot as RectTransform);
        }

        private static string TooltipText(bool excluded)
            => excluded
                ? "<color=#ff5050>Excluded from auto-pickup</color>\nClick to allow"
                : "Auto-pickup allowed\nClick to exclude";

        //  One labelled section per category 
        private static void CreateGroupSection(string categoryName, List<ItemDrop> items)
        {
            Jotunn.Logger.LogInfo($"Creating section for category '{categoryName}' with {items.Count} items");
            // Section root — fixed width to match scroll view, auto height
            var section = new GameObject($"Section_{categoryName}", typeof(RectTransform));
            section.transform.SetParent(_contentRoot, false);

            var sectionRT = section.GetComponent<RectTransform>();
            sectionRT.sizeDelta = new Vector2(ScrollW - 24f, 0f);   // height driven by layout

            var sectionVLG = section.AddComponent<VerticalLayoutGroup>();
            sectionVLG.childAlignment = TextAnchor.UpperLeft;
            sectionVLG.childControlHeight = false;
            sectionVLG.childControlWidth = false;
            sectionVLG.childForceExpandHeight = false;
            sectionVLG.childForceExpandWidth = false;
            sectionVLG.spacing = 4f;

            var sectionCSF = section.AddComponent<ContentSizeFitter>();
            sectionCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sectionCSF.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            //  Category header label 
            var header = GUIManager.Instance.CreateText(
                text: categoryName,
                parent: section.transform,
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                position: Vector2.zero,
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: (int)GroupHeaderFontSize,
                color: GUIManager.Instance.ValheimOrange,
                outline: true,
                outlineColor: Color.black,
                width: ScrollW - 24f,
                height: GroupHeaderH,
                addContentSizeFitter: false);

            var headerRT = header.GetComponent<RectTransform>();
            headerRT.pivot = new Vector2(0f, 1f);
            headerRT.sizeDelta = new Vector2(ScrollW - 24f, GroupHeaderH);

            var headerText = header.GetComponent<Text>();
            if (headerText) headerText.alignment = TextAnchor.MiddleLeft;

            //  Grid row 
            var grid = new GameObject($"Grid_{categoryName}", typeof(RectTransform));
            grid.transform.SetParent(section.transform, false);

            var gridRT = grid.GetComponent<RectTransform>();
            gridRT.sizeDelta = new Vector2(ScrollW - 24f, 0f);

            var glg = grid.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(CellSize, CellSize);
            glg.spacing = new Vector2(CellSpacing, CellSpacing);
            glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
            glg.startAxis = GridLayoutGroup.Axis.Horizontal;
            glg.childAlignment = TextAnchor.UpperLeft;
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;

            int columns = Mathf.FloorToInt((ScrollW - 24f + CellSpacing) / (CellSize + CellSpacing));
            glg.constraintCount = Mathf.Max(1, columns);

            var gridCSF = grid.AddComponent<ContentSizeFitter>();
            gridCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            gridCSF.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            //  Item cells 
            foreach (var item in items)
                CreateItemCell(grid.transform, section, item);
        }

        //  Single icon cell 
        private static void CreateItemCell(Transform parent, GameObject section, ItemDrop item)
        {
            bool isExcluded = LootFilterService.IsExcluded(item.name);

            // Root button object
            var cell = new GameObject($"Cell_{item.name}", typeof(RectTransform));
            cell.transform.SetParent(parent, false);

            var btn = cell.AddComponent<Button>();

            // Background image (reuse a simple sprite; you can swap for a framed one)
            var bg = cell.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);

            //  Item icon 
            var iconGO = new GameObject("Icon", typeof(RectTransform));
            iconGO.transform.SetParent(cell.transform, false);

            var iconRT = iconGO.GetComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.1f, 0.1f);
            iconRT.anchorMax = new Vector2(0.9f, 0.9f);
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;

            var iconImg = iconGO.AddComponent<Image>();
            var sprite = GetItemSprite(item);
            if (sprite) iconImg.sprite = sprite;

            iconImg.color = isExcluded ? ColExcluded : ColNormal;

            //  Red X overlay when excluded 
            var xOverlay = GUIManager.Instance.CreateText(
                text: "✕",
                parent: cell.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: Vector2.zero,
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 28,
                color: new Color(1f, 0.15f, 0.15f, 0.9f),
                outline: true,
                outlineColor: Color.black,
                width: CellSize,
                height: CellSize,
                addContentSizeFitter: false);
            xOverlay.SetActive(isExcluded);

            //  Tooltip (item display name) 
            string displayName = item.m_itemData?.m_shared?.m_name ?? item.name;
            // Valheim uses localisation keys like "$item_sword_iron"; resolve if possible
            if (Localization.instance != null && displayName.StartsWith("$"))
                displayName = Localization.instance.Localize(displayName);

            _cells.Add((cell, displayName.ToLowerInvariant(), section));

            //  Hover tooltip 
            var tooltip = cell.AddComponent<CellTooltip>();
            tooltip.Set(displayName, TooltipText(isExcluded));

            //  Button click handler 
            // Capture locals for the closure
            string itemName = item.name;
            string capturedName = displayName;
            Image capturedIcon = iconImg;
            GameObject capturedX = xOverlay;

            btn.onClick.AddListener(() =>
            {
                bool nowExcluded = LootFilterService.ToggleExcluded(itemName);
                capturedIcon.color = nowExcluded ? ColExcluded : ColNormal;
                capturedX.SetActive(nowExcluded);
                tooltip.Set(capturedName, TooltipText(nowExcluded));
                Jotunn.Logger.LogInfo($"[LootFilter] {itemName} excluded={nowExcluded}");
            });

            // Colour tint block so hover feedback works
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.3f, 1.3f, 0.7f, 1f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            btn.colors = cb;
            btn.targetGraphic = bg;
        }

        private static Sprite GetItemSprite(ItemDrop item)
        {
            if (_iconCache.TryGetValue(item.name, out var cached))
                return cached;

            Sprite sprite = item.m_itemData?.m_shared?.m_icons?.Length > 0
                ? item.m_itemData.m_shared.m_icons[0]
                : null;

            _iconCache[item.name] = sprite;
            return sprite;
        }
    }

    /// <summary>
    /// Hover tooltip for filter cells: a small dark box with the item name and
    /// its filter state that follows the cursor.
    /// We build our own instead of using vanilla's UITooltip or its prefab:
    /// UITooltip spawns under the nearest Canvas, which inside Jötunn's scroll
    /// view is the masked Content object, and the prefab draws its box away
    /// from its own position when moved to another canvas.
    /// </summary>
    internal class CellTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float ShowDelay = 0.3f;

        // Gap between the cursor and the tooltip's top-left corner (GUI units)
        private static readonly Vector2 CursorOffset = new Vector2(20f, -20f);

        // One tooltip object, shared by all cells and reused between hovers
        private static RectTransform s_tooltip;
        private static Text s_topicText;
        private static Text s_bodyText;
        private static CellTooltip s_owner;

        private string _topic = "";
        private string _text = "";
        private bool _hovered;
        private float _timer;

        public void Set(string topic, string text)
        {
            _topic = topic;
            _text = text;
            if (s_owner == this)
                UpdateText();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            _timer = 0f;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            Hide();
        }

        private void OnDisable()
        {
            _hovered = false;
            Hide();
        }

        private void LateUpdate()
        {
            if (!_hovered)
                return;

            if (s_owner != this)
            {
                _timer += Time.unscaledDeltaTime;
                if (_timer < ShowDelay)
                    return;
                Show();
            }

            if (s_tooltip)
                PositionAtCursor();
        }

        private void Show()
        {
            if (!s_tooltip)
                BuildTooltip();

            s_owner = this;
            UpdateText();
            s_tooltip.SetAsLastSibling();
            s_tooltip.gameObject.SetActive(true);
        }

        private void Hide()
        {
            if (s_owner != this)
                return;

            s_owner = null;
            if (s_tooltip)
                s_tooltip.gameObject.SetActive(false);
        }

        private void UpdateText()
        {
            s_topicText.text = _topic;
            s_bodyText.text = _text;

            // Size the box to the new text right away so positioning is exact
            LayoutRebuilder.ForceRebuildLayoutImmediate(s_tooltip);
        }

        private static void BuildTooltip()
        {
            var go = new GameObject("LootFilterTooltip", typeof(RectTransform));
            go.transform.SetParent(GUIManager.CustomGUIFront.transform, false);

            s_tooltip = (RectTransform)go.transform;
            s_tooltip.anchorMin = s_tooltip.anchorMax = Vector2.zero;
            s_tooltip.pivot = new Vector2(0f, 1f);   // top-left corner follows the cursor

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.85f);
            bg.raycastTarget = false;   // never steal the hover from the cell

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 6, 8);
            vlg.spacing = 2f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            var csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            s_topicText = CreateLine(go.transform, 18, GUIManager.Instance.ValheimOrange);
            s_bodyText = CreateLine(go.transform, 14, Color.white);

            go.SetActive(false);
        }

        private static Text CreateLine(Transform parent, int fontSize, Color color)
        {
            var textGO = GUIManager.Instance.CreateText(
                text: "",
                parent: parent,
                anchorMin: Vector2.zero,
                anchorMax: Vector2.zero,
                position: Vector2.zero,
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: fontSize,
                color: color,
                outline: true,
                outlineColor: Color.black,
                width: 0f,
                height: 0f,
                addContentSizeFitter: false);

            var text = textGO.GetComponent<Text>();
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// Hang the tooltip below-right of the cursor, flipping to the left /
        /// above near screen edges.
        /// </summary>
        private static void PositionAtCursor()
        {
            // Overlay canvas: world space is screen pixels, y grows upwards
            float scale = s_tooltip.lossyScale.x;
            Vector2 size = s_tooltip.rect.size * scale;
            Vector2 offset = CursorOffset * scale;
            Vector2 pointer = ZInput.pointerPosition;

            float left = pointer.x + offset.x;
            if (left + size.x > Screen.width)
                left = pointer.x - offset.x - size.x;

            float top = pointer.y + offset.y;
            if (top - size.y < 0f)
                top = pointer.y - offset.y + size.y;

            left = Mathf.Clamp(left, 0f, Mathf.Max(0f, Screen.width - size.x));
            top = Mathf.Clamp(top, Mathf.Min(size.y, Screen.height), Screen.height);

            s_tooltip.position = new Vector3(left, top, 0f);
        }
    }

    internal static class GameObjectExtensions
    {
        public static T GetOrAddComponent<T>(this GameObject go) where T : Component
            => go.GetComponent<T>() ?? go.AddComponent<T>();
    }

}
