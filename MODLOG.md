# Daevas of E-Day: mod log

## Goal (agreed with the creator, 2026-10-12)
- **What it is:** a separate program that plays from the player's own Gears of War: E-Day install.
- **From Gears:** the real guns (meshes and textures), read from the install while the game runs.
- **The fighting:** AION-style, built by us. The player picks 1 of 8 classes at the start. There's a skill hotbar with cooldowns next to the Gears weapons, XP from kills, level-ups that unlock and upgrade skills, and AION-style damage numbers.
- **From AION 2:** its real `Splash.bmp`, read from the player's AION 2 install, on the title and class-select screens.
- **Players:** solo only.
- **License:** credit the creator, free to remix.

## Why a separate program
- Gears of War: E-Day can only be started through Easy Anti-Cheat: `MicrosoftGame.config` lists only `start_protected_game.exe`. We never touch the running game.
- AION 2's paks are AES-encrypted. 230 of its 267 IoStore containers have the encryption flag, and every pak index is encrypted. We don't extract keys, so its combat data can't be read.
- Only these AION 2 files are loose: `Aion2/Content/Splash/Splash.bmp`, `Movies/*.bk2` (Bink) and `Movies/PatchDownload/PatchDownload.mp4`.

## Gears of War: E-Day facts
- **Install:** `C:\XboxGames\Gears of War- E-Day\Content`. The archives are in `FairlightConcept\Content\Paks`: 89 `.utoc` files, none encrypted, Oodle compression.
- **Engine:** UE 5.6 with Coalition changes. CUE4Parse supports it as `EGame.GAME_GearsofWarEDay`.
- **Properties:** they are unversioned, and there is no .usmap.
  - We patched our CUE4Parse checkout so that `Globals.AllowMissingMappings` lets custom-serialized data load without one.
  - StaticMesh LODs are found by scanning for the LOD array. See `FindLods` in `src/Daevas.Gears/GearsInstall.cs`.
  - CUE4Parse's whole-object parse fails near the export tail. That's a Gears-specific render-data tail, so we never rely on the full parse.
- **Oodle:** decoded by ooz (Powzix, GPL-3.0), built here as `native/ooz.dll` with llvm-mingw. No RAD Oodle DLL is shipped or downloaded.
- **Speed:** mounting the 895k-file index takes about 1 s. A gun part takes 1–100 ms, and a 512 px texture takes 40–300 ms.
- **Guns:** in `Fairlight/Weapons/COG/<Gun>/SM01/*_SM01` (static parts) and `Fairlight/Weapons/cog/<gun>/Textures/cog_<gun>_d` (albedo). `GP01` holds skeletal versions, which we don't read yet.
- **Skeletal meshes:** the brute-force property skip is far too slow for them, so they're avoided for now.

## Tools (all in `../.tools`, never shipped)
- .NET SDK 8 and 10 (dotnet-install.ps1), MinGit 2.56, llvm-mingw 20261006.
- CUE4Parse at git HEAD, with our patches recorded in `third_party/cue4parse.patch`.
- universal-modder (skills only), ooz source.

## v0.2.0 (2026-10-13): real Gears characters and animations, gamepad
- **Skeletal meshes:**
  - `FindSkeletal` scans for the reference-skeleton bone array: count, then the root (FName, parent -1), then the next bone (FName, parent 0).
  - From there it reads bCooked, Gears' extra block (bulk + uint array + entries + 32 bytes) and the LOD render items.
  - The materials array is found by walking back from the skeleton until it parses exactly to it.
- **Textures:**
  - Matched by name: `<material>_d`, with trailing digits dropped and `wpn_` stripped.
  - UDIM characters use `<char>_mat_<1001+tile>_d`, with the tile taken from the section's mean U.
- **Full guns:** `*Geo_GP01` skeletal meshes are the full-detail guns, for example the Lancer at 15k triangles. `COG_<gun>_GP01` is an invisible 12-triangle placeholder, and the Gnasher only has a GeometryCollection, so it keeps its SM01 parts.
- **Animations:**
  - The ACL stream is found by the tracks-header tag 0xAC11AC11 at buffer+8, preceded by [size][bulk=0].
  - The track-to-bone table is found by walking back for [numTracks][numTracks ints].
  - Decoded with CUE4Parse-Natives ACL, built with clang plus `-Wno-error=missing-template-arg-list-after-template-kw`.
  - Skeletons: Biped_Skeleton (pm_*, drone), Wretch_Skeleton and Locust_Boomer_GP01_Skeleton.
  - Retargeting by bone name: animation rotations, mesh bone lengths and pelvis height scaled. Root drift is removed.
