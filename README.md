# RA Engine

A Minecraft‑style block‑world engine built in **Godot 4.6 (C#)** for teaching.
Build and explore voxel worlds, talk to characters, follow narrated story beats,
and play through scripted lessons — designed for retelling **historical and
Biblical scenes** in a way that captures a class's interest.

Two lessons are included: **David and Goliath** (the Valley of Elah — an action
lesson with the sling) and **The Garden of Eden** (Creation — a peaceful
exploration lesson, naming the animals). Both are picked from the main menu.

---

## Requirements

- **Godot 4.6.2 – .NET / Mono build** (the editor at `C:\Godot\Godot_v4.6.2-stable_mono_win64.exe`)
- **.NET 8 runtime** (already installed; the project targets `net8.0`)

## Running

**From the editor (easiest):** open this folder as a project in the Godot Mono
editor and press **Play** (F5). Godot compiles the C# the first time, then the
game boots to the **main menu** — pick a lesson or the build sandbox.

**From the command line:**

```sh
# build once
dotnet build "RA Engine.sln" -c Debug

# run the game (boots to the main menu)
"C:\Godot\Godot_v4.6.2-stable_mono_win64.exe" --path .

# jump straight into things
"...Godot...exe" --path . -- --lesson-david      # play David & Goliath
"...Godot...exe" --path . -- --lesson-creation   # play the Garden of Eden
"...Godot...exe" --path . -- --menu              # main menu (default)

# regenerate the texture library as fresh PNGs
"...Godot...exe" --headless --path . -- --gen-textures
```

## Controls

| Action | Key |
|---|---|
| Move | **W A S D** |
| Look | **Mouse** |
| Jump / swim up | **Space** |
| Sprint | **Shift** |
| Crouch / swim down | **Ctrl** |
| Talk to a nearby character | **E** |
| Pause menu | **Esc** |
| Toggle fly (creative) | **G** |
| Toggle Build ↔ Adventure | **B** |

**Adventure mode** (lessons): **Left‑click** attacks with the equipped weapon
(e.g. the sling). **Build mode** (sandbox): **Left‑click** breaks a block,
**Right‑click** places the block selected on the hotbar (**1–9** / mouse wheel).

**Level‑editor keys** (Build mode): **Z**/**X** mark region corners · **F** fill
with the selected block · **R** clear · **C** copy prefab · **V** paste prefab ·
**F5** save world · **F9** load world.

## Features

- Chunked voxel world with neighbour‑aware meshing, ambient occlusion and a
  custom **PBR shader** (albedo + normal + roughness/metallic/AO + emission via
  `Texture2DArray`s).
- Procedurally generated, **fully replaceable** stylized texture library.
- First‑person controller: walking, sprint, crouch, jumping, **fall damage**,
  **swimming** with buoyancy and a drowning air meter.
- **Build / break** blocks, hotbar, selection outline; a creative sandbox that
  doubles as a **level editor** with save/load and copy/paste prefabs.
- **Combat**: melee + thrown weapons, projectiles, several enemy types with
  chasing AI, health bars, and a non‑graphic "defeat" effect (no gore).
- **NPCs**, branching **dialogue** (JSON, teacher‑editable), and queued
  **narration** triggered as the player explores.
- **Lesson framework** with objectives and a victory sequence.
- Main menu, pause menu, and settings (mouse sensitivity, volume).

## Project layout

```
assets/
  textures/blocks/<name>/   replaceable PBR PNGs per block
  dialogue/<id>.json        teacher-editable conversations
  shaders/voxel.gdshader    the PBR voxel surface shader
scripts/
  core/     blocks, world, chunks, meshing, textures, save/load, input, settings
  player/   first-person controller, block interaction, level editor
  combat/   weapons, projectiles, enemies, health bars
  npc/      talkable characters
  dialogue/ dialogue data + JSON loader
  ui/       HUD, hotbar, dialogue box, narrator, menus
  lessons/  the lesson framework + David & Goliath
  tools/    the procedural texture generator
worlds/                     (saved worlds live in user://worlds/)
```

**See [`docs/AUTHORING.md`](docs/AUTHORING.md)** for how to replace textures,
write dialogue, build worlds, and author new lessons.
