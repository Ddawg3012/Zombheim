# Zombheim

**Valheim, overrun by the Knox Event.** A Valheim × Project Zomboid mashup: you play Valheim, and Project Zomboid's rules and gear come in with you, read from your own Project Zomboid install every time you play.

## What you get

- **"This is how you died."** Your first spawn in a world opens on Project Zomboid's own intro line, next to an abandoned crate of real Zomboid supplies: a Baseball Bat, Beans and Bandages.
- **Real Project Zomboid items.** Names, icons and stats (damage, weight, hunger, calories) come straight from your Project Zomboid files (`media/scripts`, its English translations and its texture packs). Nothing from either game is shipped with the mod.
- **Shamblers.** Slow, near-blind dead with sharp hearing (Valheim's Draugr bodies, Zomboid's rules). The first stragglers shuffle out of the treeline within a minute of your start.
- **Noise draws the dead.** Running, fighting and building make noise; shamblers who hear it come for you.
- **Night hordes.** Three waves come after every nightfall. Each player's game spawns its own waves, so the horde grows with your group.
- **Wounds and the Knox infection.** Shambler hits scratch, lacerate or bite. Lacerations and bites bleed until you use a Bandage. Bites always infect, and lacerations sometimes do. The infection is silent at first, then you go Queasy, Nauseous, then Fever. There is no cure, and if you die infected, something gets back up where you fell.
- **Solo or with friends.** Up to 10 Vikings, using Valheim's own hosting ("Start server") and Join game. Everyone needs Zombheim.

## Requirements

- Valheim (the game you play) with BepInEx 5 (Melty installs it).
- Project Zomboid (Build 41 or 42) installed on the same PC. Melty gives Zombheim its folder at launch; without Melty, set `ProjectZomboidFolder` in `BepInEx/config/gg.melty.zombheim.cfg`.

## How it's built

A BepInEx 5 plugin (C#, .NET Framework 4.7.2). Every game system lives in a design sheet (`sheets/*.json`): items, enemies, wounds, infection stages, night waves, tuning and the Harmony hooks. `tools/sheets.py` preflights the sheets (every cell filled, every cross-reference resolved, every hook handler present) and generates `src/Zombheim/Generated/Sheets.g.cs`. Valheim's classes are reached by name at runtime, so if a game update moves something, the mod logs a warning instead of crashing.

```
./build.sh                                          # preflight, generate, compile, package dist/Zombheim-<version>.zip
dotnet run --project tests/ParserProbe -c Release   # offline checks of the Project Zomboid readers
```

## Credits

- Zombheim: built with Claude Code for Melty. MIT licensed (see LICENSE); remixes welcome.
- Valheim by Iron Gate. Project Zomboid by The Indie Stone. Both games' content is read from the player's own copies and is never redistributed.
- BepInEx and HarmonyX (installed by Melty, not bundled).
- Project Zomboid .pack layout as documented by [pz-pack](https://github.com/ScottyThePilot/pz-pack).