- **Baking:** animations are baked into frames at load, about 8-28 frames per clip, so there's no per-frame skinning.
- **Background mode:** `--background` is a hidden, unfocused, muted window that ignores the gamepad. Use it for test runs while the creator plays.

## v0.3.0 (2026-10-13): Gears arena and a hero per class
- **Static meshes:** materials are the package's material imports, ordered by the first byte offset where -(importIndex+1) appears in the export's property bytes. Section MaterialIndex indexes that list.
- **Texture name fallbacks:** strip `_mic`, `_ws`, `_<x>pm_01a` and `_leaks`, try sibling letter variants, then the shortest texture name with that stem. UDIM is tried before the stem match.
- **Arena (`sheets/arena.json`):**
  - Historic_fort_bailey_wall_merged_v03 is a ready-made 66 x 68 x 14 m ring wall.
  - Cover is the dry_dock_cover_v0x pieces and the SM_MERGED_*_Destroyed_Burnt vehicles.
  - Collision is the mesh footprint after a 90-degree turn.
  - The ground is Tiling/Textures/aggregate_concrete_01a_d, tiled.
- **Heroes:** each class has a hero. They load when the class deploys, because preloading 8 heroes would cost about 300 MB of VRAM. Every hero uses the Biped pm_ar animations.

## v0.4.0 (2026-10-13): Gears sounds and a fort courtyard
- **Gun sounds:** each Wwise event (`WwiseAudio/Events/Fairlight_Events/...`) imports its media as `/Game/WwiseAudio/Media/xx/yy/<id>`, and the media `.ubulk` is a RIFF WEM. Gun media are 16-bit PCM (fmt 0xFFFE) and are played directly.
- **Voice sounds:**
  - Voices are Wwise Vorbis (fmt 0xFFFF). The bulk holds the WEM (RIFF size + 8 bytes) followed by padding, so trim it before decoding or vgmstream reports "broken .wem".
  - They're decoded once with vgmstream-cli into `%LOCALAPPDATA%/DaevasOfEDay/sound-cache/<id>.wav`.
- **vgmstream build:**
  - Every prebuilt vgmstream DLL is a load-time import, including libg719_decode, whose licence is unclear.
  - So vgmstream r2117 is built with Vorbis only, using llvm-mingw and busybox make (see the README). It needs only `libvorbis.dll` (BSD) and the UCRT.
- **`sounds.maxLength`:** keeps the first N seconds of a recording with a 0.25 s fade-out. The Drone screams are 4-6 s, so they're kept to 1.8 s.
- **Arena:**
  - The merged bailey wall is a hillside piece, with up to 13 m of base drop, so it floated over the flat arena. It's replaced by a ring of 35 `fort_s01_wall_*` segments (7 x 5 m, pivot at one end; props are re-centred on their footprint) plus `fort_s02_gate_v01_01a`.
  - Props draw double-sided because Gears set pieces are one-sided. The shader flips the normal with `gl_FrontFacing`.
- **`FindAlbedo` fallback:** the first `_d` texture the material imports, or one imported by a parent material up to 2 levels. For example, fort_wall_01 uses PCF_Fort_Stone_Wall_01a_Multiscale_d.
- **`launches.log`:** `%LOCALAPPDATA%/DaevasOfEDay/logs/launches.log` gets one line per start and close, to diagnose Melty's run 2 "game didn't appear".

