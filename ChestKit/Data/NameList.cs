using System;
using System.Collections.Generic;

namespace JoksterCube.ChestKit.Data;

// A set of prefab names saved as one text value: comma separated, sorted and without repeats, so the same set is
// always saved as the same text. No Unity or game state, so it can be unit tested.
public static class NameList
{
    private const char Separator = ',';

    public static HashSet<string> Parse(string? text)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text)) return names;
        foreach (string part in text!.Split(Separator))
        {
            string name = part.Trim();
            if (name.Length > 0) names.Add(name);
        }
        return names;
    }

    public static string Format(IEnumerable<string> names)
    {
        List<string> sorted = [.. new HashSet<string>(names, StringComparer.Ordinal)];
        sorted.RemoveAll(name => string.IsNullOrEmpty(name) || name.IndexOf(Separator) >= 0);
        sorted.Sort(StringComparer.Ordinal);
        return string.Join(Separator.ToString(), sorted);
    }

    // The same list with all the names added (include) or removed.
    public static string WithAll(string? text, IEnumerable<string> names, bool include)
    {
        HashSet<string> set = Parse(text);
        foreach (string name in names)
        {
            if (include) set.Add(name);
            else set.Remove(name);
        }
        return Format(set);
    }

    // The same list with the name added (include) or removed.
    public static string With(string? text, string name, bool include)
    {
        HashSet<string> names = Parse(text);
        if (include) names.Add(name);
        else names.Remove(name);
        return Format(names);
    }
}
