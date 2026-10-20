# Transfer Window Redux

An in-game Hohmann transfer calculator for KSP2 Redux. Open it from the app bar in flight or map view, pick a destination (or target a planet or moon the usual way), and it shows:

- the current and required phase angles, the time and date of the next window, and the one after it
- the transfer time and the relative inclination between the two orbits
- the ejection Δv and ejection angle, the capture Δv into a low orbit at the target, and how the total compares with your vessel's Δv

**Create node** places the departure burn as a maneuver node, ready to fine-tune. **Warp to window** stops shortly before the window, or before your first node if it comes earlier.

This is a port of [Transfer Window](https://github.com/ABritInSpace/TransferCalculator-KSP2) by ABritInSpace (MIT), with their blessing.

## Installing

Requires KSP2 Redux 26w42c or later. Unzip `TransferWindowRedux.zip` into the game's `mods` folder, so you end up with `mods\TransferWindowRedux\swinfo.json`.

## Changes from the original

- Ported from BepInEx/SpaceWarp 1 to Redux's loader (`KerbalMod`), with the IMGUI window rebuilt in UI Toolkit on the stock window template (the same frame, header and resize handle as the game's own windows).
- Works for moons as well as planets (original issue #23). Transfers are calculated from whatever orbits the target's parent: your planet for interplanetary targets, or your vessel for a moon of the body you orbit.
- A destination picker lists every body you can transfer to from where you are, including the planet a moon orbits.
- Returns from a moon: an escape burn backwards along the moon's orbit, onto a path whose periapsis dips halfway into the planet's atmosphere for reentry (or reaches a parking orbit at airless planets). The window comes round once per orbit.
- Maneuver node creation, ejection angle, capture Δv, a Δv budget, and the next window.
- Works on the launchpad: when you aren't in orbit, departure figures assume a low parking orbit (10 km above the surface or atmosphere; 80 km at Kerbin).
- The phase angle is measured in the origin's direction of travel. It no longer depends on the game's axis conventions.
- Calculations use doubles and the bodies' gravitational parameters. The original cast to `float` and hard-coded G.
- Warping uses the game's `TimeWarp.WarpTo` instead of stepping through warp rates frame by frame.
- Translated into German, French, Spanish, Brazilian Portuguese, Russian, Japanese and Simplified Chinese. These are first drafts: corrections from native speakers are very welcome.

## Settings

In the mod settings: the parking/capture orbit margin, the return periapsis (-1 picks one automatically), the warp lead time, and the saved window position and size (set them to -1 to reset).

## Assumptions and limits

- Like the original, it assumes roughly circular, coplanar, prograde orbits. Expect inaccurate figures for eccentric or inclined targets, such as Eeloo or Moho; the inclination row turns amber above 2°.
- Ejection Δv and node placement aren't available when you orbit a moon of the departure planet (an escape through two spheres of influence), or when you aren't in orbit.
- The node is a single prograde burn. Expect to fine-tune it for an encounter, especially for inclined targets.

## Translations

All text is in `Copied/localizations/transfer_window_redux.csv`, one column per language. The `Desc` column says where each string appears and what its `{0}` placeholders hold; translations must keep the placeholders. To add a language, add a column with exactly the name the game's own localization files use: `Italian`, `Korean`, `Polish`, `Chinese-Traditional [zh-TW]` and so on. A near miss such as `Chinese (Simplified)` instead of `Chinese-Simplified [zh-CN]` adds a separate, unnamed language to the game's language list. Keep the file UTF-8 with LF line endings, and quote any text containing a comma.

## Development notes

Test in Editor stages the DLL into `Assets/Mods/__Testing`, where Unity imports it as a plugin next to the asmdef that built it. That logs "has the same filename as Assembly Definition File" and starts a recompile that the build then waits on, so it hangs. In the Editor, SpaceWarp2 uses the already-loaded assembly and never reads that file, so the project's `Assets/Editor/TestingAssemblyStaging.cs` redirects each mod's editor assembly staging to `__Testing/<id>/bin~`, which Unity ignores. It reapplies after Refresh Thunderkit Pipelines; there's also a **Tools → Redirect Test-in-Editor Assembly Staging** menu item.

## Building from source

This mod lives in a shared Unity project with my other KSP2 Redux mods. Clone the whole repo and open it with the [Redux SDK Manager](https://github.com/KSP2Redux/Redux-SDK-Manager/releases/latest) (Unity 6000.4.1f1, KSP2 Unity Tools `beta-6`), which imports KSP2 into it. This mod is in `Assets/TransferWindowRedux`. Build and test it from `swinfo.asset` in the Inspector or from the Redux SDK window (**Test In Editor**, **Test In Player**, **Deploy To Zip File**).

**Releasing:** bump `version` in `swinfo.asset` and the CHANGELOG, then run **Deploy To Zip File**. The build writes `swinfo.json` only to ignored output folders, so copy the generated file over `Assets/TransferWindowRedux/swinfo.json` and commit it. In-game update checks read that copy from `main`, so push it when the release goes out, not before.

## License

MIT, the same as the original. See [LICENSE.md](LICENSE.md).