## v0.5.0 (2026-10-13): AION 2 combat, Active Reload and E-holes
- **AION 2 combat** (the creator asked for "aion 2 style combat"):
  - **Chain skills:** `skills.chainFrom` names the skill that opens the chain. Chain skills share their parent's class and slot and stay off `classes.skills`; the preflight checks all of this.
  - **Chaining rule:** a cast that lands opens the next link (`World.TryCast` counts `_castHits`; self skills always count). It stays open for `chain-skills.window` seconds in `Player.Chains[slot]`, and `ActiveSkill(slot)` returns it.
  - **Target lock:** `World.Locked`, cycled by `CycleLock` (enemies in front within `target-lock.range`). `LockedWithin(range)` takes priority in `Target()`, and the aim is redirected to it.
  - **Evasion:** `Player.EvadeTime/EvadeCd/EvadeDir`. `TakeDamage` returns "Evade" while rolling. The hero plays `cog/pm/evade/ar/pm_ar_evade_f0` (`characters.evadeAnim`, a new `Clip.Evade`) turned to the roll direction.
- **Active Reload:** `systems.active-reload` sets zoneStart 0.36, good 0.17, perfect 0.055, +30% for 6 s, and a 1.3 s jam.
  - The sounds come from the per-gun Wwise events `..._Reload_ClipEject/ClipInsert/Hit/Jammed`. Gnasher uses Open/Close/PerfectReload, and Longshot uses BoltOpen/BoltClose.
  - Each event has 5 PCM takes.
- **E-holes and deaths:**
  - New clips `Clip.Spawn` and `Clip.Death` are baked only when the sheet names a Gears animation, at 12 fps with up to 24 frames.
  - Emergence clips: Wretch `ehole/wretch_climb_ehole_a` (1.5 s), Drone `Ehole/drn_ehole_spawn_a` (3.8 s), Boomer `ehole/bm_bs_spawn_ehole_a` (5.0 s).
  - Death clips: `*_idle_death_front_body_a` / `drn_ar_death_idle_front_body_a`.
  - The Boomer's attack is `recoil/bm_hw_hip_idle_recoil`.
  - While emerging, an enemy can be shot but doesn't act.
- **Test checks:** two new ones, `chain-skill` and `evade`, for 10 in all. Autoplay locks a target, rolls away from shots and swings (and every 8 s near enemies), and aims for the perfect Active Reload.
- **Screenshots:**
  - They're read before the buffer swap, and the world pauses 20 frames before the capture.
  - `--shot-when reload|emerge|death|chain|evade` waits for that moment.
  - The argument loop skips the last argument, so flags with no value use `args.Contains`.
- **AION flight:**
  - `systems.flight`: 12 s of flight, 4.2 m up, rising at 5 m/s, refilling at 1.5 s per second on the ground, 1.25x speed.
  - In the air, `MoveCircle` skips boxes lower than you (the fort walls are 5 m tall, so they still hold). Melee attacks need `P.Pos.Y < 1.2`.
  - The wings are 7 feathers a side drawn with Rlgl, class-coloured fading to white, and they flap. Pad Y is now flight, so next gun is RB only.
  - New test check: `flight`, for 11 in all.
- **Credits** (the creator asked): only "Made by yoisjdawg" plus the AION 2 and Gears copyright lines. There's no Claude mention, and the library notices stay in the download.

## v0.6.0 (2026-10-13): Gears Siege Downtown, AION kits, guns off
The creator asked for a Gears look ("take the horde siege open world"), the guns turned off, and "full on" AION 2 skills.

**The map:**
- `Fairlight/Maps/Fairlight_World/Fairlight_Siege_Downtown_WP` is a World Partition map. Its `_Generated_/..._MainGrid_L0_X{i}_Y{j}[_d<layer>].umap` cells are 128 m squares centred on i*128 / j*128 (Gears metres).
  - HLOD0 cells hold no merged meshes.
  - The placements are in each cell's FastGeoContainer.
- **Mappingless FastGeo** (`GearsInstall.ReadFastGeo`):
  - The unversioned property block holds object-reference arrays ([count][package indices]). The first one is Assets, and the native ComponentClusters follow the last valid one.
  - CUE4Parse's FFastGeo* types then read the clusters.
  - For Gears, CUE4Parse skipped a bulk array on ISM components. The patch keeps it (`GearsInstanceData`): 48-byte elements, a 3x3 rotation-scale matrix in rows plus a translation relative to the component.
- **Layers:**
  - Base cells plus data layer `d175F5ED0` (84 cells) give the battle-damaged Siege dressing.
  - `d34B7FC44` duplicates the same buildings, so it isn't loaded.
  - The other `_d*` layers are small.
