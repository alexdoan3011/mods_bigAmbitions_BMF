using System.Collections.Generic;
using BAModAPI;
using City.CityMap;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BetterMapFilter
{
    /// <summary>
    /// Runtime driver for Better Map Filter.
    ///
    /// Lives on a persistent GameObject. When the City Map filter panel
    /// (<see cref="CityMapFilters"/>) appears it:
    ///   1. re-lays each category's switches into a 3-column grid of square cards,
    ///   2. turns each switch into a square, slightly rounded button that
    ///      highlights while it is on, with the icon centered and the label below.
    /// </summary>
    public class CityMapFilterEnhancer : MonoBehaviour
    {
        // Grid layout.
        private const int Columns = 3;
        private const float SpacingX = 6f;
        private const float SpacingY = 6f;

        // Corner radius (in sprite pixels) for the generated rounded card sprite.
        private const int CardSpriteSize = 64;
        private const int CardCornerRadius = 16;

        // Size/inset of the native "jump to district" button lifted onto a card.
        private const float FocusButtonSize = 40f;
        private const float FocusButtonInset = 8f;

        // Highlight tint applied to a card's background when its filter is on/off.
        private static readonly Color OnColor = new(0.20f, 0.55f, 1f, 0.85f);
        private static readonly Color OffColor = new(1f, 1f, 1f, 0.08f);

        // Set right after AddComponent (MonoBehaviours can't take ctor args).
        private ModContext _context;

        // The live game panel and our view of its contents.
        private CityMapFilters _panel;
        private readonly List<CityMapFilter> _rows = new();
        private readonly List<Section> _sections = new();
        private readonly List<GridLayoutGroup> _grids = new();
        private readonly List<Card> _cards = new();
        private bool _gridBuilt;
        private bool _tornDown;

        // Cached generated rounded sprite for the card backgrounds.
        private Sprite _roundedSprite;

        private float _nextScan;

        /// <summary>A category header plus the switch rows that belong to it.</summary>
        private sealed class Section
        {
            public CityMapFilterCategory Category;
            public GameObject Header;
            public readonly List<CityMapFilter> Filters = new();
        }

        /// <summary>A single switch restyled as a highlight-on button card.</summary>
        private sealed class Card
        {
            public CityMapFilter Filter;
            public Image Highlight;
            public UnityAction<bool> ToggleListener;
        }

        public void Configure(ModContext context)
        {
            _context = context;
        }

        private ModContext Context => _context;

        private void Update()
        {
            if (_tornDown)
                return;

            // Cheap periodic scan until we've hooked the panel, then re-check in
            // case the player re-enters a save (panel rebuilt).
            if (Time.unscaledTime < _nextScan)
                return;
            _nextScan = Time.unscaledTime + 1f;

            if (_panel == null)
                TryAttach();
        }

        private void TryAttach()
        {
            var panel = FindObjectOfType<CityMapFilters>(true);
            if (panel == null)
                return;

            _panel = panel;
            _gridBuilt = false;
            RefreshRows();

            if (_rows.Count == 0)
            {
                // Panel exists but rows not built yet; retry next scan.
                _panel = null;
                return;
            }

            BuildSections();
            BuildGrid();

            Context?.Logger.Info(
                $"BetterMapFilter attached: {_rows.Count} switches across {_sections.Count} categories.");
        }

        private void RefreshRows()
        {
            _rows.Clear();
            if (_panel == null)
                return;

            foreach (var row in _panel.GetComponentsInChildren<CityMapFilter>(true))
            {
                // The "toggle all" row and the inactive prefab template have no
                // category assigned; skip them.
                if (row.category == null)
                    continue;
                _rows.Add(row);
            }
        }

        // -- Grid construction ----------------------------------------------

        /// <summary>
        /// Walks the scroll content in display order, grouping each category
        /// header with the switch rows that follow it.
        /// </summary>
        private void BuildSections()
        {
            _sections.Clear();
            if (_rows.Count == 0)
                return;

            // All real switch rows share the same parent: the scroll "Content".
            var content = _rows[0].transform.parent;
            Section current = null;

            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);

                // Skip the inactive prefab templates ("Entry" / "LineEntry").
                if (child.name == "Entry" || child.name == "LineEntry")
                    continue;

                var category = child.GetComponent<CityMapFilterCategory>();
                if (category != null)
                {
                    current = new Section { Category = category, Header = child.gameObject };
                    _sections.Add(current);
                    continue;
                }

                var filter = child.GetComponent<CityMapFilter>();
                if (filter != null && filter.category != null && current != null)
                    current.Filters.Add(filter);
            }
        }

        /// <summary>
        /// Inserts a <see cref="GridLayoutGroup"/> container after each category
        /// header, moves that category's switches into it, and restyles each one
        /// as a highlight-on button card.
        /// </summary>
        private void BuildGrid()
        {
            _grids.Clear();
            _cards.Clear();
            if (_sections.Count == 0)
                return;

            var content = _rows[0].transform.parent as RectTransform;
            float contentWidth = content != null ? content.rect.width : 640f;
            if (contentWidth <= 1f)
                contentWidth = 640f;

            float cellWidth = (contentWidth - SpacingX * (Columns - 1)) / Columns;
            float cellHeight = cellWidth; // square cards

            int focusButtons = 0;
            foreach (var section in _sections)
            {
                var gridGo = new GameObject("BMF_Grid", typeof(RectTransform));
                var gridRect = (RectTransform)gridGo.transform;
                gridRect.SetParent(content, false);

                // Place the grid immediately after its header in the layout order.
                gridRect.SetSiblingIndex(section.Header.transform.GetSiblingIndex() + 1);

                var grid = gridGo.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(cellWidth, cellHeight);
                grid.spacing = new Vector2(SpacingX, SpacingY);
                grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                grid.startAxis = GridLayoutGroup.Axis.Horizontal;
                grid.childAlignment = TextAnchor.UpperLeft;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = Columns;
                _grids.Add(grid);

                // Size the container to its grid content so the parent vertical
                // layout reserves the right amount of space (and shrinks when the
                // category is collapsed or filtered down to nothing).
                var fitter = gridGo.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                foreach (var filter in section.Filters)
                {
                    filter.transform.SetParent(gridRect, false);
                    if (BuildCard(filter))
                        focusButtons++;
                }
            }

            Context?.Logger.Info(
                $"BetterMapFilter: built {_cards.Count} cards, {focusButtons} district focus buttons.");

            _gridBuilt = true;
        }

        /// <summary>
        /// Restyles a single switch row into a button card: a tinted background
        /// that lights up while the filter is on, the icon centered on top, and
        /// the label centered below. The original game <c>Toggle</c> is kept as
        /// the source of truth but hidden; clicking the card flips it. Returns
        /// <c>true</c> if the row carried a district focus button lifted onto it.
        /// </summary>
        private bool BuildCard(CityMapFilter filter)
        {
            var root = filter.gameObject;

            // Background image on the card root = the highlight + the click target.
            // A generated rounded sprite gives clearly rounded corners at any size.
            var highlight = root.GetComponent<Image>();
            if (highlight == null)
                highlight = root.AddComponent<Image>();
            highlight.sprite = GetRoundedSprite();
            highlight.type = Image.Type.Sliced;
            highlight.pixelsPerUnitMultiplier = 1f;
            highlight.raycastTarget = true;

            // We position children manually (anchors), so remove any layout group
            // that an earlier build may have added to the root.
            var oldVlg = root.GetComponent<VerticalLayoutGroup>();
            if (oldVlg != null)
                Destroy(oldVlg);

            // Locate the row's parts (names confirmed from the live hierarchy).
            Transform horizontal = root.transform.Find("Horizontal");
            Transform iconTf = root.transform.Find("Icon");
            Transform toggleTf = filter.Toggle != null ? filter.Toggle.transform : root.transform.Find("Toggle");
            var labelText = root.GetComponentInChildren<TMP_Text>(true);
            GameObject labelGo = labelText != null ? labelText.gameObject : null;

            // Icon: centered in the card, sitting just above the label.
            if (iconTf is RectTransform iconRect)
            {
                iconTf.SetParent(root.transform, false);
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.sizeDelta = new Vector2(72f, 72f);
                iconRect.anchoredPosition = new Vector2(0f, 16f);

                var iconImg = iconTf.GetComponent<Image>();
                if (iconImg != null)
                    iconImg.raycastTarget = false;
            }

            // Label: pinned across the bottom of the card, centered, always shown.
            if (labelGo != null)
            {
                labelGo.transform.SetParent(root.transform, false);

                if (labelGo.GetComponent<RectTransform>() is RectTransform labelRect)
                {
                    labelRect.anchorMin = new Vector2(0f, 0f);
                    labelRect.anchorMax = new Vector2(1f, 0f);
                    labelRect.pivot = new Vector2(0.5f, 0f);
                    labelRect.sizeDelta = new Vector2(-8f, 38f);
                    labelRect.anchoredPosition = new Vector2(0f, 6f);
                }

                labelText.alignment = TextAlignmentOptions.Center;
                labelText.enableWordWrapping = true;
                labelText.overflowMode = TextOverflowModes.Ellipsis;
                labelText.raycastTarget = false;
                labelText.enableAutoSizing = true;
                labelText.fontSizeMin = 10f;
                labelText.fontSizeMax = 18f;

                var labelLayout = labelGo.GetComponent<LayoutElement>();
                if (labelLayout != null)
                    labelLayout.ignoreLayout = true;

                labelGo.SetActive(true);
            }

            // Hide the original switch graphics but keep the Toggle component alive
            // (it stores the on/off state). Make it non-interactable so it never
            // competes with the card for the click.
            if (toggleTf != null)
            {
                var toggleLayout = toggleTf.GetComponent<LayoutElement>();
                if (toggleLayout == null)
                    toggleLayout = toggleTf.gameObject.AddComponent<LayoutElement>();
                toggleLayout.ignoreLayout = true;

                foreach (var g in toggleTf.GetComponentsInChildren<Graphic>(true))
                    g.enabled = false;
            }

            if (filter.Toggle != null)
                filter.Toggle.interactable = false;

            // District (neighborhood) switches carry a native "jump there" button.
            // Lift it onto the card before we hide the row it normally lives in.
            bool hasFocusButton = SetUpFocusButton(root, horizontal);

            // Drop the original horizontal row (held the label + focus button).
            if (horizontal != null && horizontal != labelGo?.transform)
                horizontal.gameObject.SetActive(false);

            // Card button. CRITICAL: also disable any *persistent* (prefab-wired)
            // onClick listeners — RemoveAllListeners only clears runtime ones, so
            // a leftover persistent handler would flip the toggle a second time
            // (symptom: click sound but no net change).
            var button = root.GetComponent<Button>();
            if (button == null)
                button = root.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = highlight;
            int persistentClicks = button.onClick.GetPersistentEventCount();
            for (int i = 0; i < persistentClicks; i++)
                button.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            button.onClick.RemoveAllListeners();
            var capturedFilter = filter;
            button.onClick.AddListener(() => OnCardClicked(capturedFilter));

            var card = new Card
            {
                Filter = filter,
                Highlight = highlight,
            };
            card.ToggleListener = _ => UpdateHighlight(card);
            if (filter.Toggle != null)
                filter.Toggle.onValueChanged.AddListener(card.ToggleListener);
            UpdateHighlight(card);

            _cards.Add(card);
            return hasFocusButton;
        }

        /// <summary>
        /// District rows expose a native focus button (active only when the
        /// filter has a focus point) that jumps the city camera to that district.
        /// The game nests it inside the row's "Horizontal" group, which the card
        /// layout disables, so for any row that has it active we reparent it onto
        /// the card as a small top-right button. Its original onClick (the game's
        /// <c>OnFocusButtonClick</c>) is left untouched, and because the button
        /// handles its own pointer click, jumping to a district never also flips
        /// the filter toggle. Returns <c>true</c> when a button was attached.
        /// </summary>
        private static bool SetUpFocusButton(GameObject root, Transform horizontal)
        {
            if (horizontal == null)
                return false;

            Transform focusTf = horizontal.Find("FocusButton");
            if (focusTf == null || !focusTf.gameObject.activeSelf)
                return false; // non-district rows keep this button inactive

            // Move it onto the card so it survives the row group being hidden.
            focusTf.SetParent(root.transform, false);
            focusTf.gameObject.SetActive(true);

            if (focusTf is RectTransform focusRect)
            {
                focusRect.anchorMin = new Vector2(1f, 1f);
                focusRect.anchorMax = new Vector2(1f, 1f);
                focusRect.pivot = new Vector2(1f, 1f);
                focusRect.sizeDelta = new Vector2(FocusButtonSize, FocusButtonSize);
                focusRect.anchoredPosition = new Vector2(-FocusButtonInset, -FocusButtonInset);
            }

            // Render and raycast on top of the card background.
            focusTf.SetAsLastSibling();

            var focusLayout = focusTf.GetComponent<LayoutElement>();
            if (focusLayout != null)
                focusLayout.ignoreLayout = true;

            // The button's icon normally lives in a child ("Background") whose size
            // is driven by the now-removed row layout, so it can collapse to 0x0.
            // Stretch every child to fill the button and make sure its graphic is
            // visible, otherwise the lifted button would be invisible.
            foreach (var childRect in focusTf.GetComponentsInChildren<RectTransform>(true))
            {
                if (childRect == focusTf)
                    continue;
                childRect.anchorMin = Vector2.zero;
                childRect.anchorMax = Vector2.one;
                childRect.offsetMin = Vector2.zero;
                childRect.offsetMax = Vector2.zero;
            }

            bool anyVisible = false;
            foreach (var g in focusTf.GetComponentsInChildren<Graphic>(true))
            {
                g.enabled = true;
                if (g.color.a > 0f)
                    anyVisible = true;
            }

            // Fallback: if nothing visible was found, drop a simple icon image on
            // the button so the player still sees something to click.
            if (!anyVisible)
            {
                var img = focusTf.GetComponent<Image>();
                if (img == null)
                    img = focusTf.gameObject.AddComponent<Image>();
                img.enabled = true;
                img.color = new Color(1f, 1f, 1f, 0.9f);
                img.raycastTarget = true;
            }

            return true;
        }

        private static void UpdateHighlight(Card card)
        {
            if (card.Highlight == null || card.Filter == null || card.Filter.Toggle == null)
                return;
            var target = card.Filter.Toggle.isOn ? OnColor : OffColor;
            if (card.Highlight.color != target)
                card.Highlight.color = target;
        }

        /// <summary>
        /// Flips the underlying filter exactly once and runs the game's own logic.
        /// We update the toggle state without notifying, then call the game's
        /// public <c>OnToggleClick</c> directly — this avoids depending on however
        /// the prefab wired the toggle's onValueChanged and guarantees the map
        /// refreshes (and only once).
        /// </summary>
        private void OnCardClicked(CityMapFilter filter)
        {
            if (filter == null || filter.Toggle == null)
                return;

            bool newValue = !filter.Toggle.isOn;
            filter.Toggle.SetIsOnWithoutNotify(newValue);
            filter.OnToggleClick(newValue);
        }

        /// <summary>
        /// Builds (once) a white, rounded-rectangle 9-sliced sprite used as the
        /// card background. Tinted at runtime via the Image color.
        /// </summary>
        private Sprite GetRoundedSprite()
        {
            if (_roundedSprite != null)
                return _roundedSprite;

            int size = CardSpriteSize;
            int radius = CardCornerRadius;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Nearest point inside the rounded-corner region.
                    float cx = Mathf.Clamp(x, radius, size - 1 - radius);
                    float cy = Mathf.Clamp(y, radius, size - 1 - radius);
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            var border = new Vector4(radius, radius, radius, radius);
            _roundedSprite = Sprite.Create(
                tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);
            return _roundedSprite;
        }

        // -- Per-frame highlight sync ---------------------------------------
        // Keeps each card's highlight in step with its filter, covering changes
        // the game makes on its own (toggle-all, loading a save, etc.).
        private void LateUpdate()
        {
            if (_tornDown || !_gridBuilt)
                return;

            for (int i = 0; i < _cards.Count; i++)
                UpdateHighlight(_cards[i]);
        }

        public void Teardown()
        {
            // Teardown runs while the game is unloading/quitting, so guard
            // everything: a thrown exception here fails the mod's OnUnloadAsync
            // and can wedge the whole game shutdown.
            _tornDown = true;
            try
            {
                foreach (var card in _cards)
                {
                    if (card.Filter != null && card.Filter.Toggle != null && card.ToggleListener != null)
                        card.Filter.Toggle.onValueChanged.RemoveListener(card.ToggleListener);
                }
            }
            catch (System.Exception e)
            {
                Context?.Logger.Info($"BetterMapFilter: ignored teardown error ({e.Message}).");
            }

            // NOTE: deliberately do NOT call _panel.ApplyFilters() here. During
            // shutdown the game's CityManager is already being destroyed and that
            // call throws a NullReferenceException. Our changes live on objects
            // that Unity destroys with the scene anyway, so no restore is needed.

            _cards.Clear();
        }
    }
}

