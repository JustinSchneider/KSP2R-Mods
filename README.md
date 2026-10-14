# Schneider21's KSP2 Redux mods

A KSP2 Redux SDK Unity project holding all my mods. Each mod is in its own folder under `Assets/` and is built and released separately.

| Mod | Folder | Status |
| --- | --- | --- |
| Transfer Window Redux | [`Assets/TransferWindowRedux`](Assets/TransferWindowRedux) | 0.1.0, getting ready for release |
| Agency Heritage | [`Assets/AgencyHeritage`](Assets/AgencyHeritage) | 0.1.0, in development |

## Setup

1. Install the [Redux SDK Manager](https://github.com/KSP2Redux/Redux-SDK-Manager/releases/latest). It sets up Unity 6000.4.1f1 and KSP2 Unity Tools `beta-6`.
2. Clone this repo and open it as a Redux project. The SDK imports KSP2 from your own install. Nothing from the game is committed here.
3. Build, test and deploy each mod from its `swinfo.asset` or from the Redux SDK window.

`Assets/Editor/TestingAssemblyStaging.cs` works around a Test-in-Editor hang. See the Transfer Window Redux README for details.

Each mod has its own license; see its folder.