- **Centre and ground:**
  - The centre is (0, -255) m with ground at 229 m. Streets elsewhere slope by about 10 m (222-234), so the flat block was chosen.
  - Roads and sidewalks are blueprint/spline actors, not FastGeo, so a tiled Gears ground texture stands in.
- **Batching:** LOD0 (LOD1 reads looked wrong on some pieces). Meshes matching `glass|decal|...` are skipped. Batches are per texture per 32 m chunk with up to 60k vertices, about 5.3M triangles and 1,572 batches, still at full frame rate.
- **Cache:** per-cell placements go in `%LOCALAPPDATA%/DaevasOfEDay/city-cache/<cell>.bin`, keyed by a stamp of the .utoc names, sizes and dates. A cold start takes about 30 s, a warm one about 2 s.
- **Colliders:** placed-mesh AABBs that stand on the street (bottom < 1.2 m, height > 1 m, footprint < 900 m²).
- **Navigation:**
  - A 1.5 m flow field toward the player (Dijkstra every 0.4 s, no corner cutting).
  - Enemies need line of sight to attack, spawn only in reachable cells, and are moved if they stay unreachable for 4 s.
  - The player starts in the largest connected open area.
- **Dev tools:** `DAEVAS_CITYMAP=1` writes `logs/citymap.txt` (an ASCII map of street-level pieces). `--overview <yaw> <height> <dist>` gives a camera circling the centre. The probe has `cell`, `fastgeo`, `survey` and `ground` modes.

**Guns off:** `systems.guns.enabled` is false. Left mouse / RT is `World.BasicAttack` (classes.basicKind melee, bolt or arrow, basicDamage, basicRange, basicInterval). Guns aren't loaded, and the gun HUD and Active Reload are hidden.

**AION kits:**
- 8 skills per class (32 new rows). New kinds: leap, pull, drain, pet, stealth, trap, blink, guard, haste, regen, mana and salvation. Columns `castTime`, `cc` and `ccTime` were added.
- Crowd control: stun; knockback (push 10 m/s, decaying); knockdown (tilted); airborne (lifted 1.6 m); root (no moving); slow (half speed).
- Casting state is `Player.Casting`, and moving cancels it.
- The hotbar is 8 wide: keys 1-8, D-pad, and LT + D-pad for 5-8.
- Test checks: `crowd-control` and `basic-attack` were added; `active-reload` and `gears-guns-loaded` were removed while guns are off.
- Autoplay rotates its first slot each tick and stands still while casting.

## v0.7.0 (2026-10-14): street dressing, shadows, wings, weapons, feel
- **Street dressing:** arena sheet rows with role `street` and a `count` are scattered over reachable open cells, 7+ m of path from the start, with a 1.6 m lane kept around each, then the flow field is rebuilt. The rows are burnt vehicles, COG barricades and rubble piles. The ground is `Tiling/Textures/asphalt_damaged_01a_d`, tiled every 4 m (`systems.city.groundTileMetres`).
- **Camera:** a ray from the hero's head to the eye against the colliders pulls the camera in front of walls.
- **Sun shadows** (`Lit.ShadowPass` / `UseShadows`):
  - A 4096² depth framebuffer, rendered with an orthographic sun camera spanning 110 m around the player.
  - The lit shader takes `lightVP`, `shadowMap` (slot 10) and `useShadow`, with 3x3 PCF and a normal-scaled bias.
  - raylib sizes the viewport from the RenderTexture's colour texture, so it needs a width and height even when the target is depth only.
  - The sun is now lower: (-0.645, -0.645, -0.410). `DAEVAS_SHADOWDEBUG=1` shows the shadow term.
- **Wings:** a solid membrane from an arm (10 points, drooping at the wrist) to scalloped feather tips, with a bright leading-edge strip, drawn with Rlgl in class colour fading to a tint.
- **Hand weapons** (`classes.handWeapon` / `weaponLod`), drawn while the guns are off:
  - Chainsaw (`COG/Chainsaw/SM01`), PipeA, Dom's knife, Torque Bow, Wrench and Crowbar.
  - The Locust Glaive's bind pose is chained hooks, so it isn't used.
  - Static or skeletal, the grip stays at the mesh pivot, and the long axis is rotated (not mirrored) onto +x with the head first.
