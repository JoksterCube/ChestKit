using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace JoksterCube.ChestKit.Ui;

// A mod's wooden flap under the container panel in the inventory screen, shown while a container is open. It slides
// out from behind the container panel and back. Its buttons are stacked top to bottom; only the last few
// (alwaysShown) show below the panel until the flap is allowed to grow, when pointing at it slides it further down to
// show the rest. A button can be hidden (SetButtonShown): the flap then closes up around the others.
//
// Every mod built on ChestKit has its own flap, side by side under the panel's bottom edge, left to right by
// priority (lower priority further left; mods with the same priority by mod id). The mods each carry their own copy
// of ChestKit, so they agree through the flaps themselves: each flap's object is named
// "ChestKit_Flap|priority|modId", and each flap is placed after the shown flaps that come before it.
public sealed class ChestFlap
{
    public const string NamePrefix = "ChestKit_Flap|";
    private const char NameSeparator = '|';
    private const float ButtonSize = 52f;
    private const float ButtonGap = 8f;
    private const float Padding = 12f;
    private const float PanelGap = 2f;
    // Distance from the container panel's left edge, clear of its rounded corner.
    private const float LeftInset = 12f;
    // Between two mods' flaps.
    private const float FlapGap = 6f;
    private const float SlideSeconds = 0.18f;
    // How long the flap stays out after the pointer leaves it, so it doesn't snap back when passing between buttons.
    private const float HoverLinger = 0.3f;
    private const string FlapSprite = "woodpanel_flik";
    // The game tints its wooden panels darker than their textures.
    private static readonly Color FallbackPanelTint = new(0.45f, 0.38f, 0.34f, 1f);
    private static readonly Vector3[] Corners = new Vector3[4];

    private readonly string _modId;
    private readonly int _priority;
    private readonly int _alwaysShown;
    private readonly List<(string Name, UnityAction OnClick)> _buttonSpecs = [];
    private readonly List<UiParts.IconButton> _buttons = [];
    // Buttons hidden with SetButtonShown, by index.
    private readonly HashSet<int> _hiddenButtons = [];
    private RectTransform? _background;

    private RectTransform? _root;
    private RectTransform? _anchorPanel;
    // 0 = tucked behind the container panel, 1 = fully out.
    private float _shown;
    // 0 = only the always-shown buttons out, 1 = slid further down to show all of them.
    private float _grown;
    private float _hoverUntil;

    // modId: the mod's name, e.g. "FuelChests". priority: its place in the row (lower further left).
    // alwaysShown: how many of the bottom buttons show before the flap grows.
    public ChestFlap(string modId, int priority, int alwaysShown = 1)
    {
        _modId = modId.Replace(NameSeparator, '_');
        _priority = priority;
        _alwaysShown = Mathf.Max(1, alwaysShown);
    }

    // The same, with the mod's claimed place from FlapPriority.
    public ChestFlap(string modId, FlapPriority priority, int alwaysShown = 1) : this(modId, (int)priority, alwaysShown)
    {
    }

    // The buttons, top to bottom, in the order they were added. Filled when the flap is first made.
    public IReadOnlyList<UiParts.IconButton> Buttons => _buttons;

    // Fully out: its buttons can be clicked.
    public bool IsShown => _shown >= 1f;

    // Fully grown: all its buttons show.
    public bool IsGrown => _grown >= 1f;

    public bool Exists => _root;

    // Adds a button below those added before. Only before the flap is first made (the first Update while open).
    public void AddButton(string name, UnityAction onClick) => _buttonSpecs.Add((name, onClick));

    // Shows or hides a button; the flap closes up around those hidden. Any time, also before the flap is made.
    public void SetButtonShown(int index, bool shown)
    {
        bool changed = shown ? _hiddenButtons.Remove(index) : _hiddenButtons.Add(index);
        if (changed && _root) LayOut();
    }

