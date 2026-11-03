using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace JoksterCube.ChestKit.Data;

// A mod's texts in the game's selected language. English ships inside the mod (an embedded resource); other
// languages are files named after the mod and the game's language ("FuelChests.German.json", flat { "key": "text" }
// JSON) in a "Translations" folder next to the mod's DLL, or right next to it. The mod's name keeps mods installed
// loose in the same plugins folder apart. Missing texts fall back to English, then to the key.
//
// A translated text whose {0} placeholders don't fit its English one is left out (and logged), so a typo in a
// translation file shows English instead of breaking the message.
public sealed class Translations
{
    private const string English = "English";

    // The language the game shows, read once and again whenever the player changes it.
    private static string? _gameLanguage;
    private static bool _listening;

    private static readonly Regex Placeholder = new(@"\{(\d+)[^{}]*\}", RegexOptions.Compiled);

    private readonly Dictionary<string, string> _english;
    private readonly string[] _folders;
    private readonly string _modName;
    private string? _language;
    private Dictionary<string, string>? _selected;

    // englishResource: the embedded resource's name (its LogicalName in the .csproj).
    public Translations(Assembly assembly, string englishResource)
    {
        _modName = assembly.GetName().Name;
        _english = Load(() =>
        {
            using Stream? stream = assembly.GetManifestResourceStream(englishResource);
            if (stream == null) throw new FileNotFoundException($"Embedded resource {englishResource} not found");
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }, englishResource) ?? [];
        string folder = Path.GetDirectoryName(assembly.Location) ?? "";
        _folders = [Path.Combine(folder, "Translations"), folder];
    }

    // The text for a key in the selected language.
    public string Get(string key)
    {
        string language = GameLanguage();
        if (language != _language)
        {
            _language = language;
            _selected = language == English ? null : LoadLanguage(language);
        }
        if (_selected != null && _selected.TryGetValue(key, out string text)) return text;
        return _english.TryGetValue(key, out string english) ? english : key;
    }

    // Every key, with its English text: a template for translators.
    public IReadOnlyDictionary<string, string> EnglishTexts => _english;

    // A text the game itself translates ("$biome_meadows"), or the fallback when the game has no text for it or
    // isn't running (unit tests).
    public static string Game(string token, string fallback)
    {
        try
        {
            string text = Localize(token);
            return string.IsNullOrEmpty(text) || text == token || text.StartsWith("[") ? fallback : text;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    // Whether a translated text can stand in for the English one: it uses no placeholder the English one lacks, and
    // formats without error.
    public static bool Fits(string english, string translated)
    {
        int count = 0;
        HashSet<int> known = [];
        foreach (Match match in Placeholder.Matches(english))
        {
            int index = int.Parse(match.Groups[1].Value);
            known.Add(index);
            count = Math.Max(count, index + 1);
        }
        foreach (Match match in Placeholder.Matches(translated))
            if (!known.Contains(int.Parse(match.Groups[1].Value))) return false;
        try
        {
            object[] args = new object[count];
            for (int i = 0; i < count; i++) args[i] = string.Empty;
            string.Format(translated, args);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string GameLanguage()
    {
        if (_gameLanguage != null) return _gameLanguage;
        try
        {
            _gameLanguage = ReadGameLanguage();
            if (!_listening)
            {
                _listening = true;
                ListenForLanguageChange();
            }
        }
        catch (Exception)
        {
            // Not in the game (unit tests): English.
            _gameLanguage = English;
        }
        return _gameLanguage;
    }

    // Kept apart, so that touching the game's Localization fails where it is caught when the game isn't there.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ReadGameLanguage() =>
        Localization.instance != null ? Localization.instance.GetSelectedLanguage() : English;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ListenForLanguageChange() => Localization.OnLanguageChange += () => _gameLanguage = null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Localize(string token) =>
        Localization.instance != null ? Localization.instance.Localize(token) : token;

    private Dictionary<string, string>? LoadLanguage(string language)
    {
        foreach (string folder in _folders)
        {
            string path = Path.Combine(folder, $"{_modName}.{language}.json");
            if (!File.Exists(path)) continue;
            Dictionary<string, string>? texts = Load(() => File.ReadAllText(path), path);
            if (texts != null) Check(texts, Path.GetFileName(path));
            return texts;
        }
        return null;
    }

    // Leaves out texts that don't fit their English one, and tells translators what is missing or unknown.
    private void Check(Dictionary<string, string> texts, string file)
    {
        List<string> broken = [], unknown = [], missing = [];
        foreach (KeyValuePair<string, string> text in texts)
        {
            if (!_english.TryGetValue(text.Key, out string english)) unknown.Add(text.Key);
            else if (!Fits(english, text.Value)) broken.Add(text.Key);
        }
        foreach (string key in broken) texts.Remove(key);
        foreach (string key in _english.Keys)
            if (!texts.ContainsKey(key) && !broken.Contains(key)) missing.Add(key);

        if (broken.Count > 0)
            ChestKitCore.Logger?.LogWarning($"{file}: placeholders don't match English, showing English for: {string.Join(", ", broken)}");
        if (unknown.Count > 0)
            ChestKitCore.Logger?.LogWarning($"{file}: unknown keys (renamed or misspelled?): {string.Join(", ", unknown)}");
        if (missing.Count > 0)
            ChestKitCore.Logger?.LogInfo($"{file}: {missing.Count} texts not translated, shown in English: {string.Join(", ", missing)}");
    }

    private static Dictionary<string, string>? Load(Func<string> read, string source)
    {
        try { return FlatJson.Parse(read()); }
        catch (Exception exception)
        {
            ChestKitCore.Logger?.LogWarning($"Couldn't read translations from {source}: {exception.Message}");
            return null;
        }
    }
}
