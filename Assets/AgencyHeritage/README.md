# Agency Heritage

A KSP2 Redux mod that makes rebranding your agency part of its history. Each name, flag and set of colors becomes a **brand era**. You can walk back through the eras in a museum-style window.

- **Era rail and timeline:** every era with its dates, launches, firsts and crew. Losses are marked on the timeline.
- **Collection:** the blueprints saved during each era, shown as they looked at the time.
- **Crew:** the kerbals who joined in each era, with lost and returned veterans marked. If [Wayfarer's Wings](https://github.com/Kerbalight/WayfarersWings) is installed, their ribbons show too.
- **Rebrand:** pick a new name, flag and colors. The old era is archived. The rebrand is cosmetic and free.
- **Start in agency colors:** new parts in the VAB start in your agency's colors (can be turned off in the mod settings).

History is stored in each save, so loading an older save shows the history as it was then.

**Status:** in development (0.1.0). Not yet released.

## Building from source

This mod lives in a shared Unity project with my other KSP2 Redux mods. Clone the whole repo and open it with the [Redux SDK Manager](https://github.com/KSP2Redux/Redux-SDK-Manager/releases/latest) (Unity 6000.4.1f1, KSP2 Unity Tools `beta-6`), which imports KSP2 into it. This mod is in `Assets/AgencyHeritage`. Build and test it from `swinfo.asset` in the Inspector or from the Redux SDK window (**Test In Editor**, **Test In Player**, **Deploy To Zip File**).

Commit `swinfo.json` when **Deploy To Zip File** regenerates it, because version checking reads it from the repo.