    // Every frame from the inventory screen's Update. open: a container this mod handles is open. canGrow: pointing
    // at the flap may grow it; keepGrown: keeps it grown (while something it opened is showing).
    public void Update(InventoryGui gui, bool open, bool canGrow, bool keepGrown = false)
    {
        if (!_root)
        {
            if (!open) return;
            Create(gui);
            if (!_root) return;
        }

        float step = Time.unscaledDeltaTime / SlideSeconds;
        _shown = Mathf.MoveTowards(_shown, open ? 1f : 0f, step);
        bool grow = open && canGrow && (keepGrown || IsPointed());
        _grown = Mathf.MoveTowards(_grown, grow ? 1f : 0f, step);
        bool visible = _shown > 0f;
        if (_root!.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        if (visible) Place();
    }

    // The buttons shown: hidden ones don't count, but the flap keeps room for at least one.
    private int Count => Mathf.Max(1, _buttonSpecs.Count - _hiddenButtons.Count);
    private float Width => ButtonSize + Padding * 2f;
    private float Height => Padding * 2f + Count * ButtonSize + Mathf.Max(0, Count - 1) * ButtonGap;
    private float ShownHeight => Padding * 2f + Mathf.Min(_alwaysShown, Count) * ButtonSize + Mathf.Max(0, Mathf.Min(_alwaysShown, Count) - 1) * ButtonGap;

    // Lines the flap up with the container panel's bottom edge, read from that panel's corners every frame, so it
    // follows the panel as the inventory opens and stays under it when other mods make the panel bigger; across, after
    // the shown flaps of mods that come before it. Sliding moves it from behind the panel down; the part not shown
    // stays behind the panel, which also keeps it from being clicked.
    private void Place()
    {
        if (!_anchorPanel) return;
        _anchorPanel!.GetWorldCorners(Corners);
        Vector3 local = _root!.parent.InverseTransformPoint(Corners[0]);

        float hiddenY = local.y + Height;
        float partY = local.y - PanelGap + (Height - ShownHeight);
        float fullY = local.y - PanelGap;
        float shownY = Mathf.Lerp(partY, fullY, Ease(_grown));
        _root.localPosition = new Vector3(local.x + LeftInset + OffsetInRow(), Mathf.Lerp(hiddenY, shownY, Ease(_shown)), 0f);
    }

    // The width of the shown flaps that come before this one, with the gaps after them.
    private float OffsetInRow()
    {
        float offset = 0f;
        foreach (Transform sibling in _root!.parent)
        {
            if (sibling == _root || !sibling.gameObject.activeSelf || sibling is not RectTransform rect) continue;
            if (!TryParse(sibling.name, out int priority, out string modId)) continue;
            if (Compare(priority, modId, _priority, _modId) < 0) offset += rect.rect.width + FlapGap;
        }
        return offset;
    }

    // Lower priority first; the same priority by mod id, so every mod agrees on the order.
    private static int Compare(int priorityA, string modA, int priorityB, string modB) =>
        priorityA != priorityB ? priorityA.CompareTo(priorityB) : string.CompareOrdinal(modA, modB);

    private static bool TryParse(string name, out int priority, out string modId)
    {
        priority = 0;
        modId = "";
        if (!name.StartsWith(NamePrefix, StringComparison.Ordinal)) return false;
        string[] parts = name.Split(NameSeparator);
        if (parts.Length < 3 || !int.TryParse(parts[1], out priority)) return false;
        modId = parts[2];
        return true;
    }

    // Whether the pointer is on the part of the flap showing below the container panel, or was a moment ago.
    private bool IsPointed()
    {
        if (!_root || !_root!.gameObject.activeInHierarchy || !_anchorPanel) return false;
        Canvas canvas = _root.GetComponentInParent<Canvas>();
        Camera? camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 pointer = ZInput.pointerPosition;
        bool onFlap = RectTransformUtility.RectangleContainsScreenPoint(_root, pointer, camera)
            && !RectTransformUtility.RectangleContainsScreenPoint(_anchorPanel, pointer, camera);
        if (onFlap) _hoverUntil = Time.unscaledTime + HoverLinger;
        return Time.unscaledTime < _hoverUntil;
    }

    private static float Ease(float t) => t * t * (3f - 2f * t);

    private void Create(InventoryGui gui)
    {
        _anchorPanel = gui.m_container;
        if (!_anchorPanel || !_anchorPanel!.parent || _buttonSpecs.Count == 0) return;

        // Placed just before the container panel among its siblings, so it is drawn behind it while sliding.
        GameObject rootObject = new($"{NamePrefix}{_priority}{NameSeparator}{_modId}", typeof(RectTransform));
        rootObject.transform.SetParent(_anchorPanel.parent, false);
        rootObject.transform.SetSiblingIndex(_anchorPanel.GetSiblingIndex());
        _root = (RectTransform)rootObject.transform;
        _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0f, 1f);
        _root.localScale = Vector3.one;
        _background = CreateBackground(gui, _root);

        _buttons.Clear();
        for (int index = 0; index < _buttonSpecs.Count; index++)
        {
            UiParts.IconButton button = UiParts.CreateIconButton(gui, _root, _buttonSpecs[index].Name, ButtonSize, _buttonSpecs[index].OnClick);
            RectTransform rect = button.Rect;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            _buttons.Add(button);
        }
        LayOut();
        _shown = 0f;
        _grown = 0f;
        rootObject.SetActive(false);
    }

