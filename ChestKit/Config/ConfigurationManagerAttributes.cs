#pragma warning disable CS0649, CS0169 // Fields are read by BepInEx.ConfigurationManager through reflection.

namespace JoksterCube.ChestKit.Config;

// Recognized by name by BepInEx.ConfigurationManager when passed as a ConfigDescription tag.
public sealed class ConfigurationManagerAttributes
{
    public int? Order;
    public bool? IsAdvanced;
    public bool? ReadOnly;
    public bool? Browsable;
    public string? DispName;
    public string? Category;
}
