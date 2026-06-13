# Authoring guide

How to make RA Engine your own: swap textures, write dialogue, build worlds, add
blocks, and create new lessons. None of this requires touching the engine core.

---

## 1. Replacing textures

Every block's look lives in its own folder of plain PNGs that you can open in any
image editor and overwrite at any time:

```
assets/textures/blocks/<block_name>/
    albedo.png      base colour (sRGB)            — required
    normal.png      tangent-space normal map      — required
    roughness.png   grayscale, white = rough      — required
    metallic.png    grayscale, white = metal      — optional (metals only)
    emission.png    glow colour (sRGB)            — optional (lamps, fire)
```

- Any square size works (64×64 is the default; others are resized on load).
- Keep the **file names** the same — that's how the engine finds each map.
- Missing optional maps fall back to sensible defaults (no metal, no glow,
  full ambient light).
- To regenerate the whole default set from scratch (e.g. after deleting one):

  ```sh
  "C:\Godot\...mono...exe" --headless --path . -- --gen-textures
  ```

  Texture recipes live in `scripts/tools/TextureForge.cs` if you want to tweak
  how the defaults are generated.

## 2. Adding a new block

Edit `Define()` in `scripts/core/BlockRegistry.cs`:

```csharp
Add("marble", "Marble").SetFaces("marble");                       // same on all faces
Add("crate", "Crate").SetFaces(top: "crate_top", bottom: "crate_top", side: "crate_side");
Add("lava", "Lava").SetFaces("lava").Emissive = true;             // glows
```

Then create the matching texture folder (`assets/textures/blocks/marble/…`).
Useful flags on a `BlockType`: `Solid`, `Opaque`, `Emissive`, `Hazard` +
`HazardDamage`, and `Render = RenderType.Water` for liquids.

## 3. Writing dialogue (JSON)

Conversations are JSON files in `assets/dialogue/<id>.json`:

```json
{
  "start": "0",
  "nodes": {
    "0": { "speaker": "Samuel", "text": "The Lord looks at the heart.", "next": "1" },
    "1": { "speaker": "Samuel", "text": "Will you trust Him?",
      "choices": [
        { "text": "Yes.",       "next": "yes" },
        { "text": "I'm afraid.", "next": "afraid" }
      ] },
    "yes":    { "speaker": "Samuel", "text": "Then go in peace.", "next": null },
    "afraid": { "speaker": "Samuel", "text": "Courage is fear that has prayed.", "next": "yes" }
  }
}
```

- `next: null` (or omitted) ends the conversation.
- Provide `choices` to branch; otherwise it advances on Space / click.
- Load it with `Dialogues.Load("samuel")` and hand it to an NPC.

## 4. Pre-building a world (the level editor)

Choose **Build Sandbox** from the menu (or any Build‑mode session):

1. **G** to fly, then place (right‑click) / break (left‑click) blocks; pick the
   block with **1–9** or the mouse wheel.
2. Mark a region with **Z** (corner A) and **X** (corner B). Then **F** fills it
   with the selected block, **R** clears it.
3. **C** copies the marked region as a prefab; **V** pastes it where you're
   looking — great for repeating tents, walls, trees.
4. **F5** saves the world to `user://worlds/quicksave.rworld`; **F9** loads it.

To use a saved world inside a lesson, call
`WorldIO.LoadWorld(session.World, "user://worlds/yourworld.rworld")` in the
lesson's `Build` method instead of generating terrain.

## 5. Authoring a new lesson

Copy `scripts/lessons/DavidAndGoliath.cs` as a template and implement
`ILesson`. The engine hands you a fully wired `GameSession`:

```csharp
public sealed class CrossingTheJordan : ILesson
{
    public string Id => "jordan";
    public string Title => "Crossing the Jordan";
    public string Subtitle => "Joshua 3";
    public Vector3 Spawn => new(32, 3, 50);

    public void Build(GameSession s)
    {
        // 1. terrain — generate, or load a world you pre-built
        WorldGen.FlatGround(s.World, 0, 63, 0, 63, 0);
        s.World.MarkAllDirty();
        s.World.RebuildAllNow();

        // 2. objectives shown top-right
        s.Hud.SetObjectives(new[] { "Reach the river", "Cross on dry ground" });

        // 3. story + characters
        s.Narrator.Show("The people came to the banks of the Jordan at flood stage.");
        var joshua = new Npc { NpcName = "Joshua", Dialogue = Dialogues.Load("joshua") };
        s.World.AddChild(joshua);
        joshua.GlobalPosition = new Vector3(30, 1, 48);
        joshua.Talked += () => s.Hud.CompleteObjective(0);

        // 4. triggers, enemies, victory
        var line = NarrationTrigger.Create(new Vector3(32, 2, 30), new Vector3(64, 6, 2),
            s.Narrator, "As the priests' feet touched the water, the river stood up in a heap.");
        line.Entered += () => { s.Hud.CompleteObjective(1); s.Hud.ShowCenter("The people crossed over."); };
        s.World.AddChild(line);
    }
}
```

Then add it to the list in `scripts/lessons/ILesson.cs`:

```csharp
private static readonly List<ILesson> All = new()
{
    new DavidAndGoliath(),
    new CrossingTheJordan(),   // <- appears on the main menu automatically
};
```

**Building blocks you have:**

- `WorldGen.FlatGround / Valley / BuildHut` and helpers for terrain.
- `session.SpawnEnemy(EnemyType.Soldier()/.Wolf()/.Giant(), pos)` — set
  `enemy.Target = null` to keep it dormant; subscribe to `enemy.Defeated`.
- `EnemyType` fields (health, speed, damage, scale, colours) are editable.
- `Npc` (set `NpcName`, `Dialogue`, robe colours); subscribe to `npc.Talked`.
- `NarrationTrigger.Create(center, size, narrator, lines…)` + `.Entered`.
- `session.Hud.SetObjectives / CompleteObjective / ShowCenter / ShowBanner`.
- `session.Weapons.Equip(Weapon.Sling() / .Sword() / .Bow())`.
- `session.SetMode(GameSession.Mode.Build)` to let players build inside a lesson.

## 6. Tuning feel

- Movement, jump height, fall‑damage and swim values: top of
  `scripts/player/Player.cs`.
- Daylight, sky colours, fog, shadows: `scripts/core/Scenery.cs`.
- Lighting/PBR response: `assets/shaders/voxel.gdshader`.
- Mouse sensitivity and volume are in the in‑game **Settings** menu.
