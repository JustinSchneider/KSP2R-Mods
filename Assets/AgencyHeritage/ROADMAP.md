# Roadmap

Plans for Agency Heritage after 1.0, roughly in order. Nothing here is promised by a date. If you'd like one of these sooner, or want something that isn't listed, open an issue.

## Multiplayer: SrmillaMP

The goal is for [SrmillaMP / KSP2 Multiplayer Redux](https://spacedock.info/mod/4610) sessions to keep a proper history. That mod is in early alpha, and 1.0 hasn't been tested with it. Agency Heritage is built around one agency per save. In a shared universe, these questions need answers first:

- **Whose launches count.** Every player's craft is in the shared universe, so a friend's launch may be recorded as your agency's mission. Flights should be credited to the player who launched them.
- **Where history lives.** Agency Heritage keeps its history in your local save, but the server holds the shared universe. History should survive joining, leaving and rejoining a server.
- **Shared or separate agencies.** Either each player keeps their own eras, or a server's players share one agency and see its rebrands live. This depends on how SrmillaMP handles agencies as it matures.

Until then, playing with SrmillaMP may record other players' flights in your history.

## More for other mods

1.0 has a small read-only API: the agency's eras and events for rebrands and loaded saves. See *For mod authors* in the README. Next:

- **Contributed timeline entries.** Other mods could add their own milestones to an era's timeline (title, date, world), saved with the history. Planned when another mod wants it, so the format fits a real use.
- **Orbital Survey.** Fully mapping a world with [Orbital Survey](https://spacedock.info/mod/4501) would appear as a first in the era's timeline. Optional, like Wayfarer's Wings: nothing changes if it isn't installed.
- **Wayfarer's Wings on Redux.** Ribbons are read from the original Wayfarer's Wings. The [Redux port](https://github.com/profesorabbott/WayfarersWingsRedux) hasn't published a build yet. Once it does, check that ribbons still show, and adapt if its internals moved.

## Collection

- **Re-livery.** Show past eras' blueprints repainted in the colors of the era they're viewed from, or show today's craft in an old livery.
