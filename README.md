# Schneider21's KSP2 Redux mods

A KSP2 Redux SDK Unity project holding all my mods. Each mod is in its own folder under `Assets/` and is built and released separately.

| Mod                   | Folder                                                     | Status                |
| --------------------- | ---------------------------------------------------------- | --------------------- |
| Transfer Window Redux | [`Assets/TransferWindowRedux`](Assets/TransferWindowRedux) | 1.0.0                 |
| Agency Heritage       | [`Assets/AgencyHeritage`](Assets/AgencyHeritage)           | 1.0.0                 |
| Wayfarer's Wings Redux | [`Assets/WayfarersWingsRedux`](Assets/WayfarersWingsRedux) | 0.5.0                 |

## Setup

1. Install the [Redux SDK Manager](https://github.com/KSP2Redux/Redux-SDK-Manager/releases/latest). It sets up Unity 6000.4.1f1 and KSP2 Unity Tools `beta-6`.
2. Clone this repo and open it as a Redux project. The SDK imports KSP2 from your own install. Nothing from the game is committed here.
3. Build, test and deploy each mod from its `swinfo.asset` or from the Redux SDK window.

`Assets/Editor/TestingAssemblyStaging.cs` works around a Test-in-Editor hang. See the Transfer Window Redux README for details.

`Assets/Editor/DeployZipFolder.cs` makes **Deploy To Zip File** put each mod in its own folder inside the zip (`<ModId>/swinfo.json`, …), so players can unzip straight into `mods\`. It rewrites the deploy staging paths from `Deploy/<ModId>` to `Deploy/<ModId>/<ModId>`, and reapplies whenever KSP2 Unity Tools regenerates the pipelines (also under **Tools → Put Deploy Zip Contents in a Mod Folder**). Don't keep `.gitkeep` files under a mod's `Copied/` folder: they end up in the zip, and Windows' built-in extractor fails on them.

Each mod has its own license; see its folder.
