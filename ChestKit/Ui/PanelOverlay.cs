using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace JoksterCube.ChestKit.Ui;

// A mod's page shown in the container panel itself, in place of its items: made over the panel, with the panel's own
// items, buttons and title hidden while it shows. Helpers to line things up with the panel's "Take all" row and to
// make scrolling lists that look and scroll like the game's.
public static class PanelOverlay
{
    private const float FallbackMargin = 16f;
    private const float FallbackRowHeight = 40f;
    private const float FallbackButtonWidth = 150f;
    private const float FallbackScrollSensitivity = 40f;
    private const float FallbackScrollbarWidth = 10f;
    private const float ScrollbarSpacing = 4f;
    private static readonly Vector3[] Corners = new Vector3[4];

    // Every mod's page is named "ChestKit_Page|modId", so pages of mods built on different ChestKit copies find each
    // other. Keep that format if ChestKit changes.
    public const string PageNamePrefix = "ChestKit_Page|";
    // Names the markers left on parts of the panel a page hides (see HiddenVanilla).
    public const string HiddenMarkerPrefix = "ChestKit_Hidden|";

    // An empty page filling the container panel, hidden until shown. modId: the mod's name, e.g. "FuelChests".
    public static RectTransform Create(InventoryGui gui, string modId)
    {
        GameObject overlayObject = new(PageNamePrefix + modId, typeof(RectTransform));
        overlayObject.transform.SetParent(gui.m_container, false);
        RectTransform overlay = (RectTransform)overlayObject.transform;
        UiParts.Stretch(overlay, 0f);
        overlayObject.SetActive(false);
        return overlay;
    }

    // Puts a page in front when it opens: the newest page is the last one among the panel's children.
    public static void BringToFront(RectTransform overlay) => overlay.SetAsLastSibling();

    // Whether another mod's page was opened over this one since. Only one page shows at a time: the older one closes
    // itself when it sees this (call every frame while open), so clicking from one mod's flap to another's swaps pages.
    public static bool IsCovered(RectTransform overlay)
    {
        Transform? parent = overlay.parent;
        if (!parent) return false;
        int mine = overlay.GetSiblingIndex();
        foreach (Transform sibling in parent!)
        {
            if (sibling == overlay || !sibling.gameObject.activeSelf) continue;
            if (sibling.name.StartsWith(PageNamePrefix, System.StringComparison.Ordinal) && sibling.GetSiblingIndex() > mine) return true;
        }
        return false;
    }

    // The container panel's own items, buttons and title, hidden while a page shows. They are made invisible and
    // unclickable rather than turned off, so the game keeps updating them as usual and they come back exactly as
    // they were.
    //
    // Several mods' pages may hide the same parts, opening and closing in any order. Each leaves a marker (an empty,
    // inactive child named "ChestKit_Hidden|modId|alpha|blocksRaycasts|interactable") holding how the part was before
    // any page hid it, and a part only comes back once the last marker is gone. Keep that format if ChestKit changes.
    public sealed class HiddenVanilla(string modId)
    {
        private const string MarkerPrefix = HiddenMarkerPrefix;
        private const char Separator = '|';

        private readonly string _owner = modId.Replace(Separator, '_');
        private readonly List<GameObject> _hidden = [];

        public void Hide(InventoryGui gui)
        {
            Restore();
            if (gui.m_containerGrid)
            {
                // Mods that make the panel bigger may put the grid in a scroll view: that is hidden with it.
                ScrollRect? scroll = gui.m_containerGrid.GetComponentInParent<ScrollRect>();
                bool scrollInPanel = scroll && scroll!.transform.IsChildOf(gui.m_container) && scroll.transform != gui.m_container;
                HideOne(scrollInPanel ? scroll!.gameObject : gui.m_containerGrid.gameObject);
                if (gui.m_containerGrid.m_scrollbar && !gui.m_containerGrid.m_scrollbar.transform.IsChildOf(gui.m_containerGrid.transform))
                    HideOne(gui.m_containerGrid.m_scrollbar.gameObject);
            }
            if (gui.m_takeAllButton) HideOne(gui.m_takeAllButton.gameObject);
            if (gui.m_stackAllButton) HideOne(gui.m_stackAllButton.gameObject);
            if (gui.m_containerName) HideOne(gui.m_containerName.gameObject);
        }

        public void Restore()
        {
            foreach (GameObject part in _hidden)
                if (part) RestoreOne(part);
            _hidden.Clear();
        }

