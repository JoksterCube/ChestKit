# ChestKit

Shared building blocks for JoksterCube's chest-based Valheim mods (FuelChests, ProcessChest, TempestRite and the ones that follow).

ChestKit is **not a mod**. Each mod builds it from source and merges it into its own DLL, so:

- players install nothing extra and there is no dependency to keep in step;
- each mod has its own private copy (merged as `internal`), so two mods built against different ChestKit versions never clash.

## What's in it

| Folder | Contents |
| --- | --- |
| `Common` | `PatchGuard` (switches a broken feature off after its first error), `Toggle`, key shortcut helpers |
| `Config` | `ConfigInfo` / `ConfigOptions` (BepInEx config with ServerSync and Configuration Manager ordering) |
| `Controls` | `ControlsMenu`: key rows in the game's Settings > Controls tab, kept in config shortcuts |
| `Data` | `NameList`: a set of names saved as one text value; `ChestWrite`: writes to a container's network data (ZDO) from any player who has it open, sending it to the owner's game when this one doesn't own it (MultiUserChest), and only for the keys the mod lists; `Translations` + `FlatJson`: texts in the game's language (English embedded, other languages as `<Language>.json` next to the DLL); `ContainerRules`: which containers a mod works with (never gravestones or loot; carts and boats, the obliterator and private chests by choice) |
| `Highlight` | `WorldHighlight`: objects drawn through walls in pulsing color; a see-through sphere with a countdown status effect |
| `Ui` | `UiParts`: buttons, icons, crosses, tooltips and text made from copies of the game's own UI; `ChestFlap`: a mod's flap of buttons under the chest panel, placed by its claimed `FlapPriority`; `PanelOverlay`: a mod's page shown in the chest panel in place of its items (hides and restores the panel's own items, buttons and title, lines up with its "Take all" row, places tabs and a close button there, and makes scrolling lists like the game's) |

## Using it in a mod

1. Put this folder next to the mod's folder (`Sources/ChestKit`, `Sources/MyMod`).
2. In the mod's `.csproj`:

   ```xml
   <ItemGroup>
     <ProjectReference Include="..\ChestKit\ChestKit\ChestKit.csproj" />
   </ItemGroup>
   ```

3. In the mod's `ILRepack.targets`, merge it (with `Internalize="true"`):

   ```xml
   <InputAssemblies Include="$(OutputPath)\ChestKit.dll" />
   ```

4. In the plugin's `Awake`, after building the config and before `PatchAll`:

   ```csharp
   ChestKitCore.Initialize(Logger, harmony);
   ControlsMenu.Add("MyMod_SomeKey", "Do something", () => PluginConfig.SomeKey);   // optional
   ```

   ChestKit has no Harmony patch attributes: merged into the mod, they would be patched twice by the mod's own
   `PatchAll`. Its patches are applied by `Initialize` instead.

5. Optional: a `GlobalUsings.cs` with `global using JoksterCube.ChestKit.Ui;` and the other namespaces.

Game paths come from `environment.props` (override them in a git-ignored `environment.props.user`).

## Flaps under the chest panel

Each mod has its own `ChestFlap`, and all flaps sit side by side under the chest panel's bottom edge, **left to right by
priority**: lower priority further left. Mods with the same priority are ordered by mod id, so every player sees the
same order. Only flaps that are showing take up room, so there are never gaps.

```csharp
ChestFlap flap = new("MyMod", FlapPriority.MyMod);  // once; claim a value in FlapPriority first
flap.AddButton("MyModButton", OnClick);             // top to bottom, before the first Update
flap.Update(gui, open, canGrow, keepGrown);         // every frame, from an InventoryGui.Update postfix
```

The mods each carry their own copy of ChestKit, so they agree through the flaps themselves: a flap's object is named
`ChestKit_Flap|priority|modId`, and each flap places itself after the showing flaps that come before it. Keep that
name format if ChestKit changes, or older and newer mods will stop lining up.

## Pages in the chest panel

A flap button can open a page in the chest panel itself, in place of its items (`PanelOverlay`). Only one page shows
at a time: clicking from one mod's flap to another's closes the first page and opens the other.

```csharp
RectTransform page = PanelOverlay.Create(gui, "MyMod");     // once
PanelOverlay.HiddenVanilla hidden = new("MyMod");           // hides the chest's own items, buttons and title

// opening
hidden.Hide(gui);
PanelOverlay.BringToFront(page);
page.gameObject.SetActive(true);

// every frame while open
if (PanelOverlay.IsCovered(page)) Close();                // another mod's page was opened over this one

// closing
hidden.Restore();
page.gameObject.SetActive(false);
```

As with flaps, the mods agree through the scene, since each carries its own copy of ChestKit. A page's object is named
`ChestKit_Page|modId`, and the newest page is the panel's last child. Each hidden part of the panel gets an inactive
child `ChestKit_Hidden|modId|alpha|blocksRaycasts|interactable` holding how it was before any page hid it, and only
comes back when the last such marker is gone, so pages may close in any order. Keep both formats if ChestKit changes.

Priorities are claimed in the `FlapPriority` enum (`Ui/FlapPriority.cs`), one value per mod, so no two mods share a
place by accident. A new mod adds its value there and a row here before it uses it. Never change a value a released
mod already uses: the number goes into the flap's object name.

| Priority | Mod (`FlapPriority`) |
| --- | --- |
| 10 | TempestRite (`TempestRite`) |
| 100 | FuelChests (`FuelChests`) |
| 200 | ProcessChest (`ProcessChest`) |

Leave gaps (100, 200, 300...) so new mods can go in between.

## Version

1.1.0 (set in `ChestKit/ChestKit.csproj`). Releases on GitHub are tagged `v<version>` and carry the built
`ChestKit.dll`, for reference only: mods build ChestKit from source.

## License

GNU GPL v3 or later.
