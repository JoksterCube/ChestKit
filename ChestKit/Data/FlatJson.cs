using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JoksterCube.ChestKit.Data;

// Reads a flat JSON object of strings, as translation files are: { "key": "text", ... }. Anything else is an error.
// Small on purpose, so ChestKit needs no JSON library.
public static class FlatJson
{
    public static Dictionary<string, string> Parse(string json)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        int at = 0;
        SkipSpace(json, ref at);
        Expect(json, ref at, '{');
        SkipSpace(json, ref at);
        if (Peek(json, at) == '}') return result;
        while (true)
        {
            SkipSpace(json, ref at);
            string key = ReadString(json, ref at);
            SkipSpace(json, ref at);
            Expect(json, ref at, ':');
            SkipSpace(json, ref at);
            result[key] = ReadString(json, ref at);
            SkipSpace(json, ref at);
            char next = Peek(json, at);
            at++;
            if (next == '}') return result;
            if (next != ',') throw new FormatException($"Expected ',' or '}}' at {at - 1}");
        }
    }

    private static string ReadString(string json, ref int at)
    {
        Expect(json, ref at, '"');
        StringBuilder text = new();
        while (true)
        {
            if (at >= json.Length) throw new FormatException("Unterminated string");
            char c = json[at++];
            if (c == '"') return text.ToString();
            if (c != '\\')
            {
                text.Append(c);
                continue;
            }
            if (at >= json.Length) throw new FormatException("Unterminated escape");
            char escape = json[at++];
            switch (escape)
            {
                case '"': text.Append('"'); break;
                case '\\': text.Append('\\'); break;
                case '/': text.Append('/'); break;
                case 'n': text.Append('\n'); break;
                case 't': text.Append('\t'); break;
                case 'r': text.Append('\r'); break;
                case 'b': text.Append('\b'); break;
                case 'f': text.Append('\f'); break;
                case 'u':
                    if (at + 4 > json.Length) throw new FormatException("Short \\u escape");
                    text.Append((char)int.Parse(json.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    at += 4;
                    break;
                default: throw new FormatException($"Unknown escape \\{escape}");
            }
        }
    }

    private static void SkipSpace(string json, ref int at)
    {
        while (at < json.Length && char.IsWhiteSpace(json[at])) at++;
    }

    private static char Peek(string json, int at) => at < json.Length ? json[at] : '\0';

    private static void Expect(string json, ref int at, char expected)
    {
        if (Peek(json, at) != expected) throw new FormatException($"Expected '{expected}' at {at}");
        at++;
    }
}
