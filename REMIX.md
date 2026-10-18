# Remixing Gears of Aion

Gears of Aion is GPL-3.0-or-later. You can change it and share your remix. Keep the credit ("Made by yoisjdawg"), the license, and both games' copyright lines.

## Change the design without touching code
Everything a designer would tweak is in `sheets/*.json`:

- `classes.json`: the eight classes, their heroes, HP/MP, basic attacks, block/parry/resist.
- `skills.json`: every skill: kind, element, power, ranks, cooldown, cast time, crowd control, chains.
- `enemies.json`: every Locust: HP, damage, speed, armour, weakness, resistance, special behaviour, title.
- `waves.json`: the campaign: how many of each Locust, extras, and the boss of each wave.
- `characters.json`: which Gears meshes and animations each hero and Locust uses.
- `sounds.json`: which Gears Wwise events play for each moment.
- `arena.json`: street props scattered through the city.
- `systems.json`: everything else (the city, flight, evasion, chains...).

After editing, run the preflight and code generator. It checks every row, and every Gears path against your install:

```
python -I tools/sheets.py generate --gears-files <list of install files> --gears "<Gears Content folder>" --aion "<AION 2 folder>"
```

It also writes `build/sheets-report.txt`.

## Sheet mods (no build needed)
Players can tune the game without building it. Put JSON files in `%LOCALAPPDATA%\GearsOfAion\mods`, in the same shape as the sheets:

```
{ "sheet": "enemies", "rows": [ { "id": "wretch", "hp": 120, "speed": 6.5 } ] }
```

- A row whose key matches a sheet row (its first column: `id`, `level` or `wave`) changes only the columns you give.
- A new key adds a row, starting from the sheet's first row.
- The game reads the folder at start and again whenever a file changes, so you can tune while you play. New spawns and casts use the change.
- Mistakes show in the problems list (F8).
- Melty's test runs ignore mods.

## Tools
- `python -I tools/balance.py`: a skill balance spreadsheet (`build/skill-balance.csv`).
- `python -I tools/changelog.py <version>`: a changelog from the roadmap.
- `sh tools/tour.sh "<Gears Content>" "<AION 2>"`: screenshots of every class and mode.
- `tools/Probe`: reads the Gears install: meshes, textures (`tex`), sounds (`sounds`), map cells (`survey`, `ground`).
- In game: `--viewer` (characters and their clips), `--benchmark`, the Practice Range (F7 previews effects), and the big map's spawn-point editor (N).
- Translations: run with `--dump-strings` to get `lang-template.json` in `%LOCALAPPDATA%\GearsOfAion`. Translate the values and save the file there as `lang.json`.

## Building
See README.md (.NET 10, patched CUE4Parse, ooz and vgmstream in `native/`). `sh tools/package.sh <version>` makes the release zip.

## The rules
Never ship anything from either game: the program reads the player's own installs. Don't add anti-cheat bypasses, and don't decrypt AION 2.
