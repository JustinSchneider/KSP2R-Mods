# Agency Heritage

A KSP2 Redux mod that keeps a museum of your agency's history: its flights, firsts, crew and craft. The history is sorted into **brand eras**: each time you rebrand the agency with a new name, flag or colors, a new era begins.

- **Era rail and exhibit:** every era's flag and name along the top; pick one to see its dates, colors, launches, firsts and crew, and a timeline of its milestones. The timeline has a dated axis and shows each flight going up and coming back down, with every flight and loss labelled with its dates. Zoom in to spread out a busy stretch, and click a flight for its whole story: launch, orbits, landings, docking and how it ended, each dated.
- **Missions:** a patch for every flight, sortable (oldest, newest, longest, furthest) and filterable (crewed, uncrewed, in flight, recovered, lost), drawn from what it actually did: a hop over the horizon, an orbit, a flyby, or a lander and flag on another world. A flight that visited several worlds shows its whole route. The craft is a probe or a crewed rocket sized by its part count. The patch shows the crew, a gold star for each first the flight earned, a docking mark, a fire burst if the craft was lost, and a black border if kerbals were lost. Click a patch to see the flight's story on the timeline. Flights launched before the mod was installed get whatever the game's travel log still remembers about them.
- **Collection:** the blueprints saved during each era, shown in flight as they looked at the time, each with a spec sheet: Δv, launch TWR and burn time (noted when you save the craft in the VAB), mass, size, parts and stages, crew seats, engines, propellant, power and other systems, and every flight launched from it. Each blueprint can be copied as a craft (paste it into any VAB with Ctrl+V, yours or another player's), copied as a JSON spec sheet, or exported to a folder with its files.
- **Crew:** a service record for each kerbal of the era: when they joined, flights flown, time aloft, every world they orbited or landed on, and the firsts they were part of. Click a kerbal for their flights that era, each opening its story on the timeline, next to their whole career across all eras. Lost and returned veterans are marked. If [Wayfarer's Wings Redux](../WayfarersWingsRedux) is installed, their ribbons show too, each with when and on which flight it was earned, and the record links to their Wayfarer's Wings profile.
- **Rebrand:** pick a new name, flag and colors. The old era is archived. The rebrand is cosmetic and free. Changing the agency flag in the settings, or setting agency colors from the VAB paint picker, opens the Rebrand page with your choice filled in, so every change of identity is recorded.
- **Start in agency colors:** new parts in the VAB start in your agency's colors (can be turned off in the mod settings).

History is stored in each save, so loading an older save shows the history as it was then.

The window uses the stock window template (the same frame, header and resize handle as the game's own windows). It's translated into German, French, Spanish, Brazilian Portuguese, Russian, Japanese and Simplified Chinese. These are first drafts: corrections from native speakers are very welcome.

## Installing

Requires KSP2 Redux 26w42c or later. Unzip `AgencyHeritage.zip` into the game's `mods` folder, so you end up with `mods\AgencyHeritage\swinfo.json`. Open the window from the app bar at the KSC, in the VAB or in flight.

## Settings

In the mod settings: whether new VAB parts start in the agency colors, whether the stock flag setting and VAB agency colors button open the Rebrand page (turn this off if another mod manages your agency's identity), and the saved window position and size (set them to -1 to reset).

## For mod authors

`AgencyHeritage.HeritageApi` is the supported way to read the agency's history. The rest of the assembly may change in any release. `HeritageApi.ApiVersion` goes up if anything in it changes or is removed.

| Member | |
|---|---|
| `IsReady` | True while a campaign is loaded and its history is ready. |
| `CurrentEra`, `Eras`, `EraAt(ut)` | `EraInfo` copies: `Id`, `Number`, `Name`, `FlagSpriteLocation`, `Base`, `Accent`, `Motto`, `StartUT`, `EndUT` (+∞ for the current era), `IsCurrent`. |
| `event Rebranded(previous, current)` | The player rebranded. |
| `event Loaded()` | A save's history is ready (a save loaded, or a new campaign was founded). History is per save, so follow both events. |

With a hard dependency (reference `AgencyHeritage.dll` and add `AgencyHeritage` to your swinfo `dependencies`):

```csharp
AgencyHeritage.HeritageApi.Rebranded += (previous, current) => Repaint(current.Base, current.Accent);
```

Without one, look the type up by name, so your mod still runs when Agency Heritage isn't installed:

```csharp
var api = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("AgencyHeritage.HeritageApi", false)).FirstOrDefault(t => t != null);
var era = api?.GetProperty("CurrentEra")?.GetValue(null);   // an EraInfo; read its public fields
```

Listeners are called one at a time, and an exception in one is logged without affecting the others or the game.

See [ROADMAP.md](ROADMAP.md) for what's planned, including multiplayer and timeline entries from other mods.

## Translations

All text is in `Copied/localizations/agency_heritage.csv`, one column per language. The `Desc` column says where each string appears and what its `{0}` placeholders hold; translations must keep the placeholders. Keys ending in `.One` are used for exactly one (1 day), `.Many` for any other number. To add a language, add a column with exactly the name the game's own localization files use: `Italian`, `Korean`, `Polish`, `Chinese-Traditional [zh-TW]` and so on. A near miss such as `Chinese (Simplified)` instead of `Chinese-Simplified [zh-CN]` adds a separate, unnamed language to the game's language list. Keep the file UTF-8 with LF line endings, and quote any text containing a comma.

Planet, moon and landmark names come from the game's own translations.

## Building from source

This mod lives in a shared Unity project with my other KSP2 Redux mods. Clone the whole repo and open it with the [Redux SDK Manager](https://github.com/KSP2Redux/Redux-SDK-Manager/releases/latest) (Unity 6000.4.1f1, KSP2 Unity Tools `beta-6`), which imports KSP2 into it. This mod is in `Assets/AgencyHeritage`. Build and test it from `swinfo.asset` in the Inspector or from the Redux SDK window (**Test In Editor**, **Test In Player**, **Deploy To Zip File**).

**Releasing:** bump `version` in `swinfo.asset` and the CHANGELOG, then run **Deploy To Zip File**. The build writes `swinfo.json` only to ignored output folders, so copy the generated file over `Assets/AgencyHeritage/swinfo.json` and commit it. In-game update checks read that copy from `main`, so push it when the release goes out, not before.

## License

MIT. See [LICENSE.md](LICENSE.md).
