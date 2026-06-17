# Base Models — humanoid, horse, weapons (rigged glTF, full PBR)

Three block/voxel-style base models were added under `res://assets/models/base/`. They are meant
as the shared bases for all humanoids, the first mount, and an equippable weapon pack. This doc is
the handoff for swapping the game off **procedural** models (`MobModel`/`CharacterModel`) onto these.

```
res://assets/models/base/humanoid_base.glb   ~2 m clothed humanoid, 47 animations
res://assets/models/base/horse.glb           rideable horse, 9 gaits
res://assets/models/base/weapons.glb         12 weapon/prop bases (one pack)
```

Authoring source lives in a separate repo (`ra-models`: Blockbench `.bbmodel` + `tools/` that bake the
PBR). You don't need it to use the models — but reskins/edits happen there, then re-export the `.glb`.

---

## 0. TL;DR integration (it's nearly drop-in)

These were built to fit the **existing** `RiggedModel`/`ICharacterModel` path. To use the humanoid on an
archetype, just set its model path — `CharacterModel.BuildHumanoid` already calls `RiggedModel.TryLoad`
and falls back to the procedural model if the path is empty/missing:

```csharp
// EnemyType / Npc archetype:
ModelScene  = "res://assets/models/base/humanoid_base.glb";
ModelYawDeg = 0f;     // IMPORTANT: 0, NOT 180 — these models already face -Z like the box model
Beast       = false;

// Horse (uses the Beast branch -> CharacterModel.BuildBeast -> RiggedModel.TryLoad):
ModelScene  = "res://assets/models/base/horse.glb";
ModelYawDeg = 0f;
Beast       = true;
```

`RiggedModel` auto-detects clips by name substring, so with these assets it resolves:
`idle → animation.idle`, `walk → animation.walk`, `attack → animation.swing_1h` (first match of
attack/melee/**swing**/punch/chop/slash). It force-loops idle/walk. Flash (`SetFlash`) and `Squash`
work because the glTF materials import as `StandardMaterial3D`.

**That gets you idle/walk/attack for free.** Everything below is for going further — the models carry a
*much* richer clip set (combat stances, blocks, ranged weapons, mounted gaits) and clean attach points,
which `RiggedModel` does not yet use.

---

## 1. Conventions (all three models)

- **Forward = −Z**, up = +Y. Matches the procedural box model's front, so `ModelYawDeg = 0`. (The
  `RiggedModel` comment warns glTF often faces backwards → 180; **not these** — they were authored −Z.)
- **Scale:** 16 units = 1 m. Humanoid ≈ 2.0 m tall, feet at local Y = 0 (origin at the feet).
- **Imported as a Node3D hierarchy, NOT a `Skeleton3D`.** Every bone is a plain `Node3D` — which means
  **every bone is an attach point**: parent an item node to it and zero the transform. There are no skin
  weights; limbs are separate cubes parented in a chain. (If you ever need a real `Skeleton3D` +
  `BoneAttachment3D`, the source `.bbmodel` can be re-exported with the glТF "armature" option on.)
- **Animations live on an `AnimationPlayer`** inside the imported scene. Clip names keep the Bedrock
  `animation.` prefix (e.g. `animation.walk`). `RiggedModel.FindAnim` already walks the tree to find it.
- **Full PBR baked in:** each `.glb` has base color + a metallic-roughness map + a normal map (weapons
  use flat per-material PBR factors instead). Textures use **nearest** filtering (crisp voxel look).
- ⚠️ **Metallic needs a reflection source.** The weapons' steel renders **black** in a scene with no
  `WorldEnvironment` (sky) or reflection probe. Add one (a `ProceduralSkyMaterial` sky is enough) or
  metals/anything metallic will look dead. The humanoid/horse are non-metal, so they're fine without it,
  but a sky improves everything.

---

## 2. Humanoid (`humanoid_base.glb`)

Block "Steve-proportioned" humanoid: split limbs (elbow/knee) so bends read. Painted default skin:
skin-tone head + forearms, tunic on torso/upper-arms, trousers, boots; shading + AO baked into the
texture. Intended to be **reskinned** per character (repaint in the source repo).

### Bone/node hierarchy
```
root                              whole-body reorient (used by swim)
└─ hips                           pelvis; owns lower-body / locomotion
   ├─ torso                       chest; owns upper-body pose
   │  ├─ head ─ attach_head            ← helmet / hat socket
   │  ├─ attach_back                   ← backpack / cloak / slung weapon
   │  ├─ attach_quiver                 ← arrows (upper back)
   │  ├─ upper_arm_l ─ lower_arm_l ─ hand_l ─ grip_l   ← OFF-hand item socket
   │  └─ upper_arm_r ─ lower_arm_r ─ hand_r ─ grip_r   ← MAIN-hand item socket
   ├─ attach_hip_l                     ← sheathed sword/dagger, pouch (left)
   ├─ attach_hip_r                     ← holster / sheath (right)
   ├─ thigh_l ─ shin_l
   └─ thigh_r ─ shin_r
```
`grip_r` / `grip_l` sit at the fist. Main hand = **right**. For a 2H weapon, parent it to `grip_r`; the
`hold_2h`/`swing_2h` clips already place the left hand on the haft line.