        private void HideOne(GameObject part)
        {
            if (!part.TryGetComponent(out CanvasGroup group)) group = part.AddComponent<CanvasGroup>();
            // How it was before any page hid it: from another page's marker, or as it is now.
            string original = FirstMarker(part, out Transform? marker) && marker!.name.Split(Separator) is { Length: 5 } parts
                ? string.Join(Separator.ToString(), parts[2], parts[3], parts[4])
                : string.Join(Separator.ToString(), group.alpha.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    group.blocksRaycasts ? "1" : "0", group.interactable ? "1" : "0");

            GameObject mark = new($"{MarkerPrefix}{_owner}{Separator}{original}");
            mark.SetActive(false);
            mark.transform.SetParent(part.transform, false);
            _hidden.Add(part);

            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        private void RestoreOne(GameObject part)
        {
            string mine = $"{MarkerPrefix}{_owner}{Separator}";
            string? original = null;
            bool othersLeft = false;
            foreach (Transform child in part.transform)
            {
                if (!child.name.StartsWith(MarkerPrefix, System.StringComparison.Ordinal)) continue;
                if (child.name.StartsWith(mine, System.StringComparison.Ordinal))
                {
                    original ??= child.name.Substring(mine.Length);
                    // Destroying only happens at the end of the frame, so it is renamed now to stop counting.
                    child.name = "ChestKit_Removed";
                    Object.Destroy(child.gameObject);
                }
                else othersLeft = true;
            }
            if (othersLeft || original == null || !part.TryGetComponent(out CanvasGroup group)) return;

            string[] values = original.Split(Separator);
            group.alpha = values.Length > 0 && float.TryParse(values[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float alpha) ? alpha : 1f;
            group.blocksRaycasts = values.Length <= 1 || values[1] == "1";
            group.interactable = values.Length <= 2 || values[2] == "1";
        }

        private static bool FirstMarker(GameObject part, out Transform? marker)
        {
            foreach (Transform child in part.transform)
                if (child.name.StartsWith(MarkerPrefix, System.StringComparison.Ordinal))
                {
                    marker = child;
                    return true;
                }
            marker = null;
            return false;
        }
    }

    // Where the panel's "Take all" button is, in the page's own coordinates: the row a page's tabs or title go in.
    public readonly struct HeaderRow(float left, float top, float height, float buttonWidth, float margin)
    {
        // Left edge and top of the row.
        public float Left { get; } = left;
        public float Top { get; } = top;
        public float Height { get; } = height;
        // The "Take all" button's width.
        public float ButtonWidth { get; } = buttonWidth;
        // Between the panel's edge and the row.
        public float Margin { get; } = margin;
        public float CenterY => Top - Height / 2f;
    }

    public static HeaderRow Header(InventoryGui gui, RectTransform overlay)
    {
        Rect panel = overlay.rect;
        float margin = FallbackMargin;
        float rowHeight = FallbackRowHeight;
        float buttonWidth = FallbackButtonWidth;
        float left = panel.xMin + margin;
        float rowTop = panel.yMax - margin;

        if (gui.m_takeAllButton)
        {
            ((RectTransform)gui.m_takeAllButton.transform).GetWorldCorners(Corners);
            Vector3 min = overlay.InverseTransformPoint(Corners[0]);
            Vector3 max = overlay.InverseTransformPoint(Corners[2]);
            if (max.x > min.x && max.y > min.y)
            {
                left = min.x;
                rowTop = max.y;
                rowHeight = max.y - min.y;
                buttonWidth = max.x - min.x;
                margin = Mathf.Max(4f, min.x - panel.xMin);
            }
        }
        return new HeaderRow(left, rowTop, rowHeight, buttonWidth, margin);
    }

    // The size of the panel's "Take all" button, for buttons that should look like it.
    public static Vector2 ButtonSize(InventoryGui gui)
    {
        Vector2 size = gui.m_takeAllButton ? ((RectTransform)gui.m_takeAllButton.transform).rect.size : Vector2.zero;
        return size.x > 0f && size.y > 0f ? size : new Vector2(FallbackButtonWidth, FallbackRowHeight);
    }

    // The page's close button: an "X" like the panel's own buttons, with a tooltip. Put it in place with PlaceClose.
    public static UiParts.TextButton CreateCloseButton(InventoryGui gui, RectTransform overlay, UnityEngine.Events.UnityAction onClose, string tooltip)
    {
        UiParts.TextButton close = UiParts.CreateTextButton(gui, overlay, "Close", "X", onClose);
        UITooltip? closeTooltip = UiParts.AddTooltip(gui, close.Button.gameObject);
        if (closeTooltip)
        {
            closeTooltip!.m_topic = "";
            closeTooltip.m_text = tooltip;
        }
        return close;
    }

    // At the top right corner, as tall as the "Take all" row and with the same margin from the panel's edge.
    // Returns the close button's left edge, so a title or tabs can stop short of it.
    public static float PlaceClose(UiParts.TextButton close, HeaderRow header, Rect panel)
    {
        float left = panel.xMax - header.Margin - header.Height;
        Put(close.Rect, new Vector2(left + header.Height / 2f, header.CenterY), new Vector2(header.Height, header.Height), panel);
        return left;
    }

    // A page's tabs: buttons like the panel's "Take all", side by side in its row from its left edge to right (the close
    // button's left edge), each as wide as "Take all" or narrower to fit.
    public static void PlaceTabs(IReadOnlyList<UiParts.TextButton> tabs, HeaderRow header, float right, Rect panel, float gap = 8f)
    {
        int count = tabs.Count;
        if (count == 0) return;
        float room = right - gap - header.Left;
        float width = Mathf.Max(40f, Mathf.Min(header.ButtonWidth, (room - gap * (count - 1)) / count));
        for (int i = 0; i < count; i++)
            Put(tabs[i].Rect, new Vector2(header.Left + width / 2f + i * (width + gap), header.CenterY), new Vector2(width, header.Height), panel);
    }

    // The selected tab keeps the game's bright label; the others are dimmed.
    public static void SelectTab(IReadOnlyList<UiParts.TextButton> tabs, int selected)
    {
        for (int i = 0; i < tabs.Count; i++) tabs[i].SetSelected(i == selected);
    }

    // Places a center-anchored child by its center in the page's own coordinates.
    public static void Put(RectTransform rect, Vector2 center, Vector2 size, Rect panel)
    {
        Vector2 position = center - panel.center;
        if (rect.anchoredPosition != position) rect.anchoredPosition = position;
        if (rect.sizeDelta != size) rect.sizeDelta = size;
    }

    // A vertical scroll view filling the area, returning the content to fill; content grows with what is put in it.
    // It scrolls as fast as the game's own lists and shows a copy of the container's scroll bar when needed.
    public static RectTransform ScrollContent(InventoryGui gui, RectTransform area, float sensitivity)
    {
        RectTransform viewport = UiParts.Child(area, "Viewport", 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        // Catches the mouse wheel over empty space.
        Image catcher = viewport.gameObject.AddComponent<Image>();
        catcher.color = Color.clear;

        GameObject contentObject = new("Items", typeof(RectTransform));
        contentObject.transform.SetParent(viewport, false);
        RectTransform content = (RectTransform)contentObject.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        ScrollRect scroll = area.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = false;
        scroll.scrollSensitivity = sensitivity;

        Scrollbar? bar = CopyScrollbar(gui, area);
        if (bar)
        {
            scroll.verticalScrollbar = bar;
            // The bar's room is always kept, shown or not, so nothing moves when it comes and goes.
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            viewport.offsetMax = new Vector2(-(((RectTransform)bar!.transform).sizeDelta.x + ScrollbarSpacing), 0f);
        }
        return content;
    }

    // The scroll bar of the container's grid (shown for big chests), on the right side of the area.
    public static Scrollbar? CopyScrollbar(InventoryGui gui, RectTransform area)
    {
        Scrollbar? source = gui.m_containerGrid && gui.m_containerGrid.m_scrollbar ? gui.m_containerGrid.m_scrollbar
            : gui.m_playerGrid && gui.m_playerGrid.m_scrollbar ? gui.m_playerGrid.m_scrollbar
            : gui.m_recipeListScroll;
        if (!source) return null;

        GameObject copy = Object.Instantiate(source!.gameObject, area);
        copy.name = "Scrollbar";
        UiParts.RemoveHints(copy);
        Scrollbar bar = copy.GetComponent<Scrollbar>();
        bar.onValueChanged = new Scrollbar.ScrollEvent();
        bar.direction = Scrollbar.Direction.BottomToTop;

        float width = ((RectTransform)source.transform).rect.width;
        if (width <= 0f) width = FallbackScrollbarWidth;
        RectTransform rect = (RectTransform)copy.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width, 0f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        UiParts.Unhide(copy);
        copy.SetActive(true);
        return bar;
    }

    // How fast the game's own lists scroll (the recipe list, or any other in the inventory screen). Scroll views inside
    // ignore (a mod's own page) don't count.
    public static float ScrollSensitivity(InventoryGui gui, Transform? ignore = null)
    {
        float best = 0f;
        foreach (ScrollRect scroll in gui.GetComponentsInChildren<ScrollRect>(true))
        {
            if (ignore && scroll.transform.IsChildOf(ignore!)) continue;
            if (gui.m_recipeListScroll && scroll.verticalScrollbar == gui.m_recipeListScroll) return scroll.scrollSensitivity;
            best = Mathf.Max(best, scroll.scrollSensitivity);
        }
        return best > 1f ? best : FallbackScrollSensitivity;
    }
}
