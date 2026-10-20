# Changelog

## 0.6.0 (2026-10-20)

- Requires KSP2 Redux 26w42c or later.
- Ribbon definitions load with each campaign that includes the mod (Redux campaign features).
- Stays idle in campaigns that don't include it: no ribbons are awarded, and `WingsApi.IsReady` is false. The ribbons already in the save are kept as they are.
- The filter and sort menus are as wide as their longest choice.
- Named and described in the Mods screen and the campaign feature editor.

## 0.5.0 (2026-10-15)

First release of the Redux port. The version carries on from the original's 0.4.0.

- Ported [Wayfarer's Wings](https://github.com/Kerbalight/WayfarersWings) 0.4.0 by leonardfactory to KSP2 Redux: award logic, ribbon definitions and art, and per-save storage.
- New windows built on the stock window template: Kerbals (roster and records), Ribbons (gallery) and the mission summary.
- New `WingsApi` for other mods to read ribbons and hear when they're awarded.
