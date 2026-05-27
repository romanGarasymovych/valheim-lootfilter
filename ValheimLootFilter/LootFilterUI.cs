using Jotunn.GUI;
using Jotunn.Managers;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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

        //  Sprite cache so we only load each icon once 
        private static readonly Dictionary<string, Sprite> _iconCache = new();

        //  Colours 
        private static readonly Color ColNormal = new Color(1f, 1f, 1f, 1f);   // white  — not excluded
        private static readonly Color ColExcluded = new Color(1f, 0.2f, 0.2f, 1f); // red    — excluded

        //  Layout constants 
        private const float PanelW = 1050f;
        private const float PanelH = 800f;
        private const float ScrollW = 970f;
        private const float ScrollH = 650f;
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

            bool next = !_filtersPanel.activeSelf;
            _filtersPanel.SetActive(next);
            GUIManager.BlockInput(next);

            if (next)
                RefreshContent();
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
                text: "Click an item to toggle auto-pickup exclusion  [ red = excluded ]",
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

            //  Scroll view 
            // Position it so there's room for the title above and Close below.
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
            svRT.anchoredPosition = new Vector2(0f, -80f);   // below title + legend

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

            var allItems = LootFilterService.GetAllItems();
            Jotunn.Logger.LogInfo($"Found {allItems.Count} total items in ObjectDB");

            // Filter to only items the player has discovered.
            var knownNames = Player.m_localPlayer != null
                ? Player.m_localPlayer.m_knownMaterial
                : new HashSet<string>();

            Jotunn.Logger.LogInfo($"{knownNames.Count} items are known");
            foreach (var name in knownNames)
            {
                Jotunn.Logger.LogInfo($"Known item: {name}");
            }
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

            // Force layout rebuild so the scroll view height is correct
            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot as RectTransform);
        }

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
                CreateItemCell(grid.transform, item);
        }

        //  Single icon cell 
        private static void CreateItemCell(Transform parent, ItemDrop item)
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

            // A tiny name label at the bottom of the cell
            var label = GUIManager.Instance.CreateText(
                text: displayName,
                parent: cell.transform,
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 0f),
                position: Vector2.zero,
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 7,
                color: Color.white,
                outline: true,
                outlineColor: Color.black,
                width: CellSize,
                height: 14f,
                addContentSizeFitter: false);

            var labelRT = label.GetComponent<RectTransform>();
            labelRT.pivot = new Vector2(0.5f, 0f);
            labelRT.anchoredPosition = new Vector2(0f, 0f);

            var labelText = label.GetComponent<Text>();
            if (labelText)
            {
                labelText.alignment = TextAnchor.MiddleCenter;
                labelText.resizeTextForBestFit = true;
                labelText.resizeTextMinSize = 5;
                labelText.resizeTextMaxSize = 9;
            }

            //  Button click handler 
            // Capture locals for the closure
            string itemName = item.name;
            Image capturedIcon = iconImg;
            GameObject capturedX = xOverlay;

            btn.onClick.AddListener(() =>
            {
                bool nowExcluded = LootFilterService.ToggleExcluded(itemName);
                capturedIcon.color = nowExcluded ? ColExcluded : ColNormal;
                capturedX.SetActive(nowExcluded);
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

    internal static class GameObjectExtensions
    {
        public static T GetOrAddComponent<T>(this GameObject go) where T : Component
            => go.GetComponent<T>() ?? go.AddComponent<T>();
    }

}
