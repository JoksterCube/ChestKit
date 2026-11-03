using BepInEx.Logging;
using HarmonyLib;
using JoksterCube.ChestKit.Controls;

namespace JoksterCube.ChestKit;

// Started once by the mod that carries this copy of ChestKit, from its plugin's Awake.
//
// ChestKit doesn't use Harmony patch attributes: the mod merges ChestKit into its own DLL, so the mod's own
// PatchAll would pick them up and patch twice. ChestKit's patches are applied here instead, through the mod's Harmony.
public static class ChestKitCore
{
    private static ManualLogSource? _logger;
    private static bool _patched;

    // The mod's logger, for ChestKit's own messages.
    public static ManualLogSource? Logger => _logger;

    public static void Initialize(ManualLogSource logger, Harmony harmony)
    {
        _logger = logger;
        if (_patched) return;
        _patched = true;
        ControlsMenu.Patch(harmony);
    }
}
