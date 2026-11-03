using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using JoksterCube.ChestKit.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valheim.SettingsGui;

namespace JoksterCube.ChestKit.Controls;

// Key rows in the game's Settings > Controls tab, kept in config shortcuts. The game binds a single key, so a row
// sets the main key of its shortcut and keeps its modifiers. Like the vanilla rows, a new key is only kept when the
// settings are confirmed. Mods add their rows with Add (from Awake), and ChestKitCore applies the patches.
public static class ControlsMenu
{
    private const string Feature = "Controls menu shortcuts";

    private sealed class Row(string name, Func<string> label, Func<ConfigEntry<KeyboardShortcut>> entry)
    {
        internal string Name { get; } = name;
        // "TempestRite · Accept": the mod (the name up to its first "_") in front, smaller and grey, so players know
        // where the row is from.
        internal string Label => Name.IndexOf('_') is var end and > 0
            ? $"<size=85%><color=#A8A8A8>{Name.Substring(0, end)} ·</color></size> {label()}"
            : label();
        internal Func<ConfigEntry<KeyboardShortcut>> Entry { get; } = entry;
    }

    private static readonly List<Row> Rows = [];
    private static Dictionary<string, KeyCode>? _keyCodes;

    // name: unique, the mod's name then "_", e.g. "FuelChests_ShowSources" (the mod's name is shown before the label);
    // label: shown in the menu (asked for each time, so it can follow
    // the game's language).
    public static void Add(string name, Func<string> label, Func<ConfigEntry<KeyboardShortcut>> entry)
    {
        if (!Rows.Exists(row => row.Name == name)) Rows.Add(new Row(name, label, entry));
    }

    internal static void Patch(Harmony harmony)
    {
        Type type = typeof(KeyboardMouseSettings);
        harmony.Patch(AccessTools.Method(type, nameof(KeyboardMouseSettings.Initialize)),
            prefix: new HarmonyMethod(typeof(ControlsMenu), nameof(InitializePrefix)));
        harmony.Patch(AccessTools.Method(type, nameof(KeyboardMouseSettings.UpdateBindings)),
            postfix: new HarmonyMethod(typeof(ControlsMenu), nameof(UpdateBindingsPostfix)));
        harmony.Patch(AccessTools.Method(type, nameof(KeyboardMouseSettings.OnOkAsync)),
            postfix: new HarmonyMethod(typeof(ControlsMenu), nameof(OkPostfix)));
        harmony.Patch(AccessTools.Method(type, nameof(KeyboardMouseSettings.ResetBindings)),
            postfix: new HarmonyMethod(typeof(ControlsMenu), nameof(ResetPostfix)));
    }

    private static void InitializePrefix(KeyboardMouseSettings __instance) => Guard(() => AddRows(__instance));
    private static void UpdateBindingsPostfix(KeyboardMouseSettings __instance) => Guard(() =>
    {
        Arrange(__instance);
        UpdateLabels(__instance);
    });
    private static void OkPostfix() => Guard(Save);
    private static void ResetPostfix(KeyboardMouseSettings __instance) => Guard(() => Reset(__instance));

    private static void Guard(Action action)
    {
        if (Rows.Count == 0 || PatchGuard.IsDisabled(Feature)) return;
        try { action(); }
        catch (Exception exception) { PatchGuard.Fail(Feature, exception); }
    }

    // Before the tab sets up its rows, so the new ones get the same click handling and gamepad navigation.
    private static void AddRows(KeyboardMouseSettings settings)
    {
        ZInput input = ZInput.instance;
        if (input == null) return;
        // The menu's binding starts from the config every time, so a key chosen and then backed out of is undone.
        foreach (Row row in Rows) Button(input, row.Name).Rebind(PathFor(row.Entry().Value.MainKey));

        List<KeySetting> keys = settings.m_keys;
        if (keys.Count == 0 || keys.Exists(key => key.m_keyName == Rows[0].Name)) return;
        // At the bottom of the first column, copied from a row of that column (label left of the key). Arrange puts
        // every row in its column afterwards, whatever other mods added.
        int insertAt = Mathf.Clamp(settings.m_keyRows, 1, keys.Count);
        int leftRow = keys.FindLastIndex(key => IsShown(key) && IsLeftColumn(key));
        KeySetting template = keys[leftRow >= 0 ? leftRow : insertAt - 1];
        int sibling = template.m_keyTransform.GetSiblingIndex() + 1;
        for (int index = 0; index < Rows.Count; index++)
        {
            RectTransform row = UnityEngine.Object.Instantiate(template.m_keyTransform, template.m_keyTransform.parent);
            row.name = Rows[index].Name;
            row.SetSiblingIndex(sibling + index);
            Transform label = row.Find("Label");
            if (label && label.TryGetComponent(out TMP_Text text))
            {
                text.richText = true;
                text.text = Rows[index].Label;
            }
            keys.Insert(insertAt + index, new KeySetting
            {
                m_keyName = Rows[index].Name,
                m_keyTransform = row,
                m_blockedButtons = template.m_blockedButtons ?? []
            });
        }
        // Arrange sets the rows and height once every mod has added its rows (some of the list's rows are hidden, so
        // counting the list would be off).
        settings.m_keyRows += Rows.Count;
    }