- **Feel:**
  - Particles (`Particles.cs`, soft round sprites, blended or additive): blood on hits and kills, class sparks on skill hits, rising motes on casts, E-hole dust and rock, evade dust, and wing sparkle.
  - Camera shake with controller rumble (`World.AddShake`, `Pad.Rumble`).
  - Pause on Esc, P or Back (resume, retry, class select).
  - The end screen has stats (kills, damage dealt and taken, skills, chains, time) and R / X to retry.
- **Reliability:** an enemy that is out of sight and not moving while it tries to is relocated after 4 s. Autoplay hunts the nearest enemy after 6 s with none in sight. All 8 classes reach every milestone.

## v0.9.0 (2026-10-17)
Published on Melty 2026-10-17 at the creator's "publish" (release f0256bbe, test 20e3aeeb passed 3/3 with footage reviews; listing renamed Gears of Aion, slug gears-of-aion). Follow-up: the autoplay camera can press against walls among rooftops (seen in test run 3, 02:34-03:00).
Gears of Aion: the rename, the logo and splash, AION 2-style wings, the memory fix, and ROADMAP100.md worked through (the
Abyss island and its life, grounded buildings, see-through fixes, walkable piles, stairs and wrecks, Rescue mode, the
Abyss gameplay, settings and stability). Details below and in ROADMAP100.md.
Benchmark (High, uncapped): 143 fps
average, 123 fps 1% low; capped by VSync, about 60 on Low, Medium and High.

Brand lockup "AION 2 logo x Gears of War: E-Day logo x yoisjdawg" on the splash, loading screen, title and credits
(Brand.cs). Both logos are read from the player's installs at start: AION 2's is cut out of its splash art (its sky
keyed clear), Gears' is its brand texture T_UI_logo_gears_eday. The HUD font now has the © sign. Dev flag
--overlay 1 opens the credits for screenshots.

Release material for 0.9.0: `media/GearsOfAion-trailer.mp4` (opens on the Abyss; `tools/trailer.sh` records an Abyss camera-path clip first), capsule art from the island in the void (`tools/capsule.sh`, `CAPSULE_CAM`), the district tour (`tools/edgetour.sh` -> `media/tour/`, `media/tour-sheet.png`). Fixed on the way: `DAEVAS_MAP` now applies after the saved profile loads.

