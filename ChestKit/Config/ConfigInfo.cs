using BepInEx.Configuration;

namespace JoksterCube.ChestKit.Config;

public sealed class ConfigInfo<T>
{
    public string Group { get; }
    public string Name { get; }
    public ConfigDescription Description { get; }
    public T DefaultValue { get; }
    public bool Synchronized { get; }
    // Hidden behind "Advanced settings" in Configuration Manager.
    public bool IsAdvanced { get; set; }

    public ConfigInfo(string group, string name, string description, T defaultValue, bool synchronized = true) :
        this(group, name, new ConfigDescription(description), defaultValue, synchronized)
    { }

    public ConfigInfo(string group, string name, ConfigDescription description, T defaultValue, bool synchronized = true)
    {
        Group = group;
        Name = name;
        Description = description;
        DefaultValue = defaultValue;
        Synchronized = synchronized;
    }
}