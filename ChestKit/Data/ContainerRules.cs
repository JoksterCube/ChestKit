using System;

namespace JoksterCube.ChestKit.Data;

// Which containers a mod works with. Never gravestones, or containers that aren't built pieces (loot in the world).
// Carts and boats, the obliterator and private chests each depend on the mod (usually its config), off by default.
public sealed class ContainerRules
{
    public Func<bool> AllowVehicles { get; set; } = () => false;
    public Func<bool> AllowObliterator { get; set; } = () => false;
    public Func<bool> AllowPrivate { get; set; } = () => false;

    // A container in the world (its network object loaded).
    public bool Allows(Container container) =>
        container && container.m_nview && container.m_nview.GetZDO() != null && AllowsKind(container);

    // From the container's kind alone, so it also works on a prefab (as servers see the world).
    public bool AllowsKind(Container container)
    {
        if (!container) return false;
        if (container.GetComponentInParent<TombStone>()) return false;
        if (!container.GetComponentInParent<Piece>()) return false;
        if (!AllowVehicles() && (container.GetComponentInParent<Ship>() || container.GetComponentInParent<Vagon>())) return false;
        if (!AllowObliterator() && container.GetComponentInParent<Incinerator>()) return false;
        if (!AllowPrivate() && container.m_privacy == Container.PrivacySetting.Private) return false;
        return true;
    }
}