## v0.8.0
- **Renamed to Gears of Aion** (2026-10-16, the creator's choice). The exe is `GearsOfAion.exe`, the data folder is `%LOCALAPPDATA%\GearsOfAion` (`Paths.DataDir`; the old DaevasOfEDay folder moves over once, `Paths.MoveOldData`). The app is installed in `%LOCALAPPDATA%\Programs\GearsOfAion` with the desktop shortcut "Gears of Aion". The Melty slug/modId stay as they are.
- **Logo** (`Logo.cs`, drawn from scratch): a Gears-style gunmetal cog (10 square teeth, bevelled rim, crimson heart, gold AION star) carried by feathered wings. `--logo <png> [--logo-plain] [--logo-size N]` exports it; `sh tools/icon.sh` makes `Assets/app.ico`; `tools/capsule.sh` lays it over the capsule art.
- **Splash** (`Splash.cs`): the loading screen. Light rays behind the logo, white feathers and Gears brass (casings and copper-tipped rounds) falling from the top, embers, credits. `--splash <png> [--splash-size WxH]` exports a still (`media/splash.png`).
- **Wings** (`Wings.cs`) after AION 2's Daeva wings (the creator's reference video): a high wrist, 102 feathers per side in six rows (primaries, secondaries, tertials, coverts), soft white with a hint of class colour at the tips, at 70% of AION's span (`Wings.Size`) so the hero stays in view. On the ground they fold side-on into a slim teardrop on each shoulder blade (the creator's sketch: a rounded wrist hump, the primaries making the point); they open and fold over a moment. Preview: `DAEVAS_VIEWERWINGS=<spread>` in the viewer, or `sh tools/wingview.sh [spread] [yaw]`.
- To do before listing: the `--capsule` hero shot now lands on a cramped ledge; pick a better spot.
- **Memory fix (2026-10-17):** the creator's PC (16 GB) froze at boot with the game at 11 GB (likely two copies launched while the first was "not responding"). Now: city meshes drop raylib's CPU vertex copies after upload, the decoded character textures are freed after loading, the .NET heap is compacted after loading and after streaming, upload loops pump window messages (`Responsive.Tick`, loading screens only), and a named mutex keeps one copy (a second launch brings the first to the front). Measured: steady 2.1-2.3 GB private (was 4.3-6.2), loading peak about 4.1 GB. `DAEVAS_MEMLOG=1` logs memory every 5 s, `DAEVAS_HIDE=1` runs a player's launch in a hidden window. `latest.log` falls back to `latest-<pid>.log` when another copy holds it.
- **No AION 2 splash on screen** (the creator's wish): title, mode, class and deploy screens use the Gears of Aion scene (`Splash.Backdrop`). AION 2's Splash.bmp is still read (the `aion-splash-loaded` check) but no longer shown; before publishing, decide with the creator how AION 2 shows in the mashup.
- **The Abyss and the city fixes (2026-10-17, ROADMAP100.md):**
  - `Abyss.cs`: the district stands on an island measured from its own footprint (`Measure`: chunk reach by angle, smoothed, plus 6 m), ringed with hanging Gears cliff meshes (TechTest/Props/Rock, loaded with the props), a generated rock underside, and 26 floating islands. The sky shader paints a violet void with nebulae and stars below the horizon (the old painted skyline is gone); the lit shader's fog turns to `Lit.AbyssColor` below the street. The distant ring (`StartFarRing`) is no longer started.
  - See-through walls: the lit shader had blended every texture's alpha (Gears packs other data in alpha). Now `alphaMode`: Opaque by default, Cutout for hard masks, Blended for largely-clear textures (glass, overlays), chosen per texture from its pixels (`Lit.AlphaFor(name, rgba)`); city chunks and props carry their mode. City chunks draw two-sided.
  - Props split sections over 65k vertices (they used to drop the rest: the see-through vehicle) and load textures at 1024.
  - Rubble/debris/dirt piles (`City.IsPile`) are `Mounds`, walked over as domes (`MoundTop` in FloorAt; Locust drawn on them); the movement self-test walks one.
  - Flight is clamped to the play area. Dev: `DAEVAS_CAM=ex,ey,ez,tx,ty,tz` aims the camera anywhere.
  - Testing now includes real launches (the creator's request): see memory.
  - Floating buildings, round 2: the island rim is measured from every standing piece's footprint (`CityData.Reach`, `Occupied` 2 m cells) during the build. Buildings that floated stood on raised plaza slabs (`Rectangle_*`, ~3 m up, on terrain the game doesn't load) or lacked their ground storey: `CityData.Plinths` fills under them (low raised floors, building blocks 1.6-7 m up, flat slabs) with a solid retaining wall (canal_wall_v01_01a, colliders in the play area). Name checks (`Overhead`, `IsPile`) match the file name only: folder names like "LevelDesign" used to match "sign".
  - The Abyss island: Gears red pines, dead trees, burnt bushes and grass (Gears foliage keeps leaf masks where we can't read them, so only maskless plants), a rocky soil band (rocky_soil_ground_mega_01a), glowing crystal clusters (`Lit.Glow`, made from scratch), contact shadows along building bases, tinted shop glass (`Lit.Glass`).
  - Floating buildings, round 3 (systematic): `City.FillFloatingColumns` checks every 2 m square near the fight; where non-overhead pieces stack high but the lowest starts above 1.2 m with nothing at street level, it adds foundation strips (and colliders). Downtown 388 strips, Historic 30, Industrial 354. The open play area shrank about 12% (the space under floating buildings was never real street); waves still clear in all three districts.
  - ROADMAP100 batch (41/100): drifting islands (`Abyss.Drift`, things on an island move with it), aether motes rising past the rim and chunks crumbling off it (`Abyss.Update/DrawGlows`), violet uplight on what hangs below the street and crystals brighter at night (lit shader, `weather.z` = night), a torn rim lip (rock chunks, procedural rebar), an edge shimmer where flight is stopped, the wingbeat following climb/glide and folded wings swaying with the stride, Wing size and Wings (Elyos white / Asmodian dark) settings, Abyss loading tips, the last crash shown once on the title (L opens the log folder; `crash-seen.txt`), street props on the real street height, an unstick nudge, and `tools/smoke.ps1` (a real launch under a memory watchdog).
  - ROADMAP100 to 46/100: wrecks collide as 4 slices shaped from their mesh (`WorldWrecks.cs`; the movement test climbs a police car to 1.85 m), ash drifting down and embers rising over the streets, street lamps that hum and (a third of them) stutter, cut out and spark, and the minimap shows the district's edge with the void beyond.
  - Dev: `DAEVAS_PIECESAT=x,z` lists the map pieces over a spot; `DAEVAS_CAMPATH` flies the camera through shots while recording.
- **Tickers** (`Locust/Ticker/GP01`, Ticker_Skeleton clips: idle, charge loop, charge-explode, E-hole climb):
  - A melee enemy with `splash` > 0 is a bomb. It explodes on reaching you, with no XP, or when killed, with XP.
  - The blast hurts the player (full when it detonates on you, half when killed) and other Locust (2.5x), so chain reactions happen.
  - `Kill` removes the enemy and zeroes its HP before the blast, so a blast can't loop back to it.
- **Reavers** (`Locust/Reaver/GP01`, 17 m long, LOD2 16k triangles; clips: flying loops, combo, death midflight):
  - `enemies.flyHeight` makes an enemy a flyer. Reavers fly at 9 m, circle at range spitting explosive bile, and swoop to the street for 4 s of every 14.
  - Melee and close skills only reach enemies about level with you (`InReach`).
  - A flyer falls when it dies. Reavers appear in waves 4-6.
- **Skill book** on the pause screen: all 8 skills with rank, MP, cooldown, cast time, crowd control, description and chain links.
- **Melty run 2** (test 1f654885): Melty launched and observed the run-2 game (launch-sessions.json `anchor` pid, `gameObserved` true), and the game wrote its run-2 checks, but the test runner stayed at "starting" until it failed. This is Melty-side.
- **The 500-item roadmap** (`ROADMAP500.md`, `python -I tools/changelog.py 0.8.0` for the full list). Engine facts learned on the way:
  - **GPU skinning:** each character is one bind-pose mesh. Every frame of every clip is a row of bone matrices in an RGBA32F texture, blended by the Lit shader (`skinA`/`skinB`), with clip cross-fades (`AnimBlend`). Process memory went from 4.6 GB to 1.9 GB.
  - **Winding:** UE's front faces wind clockwise and the (x, z, y) mirror makes them counter-clockwise, so the winding is NOT swapped. The old swap drew characters inside-out and gave the city flipped normals. The city swaps only for mirror-placed pieces; its cache is `daevas-city-2`.
  - **Mesh indices:** never free `Mesh.Indices` after upload, or raylib draws the mesh as a plain triangle list (shards).
  - **Textures:** DXT1/DXT5 encoded on the CPU with the mip chain (`Bc.cs`): 637 MB on the GPU became 93 MB. City textures start at 128 px and stream to full size, nearest first. A warm-up draws everything once at load, because the driver uploads on first draw and that caused 1.7 s hitches.
  - **City:** cells and meshes are read in parallel. The street surface comes from the Gears geometry (`GroundAt`, never below the drawn floor). Downtown adds the `_d34B7FC44` layer (building ground floors and storefronts, with real shop interiors behind open sills). A distant ring of cells streams in after the start.
  - **Gears patch 2026-10-15:** it renamed folders' case (`Locust/Wretch` became `Locust/wretch`). The provider is now case-insensitive.
  - **Replays:** all input goes through `Inp` (sampled once a frame). A replay is the seed, settings, profile and every frame's input and dt. Two recorded runs replayed to the identical end state.
  - **Sheet mods:** `%LOCALAPPDATA%\DaevasOfEDay\mods\*.json`, reloaded live. Melty test runs ignore them.
  - **Package:** partial trimming plus dropping the debugger DLLs took it from 113 MB to 52 MB. A cold city build with the trimmed exe was checked.
  - **Balance:** the Kryll have a 3 s grace, then bite 1-1.5% of max HP a second. The level cap is 30, so late waves still grow the hero. Waves 6-8 were eased. Autoplay paths round buildings, seeks light at night and drinks potions. The last Locust of a long wave get on-screen markers.

## Next
The sheets, then the game window (Raylib-cs), then the hotbar and XP bar, then more skills.