### Animations (47) — names are prefixed `animation.`
- **Locomotion:** `idle` (loop), `walk` (loop), `run` (loop)
- **Crouch:** `crouch` (hold), `crouch_walk` (loop)
- **Air/water:** `jump` (once), `fall` (loop), `swim` (loop — rotates `root` prone)
- **Interaction:** `interact` (once, main-hand reach)
- **Melee holds (stances):** `hold_item`, `carry_2h` (big item/box), `hold_1h`, `hold_1h_shield`, `hold_2h` (greataxe/scythe grip)
- **Melee actions:** `swing_1h` (once, overhead), `swing_2h` (once, diagonal top-left→bottom-right)
- **Blocks:** `block` (unarmed), `block_1h_shield`, `block_2h`
- **Bow:** `bow_hold`, `bow_draw` (once), `bow_aim` (loop), `bow_release` (once)
- **Thrown spear:** `spear_hold`, `spear_aim`, `spear_throw` (once)
- **Sling:** `sling_hold`, `sling_whirl` (loop), `sling_release` (once)
- **Slingshot:** `slingshot_hold`, `slingshot_draw` (once), `slingshot_aim` (loop), `slingshot_release` (once)
- **Gun:** `gun_hold`, `gun_aim` (loop), `gun_fire` (once, recoil), `gun_reload` (once)
- **Mounted (lower-body only):** `mount_idle`, `mount_walk`, `mount_trot`, `mount_canter`, `mount_gallop` (loops), `mount_turn_l`, `mount_turn_r`, `mount_jump` (once), `mount_on` (once), `mount_off` (once)

### Animation layering (the important part for replacing procedural coordination)
The rig splits cleanly at **hips → torso**, and the clips respect it:
- **Upper-body action clips** (all `hold_*`, `swing_*`, `block_*`, `bow_*`, `spear_*`, `sling_*`,
  `slingshot_*`, `gun_*`) animate **only** torso, head, arms + hands — no legs/root.
- **Mounted clips** (`mount_*`) animate **only** root, hips, legs — arms stay neutral (gallop lean is on
  `hips`, not `torso`).

So an `AnimationTree` can layer a weapon set over either ground locomotion **or** a mount:
```
AnimationTree (BlendTree)
 ├─ LowerBody : StateMachine  (ground idle/walk/run  OR  mount_* gaits)
 ├─ UpperBody : StateMachine  (hold_*/aim/fire/swing/draw/block …)
 └─ Combine   : Blend2 (blend=1, Filter Enabled) — filter enables on the UpperBody input:
                  torso, head, upper_arm_l/r, lower_arm_l/r, hand_l/r, grip_l/r
```
Use `Add2` (additive) instead for small overlays (recoil, aim-sway, breathing). Note: ground `walk`/`run`
*are* full-body (they swing the arms), so the bone filter is what lets a weapon stance override the arms.
Mounted is the cleanest case (its clips never touch the arms).

### Root motion
`walk`/`run`/`crouch_walk` and the `mount_*` gaits animate `root`/`hips` **position** (bob/bounce/lean).
If the game drives position in code (it does), either disable those position tracks on import or ignore
them — otherwise the model will double-translate. `swim` rotates `root` to lay the body prone.

---

## 3. Horse (`horse.glb`)

Block rideable horse: barrel, up-angled neck, head + muzzle + ears, tail, mane/forelock, four split legs.
Bay coloration (brown body, black mane/tail/lower-leg points, dark hooves). Faces −Z like the rider.

### Bone/node hierarchy
```
root
└─ body                       barrel
   ├─ neck (+ mane) ─ head    head has muzzle, 2 ears, forelock
   ├─ tail
   ├─ saddle                  ← EMPTY node at the barrel top — the rider mount point
   ├─ leg_fl_upper ─ leg_fl_lower ─ hoof_fl    (front-left)
   ├─ leg_fr_upper ─ leg_fr_lower ─ hoof_fr    (front-right)
   ├─ leg_bl_upper ─ leg_bl_lower ─ hoof_bl    (back-left)
   └─ leg_br_upper ─ leg_br_lower ─ hoof_br    (back-right)
```

### Animations (9) — prefixed `animation.`
`idle`, `walk` (4-beat lateral), `trot` (2-beat diagonal), `canter` (3-beat), `gallop` (4-beat) — all
loops; `turn_l`, `turn_r` (lean/bank holds, blend over a gait), `jump` (once), `graze` (loop, head down).
Each gait uses biomechanically-correct footfall (legs independently phase-offset, not synced pairs —
except trot, which genuinely is paired diagonals).

### Rider + horse pairing
Parent the rider's `root` to the horse's **`saddle`** node (or a `RemoteTransform3D`). The barrel is
~0.9 m wide and the rider's `mount_*` clips straddle ~28°, so legs sit either side. Play matching clips:

| Horse | Rider (humanoid) |
|-------|------------------|
| `idle`/`walk`/`trot` | `mount_idle` / `mount_walk` / `mount_trot` |
| `canter` | `mount_canter` |
| `gallop` | `mount_gallop` |
| `turn_l`/`turn_r` | `mount_turn_l` / `mount_turn_r` |
| `jump` | `mount_jump` |

Because the rider's `mount_*` clips are lower-body only, you can still layer an upper-body weapon set
(bow/gun/etc.) on a mounted rider via the AnimationTree filter above.

---

## 4. Weapons (`weapons.glb`)

12 props in one pack, each a separately-named `Node3D` (group) whose **origin is the grip point**:
`sword`, `dagger`, `greataxe`, `shield`, `bow`, `arrow`, `spear`, `sling`, `slingshot`, `gun`, `crate`, `torch`.

**To equip:** instance the weapon node (or split it into its own scene), parent under `grip_r` (main hand)
or `grip_l` (off hand), zero the local transform, fine-tune with a small offset per weapon.
- 2H (greataxe/spear/gun): parent to `grip_r`; the humanoid's `hold_2h`/`gun_*` poses place the off hand.
- **bow**: parent to **`grip_l`** (left holds the bow, right draws).

### Canonical orientation (local to each node)
| Weapon | Grip at origin, extends… |
|--------|--------------------------|
| sword / dagger / spear | blade/point up **+Y**, handle below |
| greataxe | haft along **+Y**, head near top |
| shield | face toward **−Z**, grip bar at origin |
| bow | riser at origin, limbs **±Y**, string toward **+Z** |
| arrow | shaft **+Y**, head at top |
| sling / slingshot | held end at origin, pouch/fork **+Y** |
| gun | barrel **−Z**, stock **+Z**, trigger grip at origin |
| crate | centered on origin |
| torch | handle at origin, flame **+Y** (emissive) |

### Weapon materials (4 PBR materials, by part-name suffix)
`pbr_metal` (steel, metallic 1.0, rough 0.34) · `pbr_wood` (brown, rough 0.82) · `pbr_dark` (near-black
leather/cord, rough 0.88) · `pbr_flame` (orange, **emissive** ×3 — the torch flame glows in the dark).
Remember the metallic note: steel needs a sky/reflection probe to not look black.

---

## 5. Textures / PBR details

Each humanoid/horse `.glb` embeds 3 images (base color, metallic-roughness, normal); weapons use flat
PBR factors per material. Specifics, in case you script materials or reskin:
- **Base color** = sRGB (shading/AO baked in, voxel-skin style). **MR** = linear, packed glTF-order
  R:occlusion / G:roughness / B:metallic (Godot reads metallic←Blue, roughness←Green automatically on
  import). **Normal** = linear, tangent-space, OpenGL **+Y**. Humanoid/horse are non-metal (metallic 0).
- Texture filter = **Nearest** (set in the glTF sampler) — keep it for the pixel look.
- **Reskin** happens in the `ra-models` repo: repaint `base/<model>/*.png`, regenerate with
  `tools/gen_textures.py`, re-export the `.glb`, re-bake PBR with `tools/inject_pbr_glb.py`, then copy
  the `.glb` back here. (Blockbench's own glTF export only carries base color — that's why PBR is baked
  by a post-export script; don't expect normal/MR to survive a plain Blockbench export.)

---

## 6. Swapping procedural → these clips: suggested path

1. **Phase 1 (free):** point one humanoid archetype's `ModelScene` at `humanoid_base.glb`, `ModelYawDeg=0`.
   `RiggedModel` gives idle/walk/attack immediately. Do the same `Beast=true` for a horse mob if wanted.
   Verify it renders (add a `WorldEnvironment` sky to the scene if metals look black).
2. **Phase 2 (richer combat):** `RiggedModel` currently only knows idle/walk/attack. To use stances,
   blocks, and the 1h/2h/bow/etc. action clips, either extend `RiggedModel` (add a state→clip map and
   call `_anim.Play(...)` from gameplay) or replace its `AnimationPlayer` driving with an `AnimationTree`
   state machine. Map your existing procedural states (attacking, blocking, drawing, aiming) to the clip
   names in §2.
3. **Phase 3 (layering + mounts):** build the `AnimationTree` Blend2-with-bone-filter from §2 so a weapon
   stance plays on the upper body while locomotion **or** a `mount_*` gait plays on the lower body. Parent
   the rider to the horse `saddle` and play paired clips (§3).
4. **Equipment:** attach `weapons.glb` props to `grip_r`/`grip_l` and gear to the `attach_*` sockets (§2,§4).
5. **Root motion:** disable position tracks on locomotion/mount clips (or ignore them) since movement is
   code-driven — see §2 "Root motion".

The procedural `MobModel` stays as the automatic fallback (any archetype with `ModelScene = null` keeps
using it), so you can migrate archetype-by-archetype.