    // The flap's size and its shown buttons stacked top to bottom; hidden ones turned off.
    private void LayOut()
    {
        _root!.sizeDelta = new Vector2(Width, Height);
        // The background is turned a quarter, so its width runs along the flap's height.
        if (_background) _background!.sizeDelta = new Vector2(Height, Width);
        int row = 0;
        for (int index = 0; index < _buttons.Count; index++)
        {
            bool shown = !_hiddenButtons.Contains(index);
            GameObject button = _buttons[index].Button.gameObject;
            if (button.activeSelf != shown) button.SetActive(shown);
            if (!shown) continue;
            _buttons[index].Rect.anchoredPosition = new Vector2(0f, -(Padding + row * (ButtonSize + ButtonGap)));
            row++;
        }
    }

    // The game's side flap, the wooden board the repair button sits on: flat on one edge, rounded on the other. Its
    // flat edge is on the left; turned a quarter clockwise so that edge is on top and meets the container panel's
    // bottom edge, and tinted like the game's own flap.
    private static RectTransform CreateBackground(InventoryGui gui, RectTransform root)
    {
        GameObject gameObject = new("Background", typeof(RectTransform));
        gameObject.transform.SetParent(root, false);
        RectTransform rect = (RectTransform)gameObject.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localRotation = Quaternion.Euler(0f, 0f, -90f);
        Image background = gameObject.AddComponent<Image>();
        background.raycastTarget = true;

        Image? gameFlap = null;
        if (gui.m_repairPanel)
            foreach (Image image in gui.m_repairPanel.GetComponentsInChildren<Image>(true))
                if (image.sprite && image.sprite.name.StartsWith(FlapSprite, StringComparison.Ordinal))
                {
                    gameFlap = image;
                    break;
                }

        background.sprite = UiParts.FindSprite(FlapSprite) ?? gameFlap?.sprite;
        background.type = background.sprite && background.sprite!.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        background.color = gameFlap ? gameFlap!.color : FallbackPanelTint;
        if (gameFlap) background.material = gameFlap!.material;
        return rect;
    }

    // The inventory screen is rebuilt for every session, so the flap is made again next time.
    public void Clear()
    {
        if (_root) UnityEngine.Object.Destroy(_root!.gameObject);
        _root = null;
        _background = null;
        _anchorPanel = null;
        _buttons.Clear();
        _shown = 0f;
        _grown = 0f;
        _hoverUntil = 0f;
    }
}
