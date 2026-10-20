# Changelog

## 1.0.0 (2026-10-20)

First release: a port of schlosrat's [Flight Plan](https://github.com/schlosrat/FlightPlan) 0.10.7 to KSP2 Redux. Requires KSP2 Redux 26w42c or later.

- Window rebuilt on the stock window template with the original's layout and icons (situation, target picker for bodies, vessels and docking ports, icon tabs, maneuver buttons with their values beside them), resizable, with its position and size saved. All text localized (English, German, French, Spanish, Portuguese, Russian, Japanese, Chinese).
- Node Manager's helpers are built in; no other mod is needed.
- Preview of each burn (time, Δv, orbit after) before making the node. New nodes go after existing ones, planned from the orbit those leave you on, or replace them.
- Target tab suggests the next step toward a vessel (match planes, Hohmann transfer, match velocity, …).
- Nodes tab: list, nudge, move in time, warp to, delete, and K2-D2 Execute when K2-D2 is installed.
- Inputs are kept per vessel in each save, instead of carrying over between saves.
- The node accuracy check now runs (it never did in the original), and checks Pe against Pe.
- Public API (`FlightPlanApi`) for other mods, including Node Manager-style node creation.
- Stays idle in campaigns that don't include it (Redux campaign features), and is named and described in the Mods screen and the campaign feature editor.
- Not ported: the unreleased advanced (porkchop) transfer and the Maneuver Node Controller hand-off.
