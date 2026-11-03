using System;
using System.Collections.Generic;

namespace JoksterCube.ChestKit.Common;

// Keeps a broken feature (usually after a game update) from throwing every frame inside vanilla code:
// the first error is logged and that feature is switched off for the rest of the game session.
public static class PatchGuard
{
    private static readonly HashSet<string> Disabled = [];

    public static bool IsDisabled(string feature) => Disabled.Contains(feature);

    public static void Fail(string feature, Exception exception)
    {
        if (Disabled.Add(feature))
            ChestKitCore.Logger?.LogError($"{feature} disabled after an unexpected error. Please report it with this log.\n{exception}");
    }
}
