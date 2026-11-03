using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using ServerSync;

namespace JoksterCube.ChestKit.Config;

public static class ConfigOptions
{
    private static ConfigFile? _configFile;
    private static ConfigSync? _configSync;
    // Configuration Manager lists higher Order first, so each group counts down in binding order.
    private static readonly Dictionary<string, int> NextOrder = [];

    public static void Initialize(ConfigFile config, ConfigSync configSync)
    {
        _configFile = config;
        _configSync = configSync;
    }

    public static ConfigEntry<T> Config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true, bool advanced = false)
    {
        int order = NextOrder.TryGetValue(group, out int next) ? next : 1000;
        NextOrder[group] = order - 1;
        ConfigurationManagerAttributes attributes = new() { Order = order, IsAdvanced = advanced ? true : null };
        ConfigDescription extendedDescription = new(
            description.Description + (synchronizedSetting
                ? " [Synced with Server]"
                : " [Not Synced with Server]"),
            description.AcceptableValues,
            (description.Tags ?? []).Append(attributes).ToArray());

        ConfigEntry<T> configEntry = (_configFile ?? throw new InvalidOperationException("ConfigOptions is not initialized.")).Bind(group, name, value, extendedDescription);
        SyncedConfigEntry<T> syncedConfigEntry = (_configSync ?? throw new InvalidOperationException("ConfigOptions is not initialized.")).AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;
        return configEntry;
    }

    public static ConfigEntry<T> Config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) =>
        Config(group, name, value, new ConfigDescription(description), synchronizedSetting);

    public static ConfigEntry<T> Config<T>(ConfigInfo<T> configInfo) =>
        Config(configInfo.Group, configInfo.Name, configInfo.DefaultValue, configInfo.Description, configInfo.Synchronized, configInfo.IsAdvanced);
}