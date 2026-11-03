namespace JoksterCube.ChestKit.Ui;

// Each mod's place in the row of flaps under the chest panel (lower further left). One value per mod, so no two
// flaps share a place: a new mod claims a free number here (and in the README) before using it. Leave gaps.
// The number is what goes into the flap's object name, so never change a value a released mod already uses.
public enum FlapPriority
{
    TempestRite = 10,
    FuelChests = 100,
    ProcessChest = 200
}
