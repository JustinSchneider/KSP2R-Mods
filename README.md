# Schneider21's KSP2 Redux mods

A KSP2 Redux SDK Unity project holding all my mods. Each mod is in its own folder under `Assets/` and is built and released separately.

| Mod                   | Folder                                                     | Status                |
| --------------------- | ---------------------------------------------------------- | --------------------- |
| Transfer Window Redux | [`Assets/TransferWindowRedux`](Assets/TransferWindowRedux) | 1.1.0                 |
| Agency Heritage       | [`Assets/AgencyHeritage`](Assets/AgencyHeritage)           | 1.1.0                 |
| Wayfarer's Wings Redux | [`Assets/WayfarersWingsRedux`](Assets/WayfarersWingsRedux) | 0.6.0                 |
| Flight Plan Redux     | [`Assets/FlightPlanRedux`](Assets/FlightPlanRedux)         | 1.0.0                 |

## Setup

1. Install the [Redux SDK Manager](https://github.com/KSP2Redux/Redux-SDK-Manager/releases/latest). It sets up Unity 6000.6.0f1 and the Redux SDK package (which includes KSP2 Unity Tools), pinned to snapshot `26w42c` in `Packages/manifest.json`.
2. Clone this repo and open it as a Redux project. The SDK imports KSP2 from your own install. Nothing from the game is committed here.
3. Build, test and deploy each mod from its `swinfo.asset` or from the Redux SDK window.

`Assets/Editor/TestingAssemblyStaging.cs` works around a Test-in-Editor hang. See the Transfer Window Redux README for details.

`Assets/Editor/DeployZipFolder.cs` makes **Deploy To Zip File** put each mod in its own folder inside the zip (`<ModId>/swinfo.json`, …), so players can unzip straight into `mods\`. It rewrites the deploy staging paths from `Deploy/<ModId>` to `Deploy/<ModId>/<ModId>`, and reapplies whenever KSP2 Unity Tools regenerates the pipelines (also under **Tools → Put Deploy Zip Contents in a Mod Folder**). Don't keep `.gitkeep` files under a mod's `Copied/` folder: they end up in the zip, and Windows' built-in extractor fails on them.

## Conventions every mod follows

- **Stay dormant in campaigns that leave the mod out.** Since Redux 26w42a each campaign picks the mods it plays with, but every installed mod still loads. Redux disables a disabled mod's patches, content, campaign hooks, settings and app bar buttons, but not code the mod runs itself. Check `KerbalMod.Enabled` in the scene/campaign gate, game-message handlers and Harmony patches. Static code, such as the public API (which then reports not ready), checks `ActiveFeatures.IsModEnabled(ModGuid)`. Keep loading and saving the save data either way, so a campaign's data round-trips untouched. Reference: [Campaign features](https://docs.rendezvous.dev/s/0a4a3e31-0b40-48e0-bd81-e14856f80d3d/doc/campaign-features-9YIewcIArR), [Code modding guides](https://docs.rendezvous.dev/s/0a4a3e31-0b40-48e0-bd81-e14856f80d3d/doc/code-modding-guides-SSQ735ZM52).
- **Name the mod in Redux's feature lists.** Add `Feature/<ModId>/<ModId>/Name` (the mod's name, untranslated) and `Feature/<ModId>/<ModId>/Description` to the mod's localization CSV. Without them the Mods screen's Default Features toggle shows the raw id `<ModId>:<ModId>`.
- **Check the other mod through its bridge.** A bridge's `IsAvailable` means installed *and* enabled in the loaded campaign.
- **Load campaign content through `Loading.AddSetLoadingAction`**, registered in `OnPreInitialized`. Redux runs it in each feature set that enables the mod and calls its reset first. At the main menu only core features are active, so a direct load by label at startup finds nothing. Use `Loading.AddAddressablesLoadingAction` only for small assets every campaign needs, such as UI.

Each mod has its own license; see its folder.