    // The game shows the main key; the modifiers kept from the config go in front of it.
    private static void UpdateLabels(KeyboardMouseSettings settings)
    {
        foreach (KeySetting key in settings.m_keys)
        {
            Row? row = Rows.Find(candidate => candidate.Name == key.m_keyName);
            if (row == null) continue;
            TMP_Text text = key.m_keyTransform.GetComponentInChildren<Button>().GetComponentInChildren<TMP_Text>();
            if (text.text.Length == 0) continue;
            foreach (KeyCode modifier in Modifiers(row.Entry().Value, KeyCode.None).Reverse())
                text.text = KeyboardShortcutExtensions.KeyName(modifier) + " + " + text.text;
        }
    }

    private static void Save()
    {
        ZInput input = ZInput.instance;
        if (input == null) return;
        foreach (Row row in Rows)
        {
            if (!input.m_buttons.TryGetValue(row.Name, out ZInput.ButtonDef button)) continue;
            // A key the config cannot name is left alone rather than clearing the shortcut.
            if (!TryKeyFor(button.GetActionPath(), out KeyCode key)) continue;
            ConfigEntry<KeyboardShortcut> entry = row.Entry();
            if (key == entry.Value.MainKey) continue;
            entry.Value = key == KeyCode.None
                ? KeyboardShortcut.Empty
                : new KeyboardShortcut(key, Modifiers(entry.Value, key).ToArray());
        }
    }

    // Vanilla Reset Controls clears the rows' bindings; they go back to the default keys instead.
    private static void Reset(KeyboardMouseSettings settings)
    {
        ZInput input = ZInput.instance;
        if (input == null) return;
        foreach (Row row in Rows)
            Button(input, row.Name).Rebind(PathFor(((KeyboardShortcut)row.Entry().DefaultValue).MainKey));
        settings.UpdateBindings();
    }

    // ZInput forgets its buttons when the input layout changes, so they are added back whenever the menu opens.
    private static ZInput.ButtonDef Button(ZInput input, string name)
    {
        if (input.m_buttons.TryGetValue(name, out ZInput.ButtonDef button)) return button;
        button = new ZInput.ButtonDef(name, "", null, ZInput.InputSource.KeyboardMouse, rebindable: true);
        input.m_buttons.Add(name, button);
        input.SubscribeButton(button);
        return button;
    }

    private static IEnumerable<KeyCode> Modifiers(KeyboardShortcut shortcut, KeyCode mainKey) =>
        shortcut.Modifiers.Where(modifier => modifier != mainKey);

    private static string PathFor(KeyCode key) => key == KeyCode.None ? "" : ZInput.KeyCodeToPath(key);

    // The game's own path for every key it knows, read back the other way. Paths ignore case.
    private static bool TryKeyFor(string? path, out KeyCode key)
    {
        if (string.IsNullOrEmpty(path))
        {
            key = KeyCode.None;
            return true;
        }
        if (_keyCodes == null)
        {
            _keyCodes = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyCode code in Enum.GetValues(typeof(KeyCode)))
            {
                if (code == KeyCode.None || code >= KeyCode.JoystickButton0) continue;
                string codePath = ZInput.KeyCodeToPath(code);
                if (!codePath.EndsWith("/None") && !_keyCodes.ContainsKey(codePath)) _keyCodes[codePath] = code;
            }
        }
        return _keyCodes.TryGetValue(path!, out key);
    }

    // The key grid fills a column top to bottom and starts the next when its height runs out, so the columns only
    // split right when the rows of the first column (label left of the key) all come first and the grid is exactly as
    // tall as that column. Other mods adding rows their own way (or counting rows differently) can upset that, so the
    // rows are put back in order, in the list too (for gamepad navigation), every time the bindings are shown.
    private static void Arrange(KeyboardMouseSettings settings)
    {
        List<KeySetting> keys = settings.m_keys;
        if (keys.Count < 2) return;
        // Hidden rows take no place in the grid; they go last.
        List<KeySetting> left = keys.FindAll(key => IsShown(key) && IsLeftColumn(key));
        List<KeySetting> right = keys.FindAll(key => IsShown(key) && !IsLeftColumn(key));
        List<KeySetting> hidden = keys.FindAll(key => !IsShown(key));
        if (left.Count == 0 || right.Count == 0) return;

        Transform grid = keys[0].m_keyTransform.parent;
        int sibling = int.MaxValue;
        foreach (KeySetting key in keys)
            if (key.m_keyTransform.parent == grid) sibling = Mathf.Min(sibling, key.m_keyTransform.GetSiblingIndex());
        keys.Clear();
        keys.AddRange(left);
        keys.AddRange(right);
        keys.AddRange(hidden);
        foreach (KeySetting key in keys)
            if (key.m_keyTransform.parent == grid) key.m_keyTransform.SetSiblingIndex(sibling++);

        int rows = Mathf.Max(left.Count, right.Count);
        settings.m_keyRows = rows;
        FitGrid(grid, rows);
    }

    private static bool IsShown(KeySetting key) => key.m_keyTransform && key.m_keyTransform.gameObject.activeSelf;

    // A first-column row has its label left of its key; second-column rows are mirrored (same children, other places).
    private static bool IsLeftColumn(KeySetting key)
    {
        Transform row = key.m_keyTransform;
        Transform label = row.Find("Label");
        Transform button = row.Find("Button");
        if (!label || !button) return true;
        return label.localPosition.x < button.localPosition.x;
    }

    // Exactly as tall as the given number of rows.
    private static void FitGrid(Transform grid, int rows)
    {
        if (grid is not RectTransform rect || !grid.TryGetComponent(out GridLayoutGroup layout)) return;
        float height = layout.padding.vertical + rows * layout.cellSize.y + (rows - 1) * layout.spacing.y;
        if (!Mathf.Approximately(rect.rect.height, height)) rect.sizeDelta += new Vector2(0f, height - rect.rect.height);
    }
}
