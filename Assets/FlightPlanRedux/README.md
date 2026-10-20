# Flight Plan Redux

A KSP2 Redux port of [Flight Plan](https://github.com/schlosrat/FlightPlan) by schlosrat. Plan your flight, fly your plan: pick a maneuver and when to burn, check the preview, and Flight Plan makes the maneuver node.

In development.

- **Orbit:** circularize; new periapsis, apoapsis, both, inclination, LAN or semi-major axis. Burn at Ap, Pe, an altitude, equatorial AN/DN or after a set time.
- **Target:** match planes, Hohmann transfer, course correction and match velocity, for a vessel or a moon. Readouts for distance, closest approach, relative speed and inclination, phase angle and the next window.
- **Moon:** return to the planet with a chosen periapsis.
- **Planet:** interplanetary transfer at the next window or as soon as possible, with phase angle, transfer time and ejection Δv.
- **Resonant:** deployment orbit for an evenly spaced satellite constellation.
- **Nodes:** the vessel's nodes; nudge prograde, normal, radial and time; warp to a node; delete. With [K2-D2](https://github.com/IanMealworm/K2D2Redux) installed, Execute has K2-D2 fly the first node.

New nodes go after the ones already planned, starting from the orbit those leave you on, or replace them. Each new node is checked against the plan. What you type is kept per vessel in each save. The window uses the stock window template.

Not ported: the advanced (porkchop) transfer, which the original never released, and the Maneuver Node Controller hand-off (the Nodes tab does that job now).

## Installing

Requires KSP2 Redux 26w42c or later. Unzip `FlightPlanRedux.zip` into the game's `mods` folder, so you end up with `mods\FlightPlanRedux\swinfo.json`.

## Settings

In the mod settings: the window keybind (Alt+P), experimental features (the intercept maneuver), and the error thresholds for the node check.

## For mod authors

`FlightPlanRedux.FlightPlanApi` is the supported way in; the rest of the assembly may change in any release. `ApiVersion` goes up if anything in it changes or is removed. It also stands in for Node Manager, which has no working Redux port.

| Member | |
|---|---|
| `IsReady`, `IsOpen`, `Open()`, `Close()` | The window. |
| `OpenTab(name)` | "Orbit", "Target", "Moon", "Planet", "Resonant" or "Nodes". |
| `OpenTransfer(bodyName)` | Targets a body (internal name, e.g. "Duna") and opens the tab that plans the transfer. |
| `Nodes()` | The active vessel's nodes as `NodeInfo` copies: `Index`, `Id`, `UT`, `BurnVector`, `BurnDuration`. |
| `CreateNodeAtUT(burnVector, ut, burnOffsetFactor = -0.5, done = null)` | Adds a node after the existing ones. The burn is in node coordinates (radial, normal, prograde); -0.5 centres it on `ut`. |
| `DeleteNode(index)`, `DeleteNodes()`, `DeletePastNodes()` | Removes nodes. |
| `event NodesChanged()` | Nodes were added, removed or changed through Flight Plan. |

Look the type up by name so your mod still runs without this one:

```csharp
var api = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("FlightPlanRedux.FlightPlanApi", false)).FirstOrDefault(t => t != null);
api?.GetMethod("OpenTransfer")?.Invoke(null, new object[] { "Duna" });
```

[Transfer Window Redux](../TransferWindowRedux) uses `OpenTransfer` for its Flight Plan button.

## License

GPL-3.0, like the original. It includes code from MechJeb2 (GPL-3.0), ALGLIB (GPL-2.0-or-later) and schlosrat's Node Manager (MIT). See [LICENSE.md](LICENSE.md).
