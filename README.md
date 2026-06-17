# RA Engine

**A Minecraft-style voxel game engine, built in Godot 4.6 (C#), for teaching historical and Biblical scenes to a class.**

RA Engine is a block-world sandbox *and* a data-driven lesson framework. Students
build and explore chunked voxel worlds, talk to characters, follow narrated story
beats, and play through scripted lessons — all designed to retell **historical and
Biblical accounts** in a way that holds a classroom's attention. Teachers author
new chapters in plain JSON; no coding required.

> Built with **Godot 4.6.3** · **C# / .NET 8** · Forward+ renderer · `RAEngine` namespace.

---

## Contents

- [Lessons & campaign](#lessons--campaign)
- [Feature tour](#feature-tour)
- [Requirements](#requirements)
- [Running the game](#running-the-game)
- [Controls](#controls)
- [Showcases & galleries](#showcases--galleries)
- [Authoring lessons](#authoring-lessons)
- [Project layout](#project-layout)
- [Architecture notes](#architecture-notes)
- [Headless test suite](#headless-test-suite)
- [Visual-effects roadmap](#visual-effects-roadmap)
- [Repository conventions](#repository-conventions)

---

## Lessons & campaign

Three lessons ship today, each chosen from the main menu (or launched directly with a CLI flag):

| Lesson | Scene | Style |
|---|---|---|
| **David & Goliath** | The Valley of Elah | Action lesson — defeat the giant with the sling |
| **The Garden of Eden** | Creation | Peaceful exploration — name the animals, no combat |
| **Jericho** | The walls of Jericho | Data-driven lesson authored entirely in `assets/lessons/jericho.json` |

Lessons are stitched together by a **campaign**: an ordered chapter list with
unlock requirements. A chapter opens once its prerequisite lessons are complete,
and progress is saved crash-safely to `user://campaign.rprog` (atomic
temp-swap-with-backup write). Any JSON lesson that declares a `"chapter"` block
is appended to the campaign automatically — so adding a chapter is just adding a file.

---

## Feature tour

### Voxel world
- **Chunked streaming world** — 16×16×16 chunks, generated on worker threads in two
  phases (block fill, then meshing once all six face-neighbours exist) so chunk
  borders never seam. Loads to a configurable render distance and unloads behind you.
- **Greedy meshing** with per-vertex **ambient occlusion**: coplanar faces merge into
  larger quads only when their texture layer *and* AO match, keeping the mesh light.
- **Registry-driven blocks** — each block declares its render type, solidity, opacity,
  hardness (break time), material (drives sound), emissiveness, hazard damage, and
  per-face texture layers.
- **Procedural generation** — FastNoiseLite terrain, a valley height-field with a central
  stream channel, layered surface (grass → dirt → stone), scattered vegetation, and
  underground ore seams (coal, copper, iron, gold).
- **Living water** — deterministic, time-amortized flood-fill from edited cells that
  cascades into newly opened air below sea level and respects solid walls.

### First-person player
- Walk / **sprint** (with a subtle FOV widen for feedback) / **crouch** (shrinks the hitbox,
  blocked by low ceilings).
- **Swimming** with buoyancy, surface bobbing, a drowning air meter, and rising bubbles.
- **Gradual mining** — hold to break, with a repeating punch animation and material-based
  break sounds + dust poofs; place blocks from a 9-slot hotbar with a held-item viewmodel.
- **Fall damage**, landing dust, and camera shake scaled to the drop.
- **Creative (fly) mode** and a **Safe Mode** teacher toggle that ignores all damage.

### Combat & characters
- Enemy archetypes (**Soldier**, **Wolf**, **Giant**) with health, speed, damage, attack
  range/cooldown, and scale; line-of-sight pursuit that **auto-climbs ledges** to follow you.
- Melee + thrown weapons (the sling and its projectile), health bars, and a non-graphic
  "defeat" effect (red flash, squash-stretch pop, dust puff — no gore).
- **Dual-mode character models**: optional **rigged glTF** (auto-detecting idle/walk/attack
  clips by name) with a **procedural blocky fallback** that animates with sine-wave gaits —
  if a model is missing or fails to load, it degrades silently rather than crashing.
- **NPCs** that face the player, idle-breathe, carry a floating name label, and open
  **branching dialogue** (JSON, teacher-editable).

### Teaching framework
- **JSON-authored lessons** — spawn point, time of day, music mood, terrain build ops
  (flat/fill/line/mound/tree/hut/tent/altar…), NPCs, enemies, narration zones, intro text,
  and mode (Build vs Adventure). Every parse failure degrades to a safe default with a
  warning — a typo never crashes a lesson.
- **Data-driven quests** — ordered objectives (Talk, Defeat, Reach, Break, Place, Collect),
  optional objectives, per-objective and per-quest completion effects (narration, banners,
  sounds, waking enemies, particles), de-duplicated multi-target goals, and a live HUD
  checklist with progress counters and a completion chime.
- **Interactive signposts** — placeable wooden signs that open a scrollable reading modal on
  **[E]**, with the title engraved on the board.
- A rich **HUD**: crosshair, hotbar, health/air bars, objective checklist, center messages and
  banners, clock + compass, interact prompts, underwater/damage/vignette post-process, and a
  scene-transition fade.

### Effects & audio
- **Phase-0 global sheen** (complete): weather smoothing, color grading, depth haze, vignette,
  film grain, wind-driven grass sway, and footstep/landing dust.
- **Phase-1 fire kit** (complete): a living-fire system — candles, torches, campfires, braziers,
  forges, and altar fires — each with a flickering breathing light, embers, soft smoke, and a
  ground-level coal-bed glow, all budgeted by distance/count for performance. Recolorable
  palettes (Normal / Holy / Forge).
- **Everything is synthesized at runtime** — there are **no binary audio assets**. SFX, music
  beds (Calm/Hope/Solemn), and looping ambience (day birdsong, night crickets/owls, rain) are
  generated as deterministic 16-bit PCM and **crossfaded by time of day and weather**.
  The **texture library is procedurally generated** too, and fully replaceable.

---

## Requirements

- **Godot 4.6.x — .NET / Mono build** (the project pins `Godot.NET.Sdk/4.6.3`).
  The reference editor is `C:\Godot\Godot_v4.6.3-stable_mono_win64.exe`.
- **.NET 8 SDK** (the project targets `net8.0`).

No other dependencies — textures and audio are generated by the game itself.

---

## Running the game

### From the editor (easiest)
Open this folder as a project in the **Godot Mono** editor and press **Play (F5)**. Godot
compiles the C# on first run, then boots to the **main menu** — pick a lesson, the build
sandbox, or a showcase.

### From the command line
```sh
# Build once
dotnet build "RA Engine.sln" -c Debug

# Boot to the main menu (default)
"C:\Godot\Godot_v4.6.3-stable_mono_win64.exe" --path .

# Jump straight into a mode (note the bare -- separating Godot args from game args)
"...Godot...exe" --path . -- --lesson-david       # David & Goliath
"...Godot...exe" --path . -- --lesson-creation    # The Garden of Eden
"...Godot...exe" --path . -- --lesson-jericho      # Jericho (JSON-authored)
"...Godot...exe" --path . -- --sandbox            # endless creative sandbox
"...Godot...exe" --path . -- --showcase           # interactive FX gallery
"...Godot...exe" --path . -- --menu               # main menu (explicit)

# Regenerate assets headlessly
"...Godot...exe" --headless --path . -- --gen-textures   # rebuild the texture library as PNGs
"...Godot...exe" --headless --path . -- --gen-audio      # dump synthesized audio to assets/audio/
```

The **first flag wins**, deterministically. Any `--test-*` flag runs a specific headless
self-test (see [below](#headless-test-suite)).

---

## Controls

| Action | Input |
|---|---|
| Move | **W A S D** |
| Look | **Mouse** |
| Jump / swim up | **Space** |
| Sprint | **Shift** |
| Crouch / swim down | **Ctrl** |
| Interact — talk, read a sign | **E** |
| Break / mine a block | **Left-click** (hold to mine) |
| Place a block | **Right-click** |
| Select hotbar slot | **1–9** / **mouse wheel** |
| Inventory | **Tab** |
| Toggle creative (fly) | **G** |
| Toggle Build ↔ Adventure | **B** |
| Pause / menu | **Esc** |

**Adventure mode** (lessons): left-click **attacks** with the equipped weapon (e.g. the sling).
**Build mode** (sandbox): left-click **breaks**, right-click **places** the selected hotbar block.

**Sandbox level-editor keys** (Build mode): **Z**/**X** mark region corners · **F** fill with the
selected block · **R** clear · **C** copy a prefab · **V** paste it · **F5** save world · **F9** load world.

---

## Showcases & galleries

Reachable from **Main Menu → Showcases**, or with `--showcase`. These are hand-built static
stages (no streaming) for demonstrating effects as each FX phase lands — a reusable convention
across development.

- **Effects Showcase** — walk through labeled stations: grass-wind plain, footstep-material
  bands, a jump tower (landing dust), a water pool (splash), a lamp cluster (bloom), a far ridge
  (depth haze), and the **Fire & Light** station (candle, torch, campfire, forge, brazier, altar).
- **Block Gallery** — a pillar of every block type.

**Showcase hotkeys:**

| Key | Cycles |
|---|---|
| **F5** | Weather — Clear → Rain → Snow |
| **F6** | Time of day — Morning → Noon → Dusk → Night → auto |
| **F7** | Glow preset — Normal → Divine → Plague → Cave |
| **H** | Fire palette — Normal → Holy → Forge (reads best at dusk/night) |
| **V** | Toggle fly |

Showcases run with Safe Mode on (no fall or hazard damage).

---

## Authoring lessons

Teachers author content as data, not code:

- **Lessons** — JSON files in `assets/lessons/<id>.json` (see `jericho.json` for a worked
  example). The schema is flat and forgiving: single values or arrays are both accepted, and
  comments are allowed.
- **Dialogue** — JSON conversation trees in `assets/dialogue/` (linear or branching).
- **Textures** — replaceable PNGs in `assets/textures/`, or regenerated procedurally with
  `--gen-textures`.

See **[`docs/AUTHORING.md`](docs/AUTHORING.md)** for the full guide to replacing textures,
writing dialogue, building worlds, and authoring new lessons and chapters.

---

## Project layout

```
Main.tscn                 The single scene; its root is Game.cs
project.godot             Godot project + input map
RA Engine.csproj / .sln   .NET 8 / Mono build

assets/
  textures/   procedurally-generated, replaceable PBR block textures
  shaders/    voxel, vegetation, water, underwater, sky, damage, post,
              flame / firebase / smoke (+ fire_common.gdshaderinc)
  dialogue/   teacher-editable conversation JSON
  lessons/    JSON-authored lessons (e.g. jericho.json)

scripts/
  Game.cs / GameSession.cs   app entry + the per-session subsystem aggregate
  core/        world, chunks, meshing, streaming, blocks, textures, audio,
               environment/weather, fire kit, settings, save/load
  player/      first-person controller, block interaction, build editor
  combat/      enemies, weapons/projectiles, character models (rigged + procedural)
  npc/         talkable characters
  dialogue/    dialogue data + JSON loader
  lessons/     lesson framework, catalog, and the JSON lesson loader
  quests/      data-driven quests + objective tracker
  world/       signposts, the FX showcase controller
  ui/          HUD, menus, hotbar, dialogue box, narrator, pause menu
  tools/       the procedural texture forge

docs/
  AUTHORING.md     how to author textures, dialogue, worlds, and lessons
  FX-ROADMAP.md    the visual-effects master plan
```

Saved sandbox worlds live under `user://worlds/`; campaign progress under
`user://campaign.rprog`.

---

## Architecture notes

- **`Game.cs`** is the scene root: it sets up app-wide singletons (settings, audio, FX),
  parses the command line, and routes to a mode (menu, lesson, sandbox, showcase, or a test).
- **`GameSession`** aggregates everything a running game needs — `VoxelWorld`,
  `EnvironmentController`, `Player`, `GameHud`, block interaction, the build editor, weapons,
  the fire conductor, dialogue + narrator, the inventory, and the quest tracker — so a lesson
  or sandbox is one object to build and tear down.
- **Determinism by design** — terrain, textures, and audio are all generated from seeds/noise,
  so a world (and its assets) is byte-for-byte reproducible.
- **Physics matched to refresh rate** — the physics tick is synced to the monitor's refresh so
  look (physics-ticked) and movement update together with no translate-vs-rotate jitter; a
  `project.godot` fallback covers the headless test runner.

---

## Headless test suite

Beyond gameplay, `Game.cs` exposes ~30 self-contained `--test-*` modes that boot a scene,
assert, print a result, and quit — handy for CI and quick regression checks. Examples:

```sh
"...Godot...exe" --headless --path . -- --test-world      # world generation
"...Godot...exe" --headless --path . -- --test-greedy     # greedy meshing
"...Godot...exe" --headless --path . -- --test-water      # water flood-fill
"...Godot...exe" --headless --path . -- --test-combat     # combat
"...Godot...exe" --headless --path . -- --test-quest      # quest tracking
"...Godot...exe" --headless --path . -- --smoke           # C# assembly loads OK
```

---

## Visual-effects roadmap

The FX plan lives in **[`docs/FX-ROADMAP.md`](docs/FX-ROADMAP.md)**, governed by a
**stylized-over-realistic** aesthetic rule:

- **Phase 0 — global sheen** ✅ — grading, depth haze, vignette, film grain, grass sway, footstep dust.
- **Phase 1 — living world** 🔥 *(fire kit done)* — fire, then flowing water, ambient life, sky tie-ins.
- **Phase 2 — weather & combat drama** — rain/lightning/rainbow/sandstorm, volumetric fog, swing trails.
- **Phase 3 — Biblical miracles** — Red Sea parting, pillar of fire, burning bush, manna, Jericho collapse.
- **Phase 4 — sandbox stretch** — fire spread, large stylized water, boat wake, aurora, footprints.

---

## Repository conventions

- Godot `*.uid` files **are committed** (stable resource identifiers); the `.godot/`,
  `bin/`, and `obj/` build outputs are not.
- **No binary audio in the repo** — `assets/audio/` is git-ignored; audio is synthesized at
  runtime (`--gen-audio` only dumps it locally for inspection).
- The `godot-ai` editor addon (`addons/godot_ai/`) is a local-only dev tool and is git-ignored.
</content>
</invoke>
