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

        //  Filter on/off button 
        private static Text _filterToggleLabel;

        //  Per-cell and per-category views, refreshed when rules change 
        private static readonly List<CellView> _cellViews = new();
        private static readonly Dictionary<ItemDrop.ItemData.ItemType, Text> _categoryButtonLabels = new();
        private static readonly Dictionary<ItemDrop.ItemData.ItemType, Text> _categoryHeaders = new();

        //  Keep-up-to limit popup 
        private static GameObject _limitPopup;
        private static Text _limitPopupTitle;
        private static InputField _limitField;
        private static CellView _limitTarget;

        private class CellView
        {
            public string PrefabName;
            public string DisplayName;
            public ItemDrop.ItemData.ItemType Category;
            public Image Icon;
            public GameObject XOverlay;
            public Text LimitLabel;
            public CellTooltip Tooltip;
        }

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
        private static readonly Color ColLimit = new Color(1f, 0.85f, 0.35f, 1f);  // yellow — keep-up-to amount

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
            else
            {
                if (_searchField)
                    _searchField.DeactivateInputField();
                CloseLimitPopup();
            }
        }

        public static bool IsOpen => _filtersPanel && _filtersPanel.activeSelf;

        /// <summary>True while the player is typing in the search box or the limit field.</summary>
        public static bool IsTyping
            => (_searchField && _searchField.isFocused) || (_limitField && _limitField.isFocused);

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

                // Esc backs out of the limit popup first, then the panel
                if (_limitPopup && _limitPopup.activeSelf)
                    CloseLimitPopup();
                else
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
                text: "Click: toggle auto-pickup    Right-click: keep-up-to amount    [ red = excluded ]",
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

            //  Filter on/off button (bottom left) 
            var filterBtn = GUIManager.Instance.CreateButton(
                text: "",
                parent: _filtersPanel.transform,
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(0f, 0f),
                position: new Vector2(170f, 40f),
                width: 220f,
                height: 60f);
            filterBtn.SetActive(true);
            filterBtn.GetComponent<Button>().onClick.AddListener(() => LootFilterService.ToggleEnabled());
            _filterToggleLabel = filterBtn.GetComponentInChildren<Text>();

            // Stay in sync when the hotkey or Configuration Manager flips it
            LootFilterService.EnabledChanged += _ => UpdateFilterToggleLabel();
            UpdateFilterToggleLabel();

            BuildLimitPopup();
        }

        private static void UpdateFilterToggleLabel()
        {
            if (!_filterToggleLabel)
                return;

            _filterToggleLabel.text = LootFilterService.Enabled
                ? "Filter: <color=#7fd17f>On</color>"
                : "Filter: <color=#ff5050>Off</color>";
            _filterToggleLabel.supportRichText = true;
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
            _cellViews.Clear();
            _categoryButtonLabels.Clear();
            _categoryHeaders.Clear();

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

                CreateGroupSection(group.Key, discovered);
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

        private static string TooltipText(CellView view)
        {
            string state;
            if (LootFilterService.IsCategoryExcluded(view.Category))
                state = "<color=#ff5050>Excluded with its category</color>\nUse the category button to allow";
            else if (LootFilterService.IsExcluded(view.PrefabName))
                state = "<color=#ff5050>Excluded from auto-pickup</color>\nClick to allow";
            else
            {
                int limit = LootFilterService.GetQuantityLimit(view.PrefabName);
                state = limit > 0
                    ? $"Auto-pickup until you carry {limit}\nClick to exclude"
                    : "Auto-pickup allowed\nClick to exclude";
            }

            return state + "\nRight-click to set a keep-up-to amount";
        }

        /// <summary>Re-apply icon tint, ✕, limit label and tooltip from the current rules.</summary>
        private static void RefreshCell(CellView view)
        {
            bool excluded = LootFilterService.IsCategoryExcluded(view.Category)
                            || LootFilterService.IsExcluded(view.PrefabName);
            int limit = LootFilterService.GetQuantityLimit(view.PrefabName);

            view.Icon.color = excluded ? ColExcluded : ColNormal;
            view.XOverlay.SetActive(excluded);
            view.LimitLabel.text = limit > 0 ? limit.ToString() : "";
            view.LimitLabel.gameObject.SetActive(limit > 0 && !excluded);
            view.Tooltip.Set(view.DisplayName, TooltipText(view));
        }

        private static void RefreshCategory(ItemDrop.ItemData.ItemType category)
        {
            bool excluded = LootFilterService.IsCategoryExcluded(category);
            if (_categoryButtonLabels.TryGetValue(category, out var label) && label)
                label.text = excluded ? "Allow all" : "Exclude all";
            if (_categoryHeaders.TryGetValue(category, out var header) && header)
                header.text = excluded
                    ? $"{category}  <color=#ff5050>(whole category excluded)</color>"
                    : category.ToString();

            foreach (var view in _cellViews)
                if (view.Category == category)
                    RefreshCell(view);
        }

        //  One labelled section per category 
        private static void CreateGroupSection(ItemDrop.ItemData.ItemType category, List<ItemDrop> items)
        {
            string categoryName = category.ToString();
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

            //  Category header row: name on the left, exclude-all button on the right 
            var headerRow = new GameObject($"Header_{categoryName}", typeof(RectTransform));
            headerRow.transform.SetParent(section.transform, false);
            ((RectTransform)headerRow.transform).sizeDelta = new Vector2(ScrollW - 24f, GroupHeaderH);

            var categoryBtn = GUIManager.Instance.CreateButton(
                text: "",
                parent: headerRow.transform,
                anchorMin: new Vector2(1f, 0.5f),
                anchorMax: new Vector2(1f, 0.5f),
                position: new Vector2(-100f, 0f),
                width: 200f,
                height: GroupHeaderH);
            categoryBtn.SetActive(true);
            var categoryLabel = categoryBtn.GetComponentInChildren<Text>();
            if (categoryLabel)
            {
                categoryLabel.fontSize = 14;
                _categoryButtonLabels[category] = categoryLabel;
            }
            categoryBtn.GetComponent<Button>().onClick.AddListener(() =>
            {
                bool nowExcluded = LootFilterService.ToggleCategoryExcluded(category);
                RefreshCategory(category);
                Jotunn.Logger.LogInfo($"[LootFilter] category {category} excluded={nowExcluded}");
            });

            var header = GUIManager.Instance.CreateText(
                text: categoryName,
                parent: headerRow.transform,
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

            // Header text must not swallow clicks meant for the button
            if (headerText)
            {
                headerText.raycastTarget = false;
                headerText.supportRichText = true;
                _categoryHeaders[category] = headerText;
            }

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
                CreateItemCell(grid.transform, section, category, item);

            RefreshCategory(category);
        }

        //  Single icon cell 
        private static void CreateItemCell(Transform parent, GameObject section,
            ItemDrop.ItemData.ItemType category, ItemDrop item)
        {

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

            //  Keep-up-to amount, bottom right 
            var limitGO = GUIManager.Instance.CreateText(
                text: "",
                parent: cell.transform,
                anchorMin: new Vector2(1f, 0f),
                anchorMax: new Vector2(1f, 0f),
                position: new Vector2(-3f, 2f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 14,
                color: ColLimit,
                outline: true,
                outlineColor: Color.black,
                width: CellSize,
                height: 18f,
                addContentSizeFitter: false);
            var limitRT = limitGO.GetComponent<RectTransform>();
            limitRT.pivot = new Vector2(1f, 0f);
            var limitText = limitGO.GetComponent<Text>();
            limitText.alignment = TextAnchor.LowerRight;
            limitText.raycastTarget = false;

            //  Tooltip (item display name) 
            string displayName = item.m_itemData?.m_shared?.m_name ?? item.name;
            // Valheim uses localisation keys like "$item_sword_iron"; resolve if possible
            if (Localization.instance != null && displayName.StartsWith("$"))
                displayName = Localization.instance.Localize(displayName);

            _cells.Add((cell, displayName.ToLowerInvariant(), section));

            //  Hover tooltip 
            var view = new CellView
            {
                PrefabName = item.name,
                DisplayName = displayName,
                Category = category,
                Icon = iconImg,
                XOverlay = xOverlay,
                LimitLabel = limitText,
                Tooltip = cell.AddComponent<CellTooltip>(),
            };
            _cellViews.Add(view);
            RefreshCell(view);

            //  Left click: exclude/allow this item 
            btn.onClick.AddListener(() =>
            {
                // A category-wide exclusion wins; the category button undoes it
                if (LootFilterService.IsCategoryExcluded(view.Category))
                    return;

                bool nowExcluded = LootFilterService.ToggleExcluded(view.PrefabName);
                RefreshCell(view);
                Jotunn.Logger.LogInfo($"[LootFilter] {view.PrefabName} excluded={nowExcluded}");
            });

            //  Right click: keep-up-to amount 
            cell.AddComponent<RightClickHandler>().OnRightClick = () => OpenLimitPopup(view);

            // Colour tint block so hover feedback works
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.3f, 1.3f, 0.7f, 1f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            btn.colors = cb;
            btn.targetGraphic = bg;
        }

        //  Keep-up-to limit popup 
        private static void BuildLimitPopup()
        {
            // Full-panel dimmer that also blocks clicks on the grid behind
            _limitPopup = new GameObject("LimitPopup", typeof(RectTransform));
            _limitPopup.transform.SetParent(_filtersPanel.transform, false);
            var rootRT = (RectTransform)_limitPopup.transform;
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;
            _limitPopup.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            var box = new GameObject("Box", typeof(RectTransform));
            box.transform.SetParent(_limitPopup.transform, false);
            var boxRT = (RectTransform)box.transform;
            boxRT.anchorMin = boxRT.anchorMax = new Vector2(0.5f, 0.5f);
            boxRT.sizeDelta = new Vector2(420f, 200f);
            box.AddComponent<Image>().color = new Color(0.12f, 0.1f, 0.08f, 0.97f);

            _limitPopupTitle = GUIManager.Instance.CreateText(
                text: "",
                parent: box.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(0f, -30f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 18,
                color: GUIManager.Instance.ValheimOrange,
                outline: true,
                outlineColor: Color.black,
                width: 400f,
                height: 50f,
                addContentSizeFitter: false).GetComponent<Text>();
            _limitPopupTitle.alignment = TextAnchor.MiddleCenter;

            var field = GUIManager.Instance.CreateInputField(
                parent: box.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: new Vector2(0f, 5f),
                contentType: InputField.ContentType.IntegerNumber,
                placeholderText: "No limit",
                fontSize: 18,
                width: 160f,
                height: 34f);
            _limitField = field.GetComponent<InputField>();
            _limitField.characterLimit = 6;
            _limitField.onEndEdit.AddListener(_ =>
            {
                // Enter saves; clicking elsewhere just leaves the field
                if (ZInput.GetKeyDown(KeyCode.Return, false) || ZInput.GetKeyDown(KeyCode.KeypadEnter, false))
                    SaveLimitPopup();
            });

            CreatePopupButton(box.transform, "Save", -135f, SaveLimitPopup);
            CreatePopupButton(box.transform, "No limit", 0f, () =>
            {
                _limitField.text = "";
                SaveLimitPopup();
            });
            CreatePopupButton(box.transform, "Cancel", 135f, CloseLimitPopup);

            _limitPopup.SetActive(false);
        }

        private static void CreatePopupButton(Transform parent, string text, float x, UnityEngine.Events.UnityAction onClick)
        {
            var go = GUIManager.Instance.CreateButton(
                text: text,
                parent: parent,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position: new Vector2(x, 35f),
                width: 125f,
                height: 40f);
            go.SetActive(true);
            go.GetComponent<Button>().onClick.AddListener(onClick);
        }

        private static void OpenLimitPopup(CellView view)
        {
            if (!_limitPopup)
                return;

            _limitTarget = view;
            _limitPopupTitle.text = $"Keep up to how many\n{view.DisplayName}?";
            int limit = LootFilterService.GetQuantityLimit(view.PrefabName);
            _limitField.text = limit > 0 ? limit.ToString() : "";

            _limitPopup.SetActive(true);
            _limitPopup.transform.SetAsLastSibling();
            _limitField.Select();
            _limitField.ActivateInputField();
        }

        private static void SaveLimitPopup()
        {
            if (_limitTarget != null)
            {
                int.TryParse(_limitField.text, out int limit);
                LootFilterService.SetQuantityLimit(_limitTarget.PrefabName, limit);
                RefreshCell(_limitTarget);
                Jotunn.Logger.LogInfo($"[LootFilter] {_limitTarget.PrefabName} limit={limit}");
            }

            CloseLimitPopup();
        }

        private static void CloseLimitPopup()
        {
            _limitTarget = null;
            if (_limitField)
                _limitField.DeactivateInputField();
            if (_limitPopup)
                _limitPopup.SetActive(false);
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

    /// <summary>Calls OnRightClick when the cell is right-clicked (Button only handles left).</summary>
    internal class RightClickHandler : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
                OnRightClick?.Invoke();
        }
    }

    internal static class GameObjectExtensions
    {
        public static T GetOrAddComponent<T>(this GameObject go) where T : Component
            => go.GetComponent<T>() ?? go.AddComponent<T>();
    }

}
