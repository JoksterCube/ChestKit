using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace JoksterCube.ChestKit.Ui;

// Building blocks for the mod's UI, made from copies of the game's own UI so they look like the game's.
public static class UiParts
{
    public const float IconPadding = 7f;
    private const float CrossThickness = 4f;
    // Of the button's size, on each side.
    private const float CrossInsetPart = 0.25f;
    private static readonly Color CrossColor = new(0.9f, 0.15f, 0.1f, 1f);
    private static readonly Color CrossEdge = new(0.1f, 0.05f, 0.03f, 0.85f);
    private static readonly Color FallbackButton = new(0.15f, 0.12f, 0.1f, 0.9f);

    // A square button with an icon and a red cross that can be shown over it.
    public sealed class IconButton(Button button, Image icon, GameObject cross, UITooltip? tooltip)
    {
        public Button Button { get; } = button;
        public Image Icon { get; } = icon;
        public GameObject Cross { get; } = cross;
        public UITooltip? Tooltip { get; } = tooltip;
        // Where the tooltip shows, like an inventory item's card; null: at the pointer.
        public RectTransform? TooltipAnchor { get; set; }
        public RectTransform Rect => (RectTransform)Button.transform;

        public void SetCrossed(bool crossed)
        {
            if (Cross.activeSelf != crossed) Cross.SetActive(crossed);
        }

        // Also updates the tooltip while it is shown.
        public void SetTooltip(string topic, string text)
        {
            if (!Tooltip || (Tooltip!.m_topic == topic && Tooltip.m_text == text)) return;
            Tooltip.Set(topic, text, TooltipAnchor);
        }
    }

    // A button with a text label, like the container's "Take all" button. The selected one of a group keeps the
    // game's bright label; the others are dimmed.
    public sealed class TextButton
    {
        private const float DimmedPart = 0.55f;
        private readonly Color _labelColor;

        public Button Button { get; }
        public TMP_Text? Label { get; }
        public RectTransform Rect => (RectTransform)Button.transform;

        public TextButton(Button button, TMP_Text? label)
        {
            Button = button;
            Label = label;
            _labelColor = label ? label!.color : Color.white;
        }

        public void SetSelected(bool selected)
        {
            if (!Label) return;
            Color color = selected
                ? _labelColor
                : new Color(_labelColor.r * DimmedPart, _labelColor.g * DimmedPart, _labelColor.b * DimmedPart, _labelColor.a);
            if (Label!.color != color) Label.color = color;
        }
    }

    // A copy of the crafting tab button with its label swapped for an icon. The tooltip is the craft button's, or the
    // given one (such as an inventory item's card).
    public static IconButton CreateIconButton(InventoryGui gui, Transform parent, string name, float size, UnityAction onClick,
        GameObject? tooltipPrefab = null)
    {
        GameObject root = CopyButton(gui, gui.m_tabCraft, parent, name, keepLabel: false, out _);
        RectTransform rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(size, size);

        Image icon = Child(root.transform, "Icon", IconPadding).gameObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        GameObject cross = CreateCross(root.transform, size);
        cross.SetActive(false);

        Button button = root.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        return new IconButton(button, icon, cross, AddTooltip(gui, root, tooltipPrefab));
    }

