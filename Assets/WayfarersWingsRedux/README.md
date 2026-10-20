# Wayfarer's Wings Redux

A KSP2 Redux port of [Wayfarer's Wings](https://github.com/Kerbalight/WayfarersWings) by leonardfactory (Kerbalight). Kerbals earn rank ribbons for what they do: orbiting and landing on each world, EVAs, docking, high-g flights, rovers, discoveries and more. The first kerbal to do something gets a special "first" ribbon that no one else can earn. More than 400 ribbons in all, with the original pixel art.

- **Kerbals:** the roster with each kerbal's ribbon rack, searchable and filterable by status (on a mission, in the active vessel, available, starred), sorted by name or prestige. Pick a kerbal for their record: missions, time on missions and on EVA, worlds, regions and points of interest visited, and every ribbon with its date.
- **Ribbons:** every ribbon there is to earn, with how many kerbals hold it.
- **Mission summary:** when a crewed vessel is recovered, the ribbons its crew earned on the mission.

Ribbons are stored in each save. The windows use the stock window template, like the game's own.

## Installing

Requires KSP2 Redux 26w42c or later. Unzip `WayfarersWingsRedux.zip` into the game's `mods` folder, so you end up with `mods\WayfarersWingsRedux\swinfo.json`.

## Settings

In the mod settings: always show ribbons at full size, the ribbon resolution (1x, 2x or detect from the screen), whether to show the ribbons earned on a mission when its vessel is recovered, and the "wing cheats" that let you award and revoke ribbons by hand.

## For mod authors

`WayfarersWingsRedux.WingsApi` is the supported way to read ribbons. The rest of the assembly, including the ported `WayfarersWings.*` namespaces, may change in any release. `WingsApi.ApiVersion` goes up if anything in it changes or is removed.

| Member | |
|---|---|
| `IsReady` | True once the ribbon definitions and a campaign's ribbons are loaded. |
| `AllAwards()`, `AwardsFor(kerbalName)` | `WingAwardInfo` copies, oldest first: `KerbalId`, `KerbalName`, `WingCode`, `WingName`, `Description`, `UT`, `IsFirst`, `Points`, `Superseded`, `ImageLayers`. |
| `LayerAddress(layer, hiDpi)` | The addressable sprite key of a ribbon layer at 2x or 1x. |
| `OpenKerbal(kerbalName)` | Opens the Wayfarer's Wings window on a kerbal's record. False if there's no such kerbal. |
| `event Awarded(info)`, `event Revoked(info)` | A kerbal earned a ribbon, or had one taken back by hand. |
| `event Loaded()` | A save's ribbons are ready. Ribbons are per save, so follow this too. |

`ImageLayers` are addressable sprite keys, bottom layer first; load them with the game's asset system and stack them to draw the ribbon. Without a hard dependency, look the type up by name so your mod still runs when this one isn't installed:

```csharp
var api = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("WayfarersWingsRedux.WingsApi", false)).FirstOrDefault(t => t != null);
var awards = api?.GetMethod("AllAwards")?.Invoke(null, null) as System.Collections.IEnumerable;
```

Listeners are called one at a time, and an exception in one is logged without affecting the others or the game. [Agency Heritage](../AgencyHeritage) uses this to show ribbons on its crew records.

## Ribbon definitions

Ribbons are defined in JSON under `Wings/` (addressable label `wayfarer_wings_data`; planets under `wayfarer_wings_planets`). Layers are referenced by their original addresses, such as `Assets/Wings/Layers/mun.png`. After adding or renaming a file there, run **Tools → Wayfarer's Wings → Sync Addressables** in Unity.

## Building from source

This mod lives in a shared Unity project with my other KSP2 Redux mods. Clone the whole repo and open it with the [Redux SDK Manager](https://github.com/KSP2Redux/Redux-SDK-Manager/releases/latest) (Unity 6000.4.1f1, KSP2 Unity Tools `beta-6`). This mod is in `Assets/WayfarersWingsRedux`. Build and test it from `swinfo.asset` in the Inspector or from the Redux SDK window.

**Releasing:** bump `version` in `swinfo.asset` and the CHANGELOG, then run **Deploy To Zip File**. Copy the generated `swinfo.json` over `Assets/WayfarersWingsRedux/swinfo.json` and commit it; in-game update checks read that copy from `main`.

## Credits and license

Original mod, ribbon art and ribbon definitions by leonardfactory ([Kerbalight/WayfarersWings](https://github.com/Kerbalight/WayfarersWings)), MIT. Redux port by Schneider, MIT. See [LICENSE.md](LICENSE.md).
