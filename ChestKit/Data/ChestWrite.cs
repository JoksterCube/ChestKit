using System;
using System.Collections.Generic;
using HarmonyLib;

namespace JoksterCube.ChestKit.Data;

// Writes a mod's values to a container's network data (ZDO). Only the game that owns a container should write its
// data: a write from another game can clash with the owner's own writes (its items too) and one of them is lost.
// Without other mods only the owner has a container open, but with mods like MultiUserChest other players can have it
// open too. Their writes are sent to the owner's game, which makes them, and they reach everyone with the container's
// next update.
//
//   ChestWrite.Setup("MyMod", container => IsMine(container), () => [KeyA, KeyB]);   // once, in the plugin's Awake
//   using (ChestWrite zdo = ChestWrite.Begin(container)) { zdo.Set(KeyA, zdo.GetInt(KeyA) + 1); }
//
// Inside one write, reads see the values set before them. Writes from another player's game only show once the
// owner's update arrives (a moment later).
public sealed class ChestWrite : IDisposable
{
    private const byte IntValue = 0;
    private const byte StringValue = 1;
    // A write that reaches a game no longer owning the container is passed on to the new owner, at most this often.
    private const int MaxForwards = 2;

    private static string? _rpc;
    private static Func<Container, bool>? _applies;
    private static Func<IEnumerable<int>>? _keySource;
    private static HashSet<int>? _keys;

    private readonly Container _container;
    private readonly ZDO _zdo;
    private readonly bool _owner;
    // Values set by a game that doesn't own the container, sent on Dispose, in order.
    private readonly List<(int Key, object Value)> _pending = [];
    private readonly Dictionary<int, object> _latest = [];

    private ChestWrite(Container container, ZDO zdo, bool owner)
    {
        _container = container;
        _zdo = zdo;
        _owner = owner;
    }

    // Once, from the plugin's Awake, before ChestKitCore.Initialize. modId names the mod's RPC (each mod has its own);
    // applies: the containers the mod writes to; keys: every key other players may write, read the first time one
    // arrives. Anything else sent is ignored, so other players can't change a container's items through this.
    public static void Setup(string modId, Func<Container, bool> applies, Func<IEnumerable<int>> keys)
    {
        _rpc = $"ChestKit_Write_{modId}";
        _applies = applies;
        _keySource = keys;
        _keys = null;
    }

    // Whether the container has network data to write to. Null when it doesn't.
    public static ChestWrite? Begin(Container? container)
    {
        if (!container || !container!.m_nview || !container.m_nview.IsValid()) return null;
        ZDO? zdo = container.m_nview.GetZDO();
        return zdo == null ? null : new ChestWrite(container, zdo, container.m_nview.IsOwner());
    }

    public bool GetBool(int key) => GetInt(key) != 0;

    public int GetInt(int key, int defaultValue = 0) =>
        _latest.TryGetValue(key, out object value) && value is int number ? number : _zdo.GetInt(key, defaultValue);

    public string GetString(int key) =>
        _latest.TryGetValue(key, out object value) && value is string text ? text : _zdo.GetString(key);

    public void Set(int key, bool value) => Set(key, value ? 1 : 0);

    public void Set(int key, int value) => Remember(key, value);

    public void Set(int key, string value) => Remember(key, value ?? "");

    private void Remember(int key, object value)
    {
        if (_owner)
        {
            if (value is int number) _zdo.Set(key, number);
            else _zdo.Set(key, (string)value);
            return;
        }
        _latest[key] = value;
        _pending.Add((key, value));
    }

    // Sends the values set by a game that doesn't own the container to the owner's game.
    public void Dispose()
    {
        if (_owner || _pending.Count == 0) return;
        Send(_container, 0, _pending);
        _pending.Clear();
    }

    private static void Send(Container container, int forwards, List<(int Key, object Value)> values)
    {
        if (_rpc == null || !container || !container.m_nview || !container.m_nview.IsValid()) return;
        ZPackage package = new();
        package.Write(forwards);
        package.Write(values.Count);
        foreach ((int key, object value) in values)
        {
            package.Write(key);
            if (value is int number)
            {
                package.Write(IntValue);
                package.Write(number);
            }
            else
            {
                package.Write(StringValue);
                package.Write((string)value);
            }
        }
        container.m_nview.InvokeRPC(_rpc, package);
    }

    internal static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Container), nameof(Container.Awake)),
            postfix: new HarmonyMethod(typeof(ChestWrite), nameof(AwakePostfix)));
    }

    private static void AwakePostfix(Container __instance)
    {
        if (_rpc == null || !__instance.m_nview || __instance.m_nview.GetZDO() == null) return;
        Container container = __instance;
        __instance.m_nview.Register<ZPackage>(_rpc, (_, package) => Received(container, package));
    }

    // In the owner's game: another player's values for this container.
    private static void Received(Container container, ZPackage package)
    {
        try
        {
            if (!container || !container.m_nview || !container.m_nview.IsValid() || _applies == null || !_applies(container)) return;
            int forwards = package.ReadInt();
            int count = package.ReadInt();
            HashSet<int> keys = Keys();
            List<(int Key, object Value)> values = [];
            for (int index = 0; index < count; index++)
            {
                int key = package.ReadInt();
                byte kind = package.ReadByte();
                object value = kind switch
                {
                    IntValue => package.ReadInt(),
                    StringValue => package.ReadString(),
                    _ => throw new FormatException($"unknown value kind {kind}"),
                };
                if (keys.Contains(key)) values.Add((key, value));
            }
            if (values.Count == 0) return;

            // Ownership moved on while this was on its way: passed on to the new owner.
            if (!container.m_nview.IsOwner())
            {
                if (forwards < MaxForwards) Send(container, forwards + 1, values);
                return;
            }

            ZDO zdo = container.m_nview.GetZDO();
            foreach ((int key, object value) in values)
            {
                if (value is int number) zdo.Set(key, number);
                else zdo.Set(key, (string)value);
            }
        }
        catch (Exception exception)
        {
            ChestKitCore.Logger?.LogWarning($"Ignored another player's chest settings that couldn't be read: {exception.Message}");
        }
    }

    private static HashSet<int> Keys() => _keys ??= _keySource == null ? [] : [.. _keySource()];
}