    // A copy of the container's "Take all" button with its own label.
    public static TextButton CreateTextButton(InventoryGui gui, Transform parent, string name, string text, UnityAction onClick)
    {
        Button? source = gui.m_takeAllButton ? gui.m_takeAllButton : gui.m_tabCraft;
        GameObject root = CopyButton(gui, source, parent, name, keepLabel: true, out TMP_Text? label);
        if (label)
        {
            label!.text = text;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
        Button button = root.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        return new TextButton(button, label);
    }

    private static GameObject CopyButton(InventoryGui gui, Button? source, Transform parent, string name, bool keepLabel, out TMP_Text? label)
    {
        label = null;
        GameObject root;
        if (source)
        {
            root = Object.Instantiate(source!.gameObject, parent);
            RemoveHints(root);
            // It recolors the first text it finds every frame; labels are colored here instead.
            foreach (ButtonTextColor textColor in root.GetComponentsInChildren<ButtonTextColor>(true)) Object.Destroy(textColor);
            if (keepLabel)
            {
                // Everything but the hints stays, so the button keeps the game's look.
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                    if (text && !IsBeingRemoved(text.transform, root.transform))
                    {
                        label = text;
                        break;
                    }
            }
            else
                foreach (Transform child in root.transform) Destroy(child.gameObject);
        }
        else
        {
            root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = FallbackButton;
        }
        root.name = name;
        Unhide(root);
        root.SetActive(true);

        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;

        Button button = root.GetComponent<Button>();
        if (!root.TryGetComponent(out Image background))
        {
            // The frame was on a removed child: put the same frame on the button itself.
            background = root.AddComponent<Image>();
            if (source && source!.targetGraphic is Image frame && frame)
            {
                background.sprite = frame.sprite;
                background.type = frame.type;
                background.color = frame.color;
            }
            else background.color = FallbackButton;
        }
        button.targetGraphic = background;
        button.onClick = new Button.ButtonClickedEvent();
        button.interactable = true;

        if (!label && keepLabel)
        {
            label = CreateText(gui, root.transform, "Label", 18f, Color.white, TextAlignmentOptions.Center);
            Stretch((RectTransform)label.transform, 4f);
        }
        return root;
    }

    // A copy of a part of the panel that a mod's page has hidden (PanelOverlay.HiddenVanilla) would come out hidden too:
    // the copy is shown, and the hidden markers it brought along are removed.
    public static void Unhide(GameObject copy)
    {
        if (copy.TryGetComponent(out CanvasGroup group))
        {
            group.alpha = 1f;
            group.blocksRaycasts = true;
            group.interactable = true;
        }
        foreach (Transform child in copy.transform)
            if (child.name.StartsWith(PanelOverlay.HiddenMarkerPrefix, System.StringComparison.Ordinal)) Destroy(child.gameObject);
    }

    // Gamepad and key hints belong to the copied button's original action, so they are removed with their scripts.
    private static readonly List<GameObject> Hints = [];

    public static void RemoveHints(GameObject root)
    {
        Hints.Clear();
        foreach (UIGamePad pad in root.GetComponentsInChildren<UIGamePad>(true))
        {
            if (pad.m_hint) Hints.Add(pad.m_hint);
            Object.Destroy(pad);
        }
        foreach (UIInputHint hint in root.GetComponentsInChildren<UIInputHint>(true))
        {
            if (hint.m_gamepadHint) Hints.Add(hint.m_gamepadHint);
            if (hint.m_mouseKeyboardHint) Hints.Add(hint.m_mouseKeyboardHint);
            if (hint.m_gamepadMouseHint) Hints.Add(hint.m_gamepadMouseHint);
            foreach (UIInputHint.InputLayoutElement element in hint.m_inputLayoutSettings)
                if (element.m_hintObject) Hints.Add(element.m_hintObject);
            Object.Destroy(hint);
        }
        foreach (GameObject hint in Hints)
            if (hint && hint.transform.IsChildOf(root.transform) && hint != root) Destroy(hint);
        Hints.Clear();
    }

    private static bool IsBeingRemoved(Transform transform, Transform root)
    {
        for (Transform? current = transform; current && current != root; current = current!.parent)
            if (!current!.gameObject.activeSelf && current.name.EndsWith("_removed", System.StringComparison.Ordinal)) return true;
        return false;
    }

    // Destroying only happens at the end of the frame, so removed objects are also hidden and renamed right away.
    private static void Destroy(GameObject gameObject)
    {
        gameObject.SetActive(false);
        gameObject.name += "_removed";
        Object.Destroy(gameObject);
    }

    // The game's tooltip, as on the craft button, unless another tooltip prefab is given.
    public static UITooltip? AddTooltip(InventoryGui gui, GameObject target, GameObject? prefab = null)
    {
        if (!target.TryGetComponent(out UITooltip tooltip)) tooltip = target.AddComponent<UITooltip>();
        if (prefab) tooltip.m_tooltipPrefab = prefab;
        UITooltip? source = gui.m_craftButton ? gui.m_craftButton.GetComponent<UITooltip>() : null;
        if (source && !tooltip.m_tooltipPrefab) tooltip.m_tooltipPrefab = source!.m_tooltipPrefab;
        return tooltip.m_tooltipPrefab ? tooltip : null;
    }

    // The card shown when hovering an item in the inventory, and where the inventory shows it.
    public static GameObject? ItemTooltipPrefab(InventoryGui gui, out RectTransform? anchor)
    {
        InventoryGrid? grid = gui.m_containerGrid ? gui.m_containerGrid : gui.m_playerGrid;
        anchor = grid ? grid!.m_tooltipAnchor : null;
        UITooltip? element = grid && grid!.m_elementPrefab ? grid.m_elementPrefab.GetComponentInChildren<UITooltip>(true) : null;
        return element ? element!.m_tooltipPrefab : null;
    }

    // Text in the game's body font, the one used for recipe descriptions.
    public static TextMeshProUGUI CreateText(InventoryGui gui, Transform parent, string name, float size, Color color, TextAlignmentOptions alignment)
    {
        // Built inactive so the font is set before the text wakes up: awake without a font, it logs a
        // "LiberationSans SDF Font Asset was not found" warning, as the game has no default font.
        GameObject gameObject = new(name, typeof(RectTransform));
        gameObject.SetActive(false);
        gameObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = gameObject.AddComponent<TextMeshProUGUI>();
        TMP_Text? source = gui.m_recipeDecription ? gui.m_recipeDecription : gui.m_containerName;
        if (source)
            text.font = source!.font;
        // Waking up resets the size, wrapping and raycast settings, so those are set after it.
        gameObject.SetActive(true);
        if (source)
            text.fontSharedMaterial = source!.fontSharedMaterial;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    // A red X over a button, thin enough to keep the icon readable, with a dark edge so it shows on any icon.
    public static GameObject CreateCross(Transform button, float size)
    {
        float inset = size * CrossInsetPart;
        RectTransform cross = Child(button, "Cross", inset);
        float length = (size - inset * 2f) * 1.41f;
        foreach (float angle in new[] { 45f, -45f }) Bar(cross, angle, length + 3f, CrossThickness + 3f, CrossEdge);
        foreach (float angle in new[] { 45f, -45f }) Bar(cross, angle, length, CrossThickness, CrossColor);
        return cross.gameObject;
    }

    private static void Bar(RectTransform parent, float angle, float length, float thickness, Color color)
    {
        GameObject bar = new("Bar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)bar.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(length, thickness);
        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        Image image = bar.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    // A child that fills its parent, less an inset on every side.
    public static RectTransform Child(Transform parent, string name, float inset)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)child.transform;
        Stretch(rect, inset);
        return rect;
    }

    public static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        rect.localScale = Vector3.one;
    }

    public static Sprite? FindSprite(string name)
    {
        foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            if (sprite.name == name) return sprite;
        return null;
    }
}
