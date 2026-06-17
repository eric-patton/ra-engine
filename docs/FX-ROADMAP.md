# FX & Shader Master Plan + Phased Roadmap

The single source of truth for RA Engine's visual-effects program: every effect we
want, how to build it, what to reuse, and the order we'll build it in. Captured so it
survives new sessions — if you're picking this up cold, **start here**.

Status: **planning** (2026-06-16). Nothing below is implemented yet except where the
"How" line says "already exists." Provenance and open follow-ups are at the bottom.

---

## 0. Guiding principles

1. **Stylized, not realistic — cohesion over fidelity.** This game is blocky/voxel and
   *marketed as such*. The reference repos (`moses-red-sea`, `godot-effects`) are
   **technique sources to re-skin**, not assets to drop in. The realistic FFT ocean /
   `red_sea_water` shader is gorgeous but would *clash* with our look. So: **borrow the
   idea (parting cross-section, contact foam, blackbody flame ramp, caustic filaments),
   re-implement it flat-shaded / gradient / chunky to match the world.** Every item with
   a realistic reference carries a **Stylize:** note.
2. **Reuse-first.** Most of this list is *wiring + parameter tweens*, not new shaders.
   The plumbing already exists (see §2). Prefer extending `EnvironmentController`,
   `Fx.cs`, and the existing shaders over new nodes.
3. **`EnvironmentController` is the conductor.** Ambient/weather/grade effects are param
   tweens it can host. Recurring theme: **stop *snapping* between states, start
   *tweening*.**
4. **Budget / LOD everything that scales.** Torch lights and per-source particles are the
   perf risk. A central budget manager caps live emitters/lights by distance + count.
5. **Kid-appropriate.** Teaching tool for a class. Impact/feedback yes; gore no. "Blood"
   plague is a color shift; combat hits are sparks/puffs, not viscera.
6. **Lessons trigger set-pieces in one line.** Miracles are scripted beats; a small
   `MiracleFx` director exposes flash/shake/beam/grade-preset/time-pin so a lesson script
   stays readable.

---

## 1. Reference inventory (don't lose this research)

### `C:/repos/moses-red-sea` — realistic, reference-only
- `shaders/red_sea_water.gdshader` — **the** parting effect: ONE subdivided plane bent in
  the vertex stage into a continuous cross-section (flat sea → dry trough corridor →
  towering walls → back to sea), driven by a `part` 0→1 uniform. FFT waves, screen-space
  refraction (`hint_screen_texture`), depth absorption (`hint_depth_texture`,
  `exp(-thickness*density)`), triplanar wall detail, contact/whitecap foam. *Re-skin
  stylized for us — keep the cross-section idea + contact-foam + depth-absorption math.*
- `shaders/seabed.gdshader` — **caustics**: two scrolling UV layers, `pow(min(ca,cb),2)`
  filament pattern added to EMISSION; roughness-crush on submerged faces (wet sheen).
  *Ports cleanly + cheaply; this is our caustics source.*
- `addons/ocean_waves/` — GodotOceanWaves FFT (MIT): compute cascades publish global
  `displacements`/`normals` textures. *Heavy (compute). Reserve for set-piece maps only,
  and even then prefer a stylized big-water — see B10.*
- `shaders/night_sky.gdshader`, `shaders/pillar_fire*.gdshader` — pillar variants + an
  `AT_CUBEMAP_PASS` guard pattern.

### `C:/repos/godot-effects` — clean, portable fire kit (good stylistic fit)
- `fire/shaders/fire_common.gdshaderinc` — shared `hash13` / `vnoise3` / `fbm3` (+ cheap
  3-octave `fbm3_3` for vertex) and a blackbody `fire_ramp(t, dark,red,orange,gold,white)`.
  *This is our shared flame-noise/color include.*
- `fire/shaders/` — `base_glow`, `flame_column`, `flame_tongue`, `ember`, `smoke`,
  `heat_distortion`, `glory_beam`, `glory_cloud`. Layered billboards; each does one job.
- `fire/pillar_of_fire.gd` — **the flicker driver**: `FastNoiseLite` fast (9 Hz twitch) +
  slow (1.7 Hz swell) + rare surge (`smoothstep(0.55,0.95,slow)`); drives an `OmniLight3D`
  energy/color and jitters its position, and pushes a single `flicker` *instance uniform*
  into every flame node so the whole effect breathes in sync. *Port to a C# `FireLightDriver`.*
- `demo/shaders/night_sky.gdshader`, `demo/main.gd`, `demo/orbit_camera.gd` — demo harness.

### Current ra-engine FX state (what we build on)
- **`scripts/core/Fx.cs`** — pooled (16) one-shot `GpuParticles3D` facade.
  `Fx.Burst(pos, FxKind, tint, count)` with kinds `Poof/Debris/Splash/Sparkle/Dust`;
  `Fx.Shake(trauma)`, `Fx.Flash(color, amount)`, `Fx.HitStop(seconds)`. Handlers
  registered by the session (camera shaker + HUD overlay). Safe no-ops when headless.
- **`scripts/core/EnvironmentController.cs`** — time-of-day (`Dawn .27 / Noon .5 / Dusk .73
  / Night 0 / Morning .36`), `SkyMaterial` ShaderMaterial accessor, sun/moon lights,
  WorldEnvironment glow (`SetGlowLevel`), fog, crossfading ambient particles (motes by day
  / fireflies by night via `AmountRatio`), `Wind` (public, per-frame), `SetWeather`,
  `SetFixedTime`. **The conductor.**
- **`scripts/core/WeatherDirector.cs`** — biome→weather (`Snow`/`Rain`/`Clear`) on a slow
  deterministic schedule; lessons can override.
- **Shaders** `assets/shaders/`: `voxel` (terrain, per-face emissive layer for
  torch/glowstone), `vegetation` (cross-mesh, `discard` alpha, **constant** `sway_amount`),
  `water` (stylized — exposes `shallow_color`/`deep_color`/`wave_height`), `underwater`
  (screen-read tint+wobble+vignette), `damage` (flash + edge vignette, screen-read? no —
  composites tinted alpha only), `sky` (fbm clouds, blocky sun/moon discs, stars; uniforms
  `cloud_coverage`, `cloud_speed`, `day`, `sun_dir`, star params).
- **Water fill** `scripts/core/VoxelWorld.Streaming.cs` — deterministic depth-first
  flood-fill (`PriorityQueue` by depth), amortized on a pump tick, bounded by sea level,
  `BlockChangeCause.Script`. The pattern to mirror for fire-spread (A9) and to extend for
  river flow (B1).
- **`scripts/player/ScreenFx.cs`** — receives `Fx.OnFlash`; home for new full-screen
  passes (chroma, vignette, grain, godrays).
- **`scripts/combat/`** — `RiggedModel` (per-instance flash + squash feedback),
  `Projectile`, `WeaponController`; `HeldItem` already swings.

---

## 2. Full catalog

Format per item: **ID Name** `[effort S/M/L · impact low/med/high]` then
*Looks / How / Reuse / Theme / Stylize (if needed)*.

### A. Fire, Light & Smoke

**A1 Torch flame + breathing light** `[M · high]`
- *Looks:* small layered flame on wall/standing torches; warm light that flickers and
  wanders so torch-lit rooms feel alive. Single biggest cave/night upgrade.
- *How:* 2–3 stacked billboards (`base_glow` plane + small `flame_column` + `flame_tongue`
  tips) + a small warm `OmniLight3D` (range ~6, energy ~2) driven by a trimmed
  `TorchFlicker` (fast+swell, no surge). Spawn when a torch block is placed (already an
  emissive block).
- *Reuse:* `fire_common.gdshaderinc`, `base_glow`/`flame_column`/`flame_tongue` ported to
  spatial; flicker logic from `pillar_of_fire.gd`.

**A2 Campfire / brazier / altar fire** `[M · high]`
- *Looks:* bigger than a torch — flame + rising embers + thin wind-bent smoke column +
  ground glow + crackle audio + stronger flicker light. One scene, three sizes (campfire,
  brazier, altar).
- *How/Reuse:* A1 stack scaled up + `ember` + `smoke` + `base_glow` plane.

**A3 Rising embers & sparks** `[S · med]` — additive `ember.gdshader` GPUParticles off any
fire; blackbody color-by-life, upward drift + turbulence.

**A4 Localized heat shimmer** `[S · med]` — `heat_distortion.gdshader` quad billboard above
flames (screen-read refraction). Sibling of G4.

**A5 Smoke column** `[S · med]` — `smoke.gdshader` billboards expand+erode+wind-bend, dark→
grey→clear; campfires, chimneys, sacrifices, burning Jericho.

**A6 Candle / oil lamp** `[S · med]` — single tiny quad flame + faint point light. Menorah,
tabernacle lamps.

**A7 Holy / blue flame variant** `[S · high]` — recolor `fire_ramp` to white-gold or
blue-white for supernatural fire (burning bush, pillar). Expose a color-set param so A1/A2
can swap.

**A8 Glory motes / floating light specks** `[S · med]` — slow luminous floating specks
(`glory_cloud`/`glory_beam` motes) for sacred spaces (Ark, Holy of Holies). Bridges to F.

**A9 Fire spread (sandbox)** `[L · med]` — flammable neighbors (wood/leaves/wheat) ignite on
the same amortized tick as the water fill; burn → ash/charcoal. Visual = flame nodes on
burning blocks + smoke + drifting embers. **Gate behind a setting/lesson flag** (can grief
builds). Theme: Sodom, burnt offerings.

**A10 Forge / kiln glow** `[S · low]` — pulsing emissive + ember spit for blacksmith /
Egyptian brickmaking.

### B. Water & Liquids

**B1 Flowing rivers by height gradient** `[M · high]` *(explicit ask)*
- *Looks:* one-block lanes and slopes visibly *stream* downhill — rivers/brooks, not ponds.
- *How:* the flood-fill already knows fill order; store a per-cell **flow vector** (toward
  the lowest open neighbor) and scroll `water.gdshader` normal/foam UVs along it, faster on
  steeper drops; foam at the leading edge. Extend `VoxelWorld.Streaming`.
- *Reuse:* the existing flow/fill BFS; `water.gdshader`.

**B2 Waterfalls + spray** `[M · high]`
- *Looks:* water pouring over a vertical drop with a misty spray + foam pool + height-scaled
  audio (bonus: a tiny rainbow in the spray on sunny days).
- *How:* detect water cell with air below in the streaming pump; vertical fast-scroll water
  face + a `Splash`-style mist burst at the base + foam pool.
- *Reuse:* `Fx` splash pattern; B3 spray.

**B3 Shoreline / waterfall-base foam & spray** `[M · med]` — bobbing foam band where water
meets land (port `red_sea_water` depth-threshold contact foam) + fine white droplet/mist
particles. *Stylize:* flat white foam, stepped not feathered.

**B4 Premium water surface** `[M · high]` — add subtle fresnel sky reflection + light
screen-space refraction to the stylized water. *Stylize:* keep it gradient/flat — a hint of
reflection, not a mirror.

**B5 Caustics on submerged blocks** `[M · high]` — port `seabed` `pow(min(ca,cb),2)` two-
layer scroll into `voxel.gdshader` EMISSION for blocks under water; depth-fade. (Cross-cuts
C-domain & G12.)

**B6 Wet-darkening waterline** `[S · med]` — submerged/recently-wet faces darker + glossier
(reverse of `seabed` roughness-crush). Sells beaches/riverbanks.

**B7 Ledge / cave drips** `[S · med]` — slow drip particles off undersides → splash + ripple
+ "drip" audio. Caves come alive.

**B8 Lava** `[M · high]` — slow glowing flow (B1 mechanic, recolored + slower) + animated
crust-crack pattern (dark cracks over hot glow) + bubble pops (`ember` spit) + heat haze
(G4) + dim red cast; lava+water → steam burst + stone (gameplay).

**B9 Splash on entry** `[S · med]` — wire existing `Fx.Splash` + expanding ring to
entity/block/projectile water entry.

**B10 Large stylized water body** `[L · high]` — for Galilee/Mediterranean/pre-parting Red
Sea. *Stylize:* do **not** ship the realistic FFT here; build a stylized big-water (gradient
+ scrolling stylized wave normal + flat whitecaps). FFT cascades remain a *fallback option*
for a single hero map only if ever justified.

**B11 Boat wake** `[M · med]` — V-foam trail behind boats (Jonah's ship, Galilee fishing,
walking on water).

**B12 Underwater bubbles & god-shafts** `[S · med]` — rising bubble particles + B5 caustics +
existing `underwater.gdshader` tint/wobble + G12 projection.

**B13 Surface ripple rings** `[S · med]` — expanding-annulus rings where rain/entities/drips
hit water. Shared shader with C1. *Stylize:* clean concentric rings.

### C. Weather & Sky  *(fleet-generated, full specs)*

**C1 Rain splashes + ripple rings** `[M · high]` — second ground emitter (`RainSplash`,
upward droplet burst) keyed to rain × dayFactor + B13 ripple rings on water. Reuse `Fx`
Splash pattern.

**C2 Volumetric valley / dawn mist** `[S · high]` — low ground-fog emitter (slab y≈-0.5,
amount ~80, preprocess-filled, near-white low-alpha puffs), density tied to `dayFactor` so
it "burns off" by mid-morning. Reuse `smoke.gdshader` billboard, whitened, no rise.

**C3 Lightning: flash + bolt + thunder** `[M · high]` — during Rain, every 15–45 s:
`Fx.Flash(white,.95)` + glow spike + a jagged additive bolt billboard (per-row hash jitter,
no texture) + thunder delayed by `distance/343`. New `LightningController`.

**C4 Rainbow after rain** `[S · high]` — pure `sky.gdshader` extension: anti-solar arc
(`acos(dot(dir,-sun_dir))` band at ~0.73 rad), ROYGBIV ramp, horizon-faded, `rainbow_strength`
tweened on Rain→Clear. Doubles as F7.

**C5 Sandstorm / dust wall** `[L · high]` — new `Weather.Sandstorm` (desert): rolling ochre
dust-wall cylinder (recolor `glory_cloud` Gaussian profile, directional scroll) + dense dust
fill particles + fog spike + reddened/dimmed sun + sky `sand_tint`. New `SandstormController`.

**C6 Wind-driven grass sway** `[S · med]` — wire `EnvironmentController.Wind` →
`vegetation.gdshader` (`sway_amount` is currently constant) + a `wind_direction` lean. Cheapest
legibility win in the project.

**C7 Godrays / sun shafts** `[M · high]` — screen-space radial blur from the sun's projected
position; strongest at low sun, warm-tinted. Implemented as G3.

**C8 Animated cloud shadows** `[L · high]` — share `sky.gdshader` cloud fbm (via the X1 shared
include or a low-res RenderTexture) into `voxel.gdshader`; drifting soft dimming on terrain.
Ties sky to world.

**C9 Snow accumulation tint** `[M · med]` — whiten top-facing faces (`NORMAL.y>0.5`) over
time during Snow via a `snow_accumulation` voxel uniform; matte roughness; fades on clear.

**C10 Wind debris (leaves / grit)** `[S · med]` — horizontal-drifting tumbling leaf/grit
particles when `Wind.Length()>3`; biome-swapped (leaf vs sand). Same `AmountRatio` crossfade
as motes/fireflies.

**C11 Weather transition smoothing** `[S · med]` — replace instant `SetWeather` with tweened
cloud coverage / precip `AmountRatio` / fog density / sun color. **Affects every scene.**
(Same effort as G13 — do once.)

**C12 (= B5) Water caustics on submerged faces** — see B5.

**C13 Volumetric atmosphere (`FogVolume`)** `[M · high]`
- *Looks:* genuinely 3D fog you can walk *into* and see light scatter through — a campfire/altar's
  smoke as a real column you can pass a torch through, dawn mist pooling in valley hollows, a
  plague-of-darkness fog bank, damp hanging in a tomb/cave. Depth the billboard/particle versions
  can't fake.
- *How:* Godot 4 `FogVolume` nodes (Box / Sphere / local-shape) feeding the **same WorldEnvironment
  volumetric-fog froxel buffer the depth haze (G2) already enables**, each with a small fog
  `ShaderMaterial` for density/shape/scroll. Local thin volumes for smoke columns (wind-bent, density
  tied to the fire); slab volumes for mist (density tied to `dayFactor`, so it burns off — **upgrades
  C2 from particles to true volume**); a dark, dense volume for F5 darkness. Placed + driven like the
  fire nodes.
- *Reuse:* volumetric fog is already on (G2); the fire conductor's spawn + LOD pattern
  (`FireController` / X4) to place and cull per-source volumes; the shared fbm noise (X1) for animating
  density.
- *Theme:* campfire / altar smoke, Egypt's darkness (F5), valley dawn mist (C2), tomb/cave damp (D5),
  Sodom aftermath haze (D10).
- *Stylize:* **keep it subtle** — low density, optionally stepped/quantized, so it adds depth without
  going photoreal and clashing with the blocky world. Reserve it for *hero* atmosphere moments, not
  everywhere.
- *Budget:* the froxel buffer + per-volume cost is the perf risk → cap live `FogVolume`s by
  distance/count via the X4 manager, exactly like the fire lights.

### D. Ambient Life & Biome Particles

**D1 Falling leaves near trees** `[S · high]` — emitter on tree clusters; tumbling drift,
seasonal color, wind-tied density, settle-then-relift.

**D2 Fireflies (blink) + pollen-in-sunbeams** `[S · high]` — enhance existing motes/fireflies:
per-particle blink phase (emission pulse), light-catching pollen by day, biome/time gating.

**D3 Butterflies / bees** `[M · med]` — 2-frame wing-flap billboards near flowers/meadows;
bees hover at hives.

**D4 Birds / doves / ravens** `[M · med]` — distant flocks crossing the sky; scriptable dove
release (Noah, baptism) and ravens (Elijah fed).

**D5 Cave ambience** `[M · med]` — drifting dust motes + B7 drips + glow-spores/mushrooms +
dusk bats at cave mouths. Caves stop feeling dead.

**D6 Swamp / marsh spores & gas** `[S · low]` — floating glowing spores + ground fog +
bubbling marsh-gas pops.

**D7 Dandelion / seed fluff** `[S · med]` — drifting white seed tufts in meadows, wind-borne.

**D8 Blossom petals** `[S · med]` — fruit-tree / spring biomes shed pink-white petals. Eden,
Gethsemane, Song of Songs.

**D9 Fish + surface jumps** `[M · med]` — darting schools in clear water + occasional jumps
(the miraculous catch).

**D10 Floating ash** `[S · low]` — dark drifting flecks + ember glow near volcanoes / after
fire (Sodom aftermath).

**D11 Ground critters** `[S · low]` — beetles/ants/lizards skittering on desert ground; frogs
near water (frog plague). Subtle "living world."

**D12 Flies / scavengers** `[S · low]` — buzzing dark specks near refuse/carrion (fly plague;
Egypt grit).

**D13 Tumbleweed / wind-blown debris** `[S · low]` — desert; ties to Wind.

**(D-arch) AmbientLifeDirector** — one component reads biome+time+weather+wind and sets all
densities via `AmountRatio`. Craft = *restraint*; density tuning is the whole game.

### E. Combat & Interaction Juice

**E1 Mining crack-stages + chip sparks** `[M · high]` — progressive 0–9 crack overlay on the
targeted block (decal/overlay shader) + material-specific chips each hit.

**E2 Textured break shards** `[S · med]` — upgrade `Debris` cubes to carry the broken block's
actual face texture.

**E3 Footstep dust by material** `[S · med]` — sand/grass/snow/water variants on the existing
footstep cadence (footstep *sounds* are already material-keyed).

**E4 Landing impact** `[S · med]` — fall-distance-scaled dust ring + thud + camera dip; Shake
on hard landings.

**E5 Sprint speed-lines / FOV kick** `[S · low]` — subtle screen lines + small FOV punch +
G10 vignette tighten when sprinting.

**E6 Hit sparks / impact puffs** `[S · med]` — metal/stone/creature variants (no gore). Wire
existing `HitStop`+`Shake`+`Flash` per hit weight.

**E7 Weapon swing trail** `[M · med]` — ribbon arc following the blade/sling on swing
(`HeldItem`/`WeaponController` already swing).

**E8 Projectile trails — David's sling** `[M · high]` — spinning slingstone blur + whoosh +
impact dust/thunk. David & Goliath centerpiece. (`Projectile.cs`.)

**E9 Damage feedback** `[S · med]` — `damage.gdshader` flash/vignette + G5 chromatic aberration
+ directional red edge from hit source.

**E10 Low-health state** `[S · med]` — pulsing red vignette + heartbeat audio + slight
desaturation (via G1 grade).

**E11 Block-place poof** `[S · low]` — already have `Poof`; add a subtle placed-block snap
highlight.

**E12 Enemy death / Goliath's fall** `[S · high]` — squash (RiggedModel has it) + poof + loot
sparkle; **Goliath** = big Shake + dust quake + slow-mo `HitStop` beat. Trailer moment.

**E13 Critical / special hit** `[S · med]` — bigger Flash + Sparkle + HitStop; scripted
freeze-frame for the slingstone-to-forehead.

**E14 Heal / pickup** `[S · low]` — Sparkle burst + soft glow + chime (manna, water, healing).

**E15 Tool / UI juice** `[S · low]` — hotbar select pop, craft-success sparkle, quest-complete
burst.

### F. Biblical Miracle Set-Pieces  *(fleet-generated; the marketing differentiator)*

All are scripted lesson beats via the `MiracleFx` director (X3). **Stylize every one** to the
blocky look.

**F1 Red Sea parting** `[M(stylized)/L · high]`
- *Looks:* the sea draws back into towering walls, a dry corridor of seabed is revealed,
  fading caustic shimmer on the just-exposed floor.
- *How (stylized):* do **not** ship `red_sea_water`'s realistic FFT plane. Build stylized
  walls — tall translucent water slabs with a vertical gradient (deep→light), flat animated
  foam crowns, a downward sheet-scroll, and `part` 0→1 driving wall height. Reveal seabed
  blocks; tween B5 caustics → 0 as it "dries."
- *Reuse (ideas only):* the cross-section concept, contact-foam threshold, and depth-
  absorption math from `red_sea_water.gdshader`; B5 caustics from `seabed`.
- *Theme:* Exodus 14. `QuestTracker` `Reach` objective when the player walks the corridor.

**F2 Pillar of fire (night) / cloud (day)** `[L · high]` — dual-aspect single node; direct
port of `godot-effects` pillar (flame column + tongues + `glory_beam` shaft + `base_glow` +
ember ring + wandering `OmniLight`); day variant swaps to white-silver `glory_cloud`. **The**
hero screenshot. Theme: Exodus 13:21. *Stylize:* the billboard kit already reads stylized —
keep flames chunky/flat.

**F3 Burning bush** `[M · high]` — `flame_tongue` + `ember` + `glory_beam`, recolored white-
gold (A7), wrapping a bush that never burns up (foliage block never removed). Theme: Exodus 3.

**F4 Manna fall** `[M · med]` — slow drifting flakes (not snow; lazy turbulence) at dawn +
settling white ground coating (per-chunk emissive tint) + landing sparkle + "what is it?"
trigger. Theme: Exodus 16.

**F5 Plague of darkness** `[S · high]` — sky `day`→0 + full overcast at noon, **no stars** (it
must read *unnatural*, not night), fog black-green, sun/moon energy 0; torches become the only
light. Reverse with a white flash. Pure param manipulation. Theme: Exodus 10.

**F6 Fire from heaven (Elijah)** `[M · high]` — `glory_beam` white bolt + `Fx.Flash`+`Shake` +
debris/sparkle detonation + sustained altar brazier (A2) + `HitStop` on impact. Three water-
pour `Place` objectives precede. Theme: 1 Kings 18.

**F7 Rainbow covenant (Noah)** `[M · high]` — C4 rainbow scripted on rain→clear, bloomed via
G11 Divine glow. Theme: Genesis 9.

**F8 Shekinah glory cloud (day pillar)** `[M · high]` — see F2 day aspect (white-silver
luminous column, cool light). Theme: Exodus 13:21 / Numbers 9.

**F9 Plague of blood** `[S · high]` — tween `water.gdshader` `shallow/deep_color` blue→arterial
red over ~8 s + rust fog + dead-fish `Debris`. Localized (sky unchanged). Theme: Exodus 7.

**F10 Walls of Jericho** `[M · high]` — staggered section collapse: rhythmic `Shake` per
trumpet circuit, then per-section debris + dust poof + `SetBlock`→air over ~4 s. Seven-circuit
`Reach` chain, collapse on the 7th. Theme: Joshua 6.

**F11 Transfiguration radiance** `[M · high]` — drop `GlowHdrThreshold` so the whole scene
*dazzles* + emissive-white robe + `glory_beam` shaft + sky→near-white + narrator-synced flash.
Theme: Matthew 17.

**F12 Locust swarm** `[M · med]` — ~2000-particle roiling additive cloud rolling in from the
east, dimming light as it passes, stripping crop/leaf blocks (`SetBlock`), buzzing audio.
Directional so the player can outrun the *edge* but not save everything. Theme: Exodus 10.

**F13 Star of Bethlehem** `[M · high]` — single bright star with diffraction spikes (sky-shader
`pow(dot,2200)` core + cross spikes) that tracks westward then stops + a descending `glory_beam`
shaft over the stable. Theme: Matthew 2.

**F14 Dove release** `[S · med]` *(wildcard)* — scripted white dove(s) ascending (D4); Noah's
ark, Jesus' baptism. Soft glow + sparkle.

**F15 Crucifixion darkness / eclipse** `[S · high]` *(wildcard)* — F5 darkness tech as a
midday eclipse: sun disc occluded, unnatural dusk, "darkness over all the land." Theme:
Matthew 27:45.

### G. Post-Processing & Screen-Space Polish  *(fleet-generated)*

**G1 Biome & time color grade** `[M · high]` — drive WorldEnvironment `Adjustment*` +
`GradientTexture1D` LUT; named grades (Dawn/Noon/Dusk/Night/Storm/Desert/Flashback) lerped
smoothly. Sepia flashback for narration; storm desaturate for plagues.

**G2 Volumetric depth haze** `[S · high]` — enable WorldEnvironment volumetric fog, tint-synced
to the existing fog-color lerp; instant scale/grandeur (Pisgah overlook).

**G3 Screen-space godrays** `[M · high]` — full-screen `godrays.gdshader` (CanvasLayer):
64-sample radial march toward the sun's projected UV, threshold-accumulate; gated by low-sun
visibility. (= C7.)

**G4 Heat haze (lava & desert)** `[M · med]` — full-screen `heat_haze.gdshader`: fbm screen-UV
offset masked to lower screen + vertical scroll; strength from nearby lava / desert biome.
Port `heat_distortion.gdshader` core.

**G5 Chromatic aberration on damage** `[S · med]` — separate `chroma.gdshader` layer (CA can't
live in `damage.gdshader` — it doesn't read the screen); RGB split scaled by edge distance,
decays with `flash_amount` via `Fx.OnFlash`/`ScreenFx`.

**G6 Depth of field for dialogue** `[S · med]` — WorldEnvironment DoF far-blur tweened in on
dialogue/cutscene, focus = player↔NPC distance; subtle (blocky game). Permanent tiny near-blur
to separate the held item.

**G7 Eye adaptation on cave exit** `[S · high]` — WorldEnvironment auto-exposure + an
exaggerated spike on cave→daylight transition (detect solid block above head).

**G8 Lesson-moment cinematic letterbox** `[S · high]` — top/bottom `ColorRect` bars tween in +
grade shift (desat/contrast) + HUD fade; verse text in the band. `CinematicBar.Open/Close`
from a lesson script.

**G9 Subtle film grain** `[S · med]` — full-screen `film_grain.gdshader`, per-frame hash noise
quantized to 24 fps, strongest in shadows; default ~0.028 (barely perceptible), up to 0.06 in
Flashback grade.

**G10 Vignette layer (reactive)** `[S · med]` — always-on `vignette.gdshader` (extracts the
`distance(SCREEN_UV,.5)` formula already in underwater/damage); base 0.28, tightens on sprint,
fades during cinematics.

**G11 Bloom presets by context** `[S · high]` — `SetGlowPreset(Normal/Divine/Plague/Cave)`
tweening WorldEnvironment glow. Divine makes set-pieces suffuse with light; Plague crushes it
flat; Cave makes torch glow pop.

**G12 Underwater caustic light projection** `[M · high]` — extend `underwater.gdshader` with
the `min(ca,cb)` two-layer caustic overlay (port from `seabed`). Submerged screen-space shimmer.

**G13 (= C11) Weather transition crossfade** — same work as C11; do once.

### H. Wildcards / atmospheric extras

**H1 Aurora / heavenly lights** `[M · med]` — sky-shader ribbon for special nights (Job's
heavens, nativity).
**H2 Shooting stars / meteors** `[S · low]` — occasional streaks at night (stars already exist).
**H3 Eclipse** — see F15.
**H4 Dust devils** `[S · low]` — small swirling desert columns (mini C5).
**H5 Rain puddles + reflections** `[M · med]` — temporary reflective puddle decals after rain.
**H6 Footprints in snow / sand** `[M · med]` — trailing decals (pairs with C9 / E3).
**H7 Breath fog (cold biomes)** `[S · low]` — player/mob exhale puff.
**H8 Torches sizzle out in rain / water** `[S · med]` — interaction: extinguish + steam wisp +
hiss; relight mechanic. Great immersion.

### X. Cross-cutting architecture

**X1 Shared noise include** `fx_noise.gdshaderinc` (hash/fbm/`fire_ramp`) used by
sky/voxel/water/fire — C8 cloud shadows + B5 caustics need the *same* noise across shaders or
they drift.
**X2 Conductor tweening** — `EnvironmentController` "tween everything" pass (umbrella over
C11/G13 and the grade/glow/fog presets).
**X3 `MiracleFx` director** — one-line set-piece API (flash/shake/beam/grade-preset/time-pin)
for lesson scripts.
**X4 FX budget / LOD manager** — cap live lights + per-source particles + `FogVolume`s by distance/count
(fire lights and volumetric fog are the perf risks). `FireController` already implements this for fires;
generalise it as new per-source effects (C13 fog volumes, etc.) come online.
**X5 Stylization pass discipline** — every realistic reference gets a flat/gradient/chunky
re-skin; review against the blocky palette before shipping.

---

## 2.5 The showcase world (`--showcase`)

Every phase ships its demos into ONE interactive, hand-built stage so the work is always
**visible and walkable** — not just screenshots that capture-and-quit. Launch from the title
screen → **Showcases** submenu (**Effects Showcase** = the FX stage; **Block Gallery** = one
pillar of every block type), or directly via `Godot ... --path . -- --showcase`.

- Built by `WorldGen.FxShowcase(world)` — a *static* flat stage (no streaming, so nothing
  clobbers it) — plus `ShowcaseController` (hotkeys) and billboarded `Signpost` station labels.
- **Convention: each phase adds a labeled station** for its new effects (extend
  `FxShowcase` + drop a signpost). The world grows into a full FX gallery over time.
- **Controls:** **F5** weather (Clear/Rain/Snow) · **F6** time (Morning/Noon/Dusk/Night/auto)
  · **F7** glow preset (Normal/Divine/Plague/Cave) · **V** fly · **Shift** sprint. `SafeMode`
  is on (no fall/hazard damage).

**Phase 0 stations:** grass-wind plain · footstep-material bands (walk north:
dirt→sand→stone→snow→planks→cloth) · jump tower (landing dust) · water pool (splash) · lamp
cluster (bloom, vivid at night / Divine glow) · far ridge (depth haze).

**Phase 1 stations:** **Fire & Light** (x21–33, z100–108, near spawn) — a candle, torch, campfire,
forge, brazier and a tall altar fire, each flickering with its own light + embers (bigger ones trail
smoke). **[H]** cycles the flame palette (Normal → Holy white-gold → Forge red); reads best at dusk /
night (**[F6]**).

## 3. Phased roadmap

Ordered for dependency + polish-per-hour. Each phase is shippable on its own.

### Phase 0 — Foundation & cheap global sheen
*Architecture + param-tween wins; mostly shader/C#, few new assets. Lifts the whole game.*
- X1 shared noise include · X2 conductor tweening · X4 FX budget scaffold · X5 stylization discipline
- C11/G13 weather transition smoothing · C6 wind-driven grass
- G2 depth haze · G1 biome/time color grade · G11 bloom presets · G10 vignette · G9 film grain · G7 eye adaptation
- E3 footstep dust · E4 landing impact (Fx already there — wire)

**Status (2026-06-16): mostly DONE.** Implemented + build-green + smoke-tested in-game:
- **C6** wind-driven grass — `vegetation.gdshader` gained `wind_dir` + dynamic `sway_amount`,
  driven from `EnvironmentController.UpdateWeather` via `Vegetation.Material`.
- **C11/G13** weather smoothing — `EnvironmentController.SetWeather` now sets targets; cloud
  coverage, precip `AmountRatio`, and fog density ease in `UpdateWeather`.
- **G1** color grade — subtle time/weather Adjustment lerp in `UpdatePostFx`.
- **G11** bloom presets — `SetGlowPreset(Normal/Divine/Plague/Cave)` eased in `UpdatePostFx`
  (ready for lessons; no visible change at Normal, as intended).
- **G2** depth haze — WorldEnvironment volumetric fog enabled, sky-tinted, kept very light
  (density 0.0035, length 64, `SkyAffect`=0) after a too-dense first pass looked murky.
- **G9+G10** grain + vignette — one combined `assets/shaders/post.gdshader` screen pass
  mounted in `GameHud` (over scene, under UI); vignette tightens on sprint (`Player.Sprinting`).
- **E3+E4** footstep + landing dust — `Player.cs` emits `Fx.Dust`/`Fx.Poof` tinted by the
  `MaterialSound` underfoot (sand/grass/snow/stone/wood), skipped over liquid/air.

**Deferred (deliberately):**
- **G7 eye adaptation** — needs Godot-4 `CameraAttributesPractical.AutoExposure*` (the fleet
  spec assumed the Godot-3 `Environment.AutoExposure*` API, which doesn't exist in 4.6).
  Auto-exposure also fights the curated Filmic+glow look; wants a careful dedicated pass.
- **X1 shared noise include** — best built alongside its first consumer (C8 cloud shadows /
  B5 caustics in Phase 1) to avoid a duplicated-then-bit-rotted include.
- **X4 FX budget/LOD manager** — nothing in Phase 0 needs it; it's a Phase 1 (fire lights) need.

**Known pre-existing issue (not introduced here):** at night, clouds at coverage 0.5 read as a
murky grey/dark streaky band against the near-black sky. Untouched by Phase 0 (no `sky.gdshader`
edits). Fold into Phase 1 "dynamic clouds" / a night-cloud lighting tweak.

### Phase 1 — Living world (fire + water + ambient)
*The signature ambient systems; the trailer's "this world is alive" layer.*
- **Fire:** A1 torch flame + breathing light · A2 campfire/brazier/altar · A3 embers · A4 heat shimmer · A5 smoke · A6 candle/lamp · A7 holy-flame variant · A10 forge glow · X4 fire LOD
- **Water:** B1 flowing rivers · B2 waterfalls+spray · B3 shoreline foam · B4 premium surface · B5 caustics · B6 wet waterline · B7 cave drips · B9 splash-on-entry · B8 lava · B13 ripple rings
- **Ambient:** D-arch AmbientLifeDirector · D1 leaves · D2 fireflies+pollen · D3 butterflies/bees · D4 birds/doves · D5 cave ambience · D7 dandelion · D8 blossom · D9 fish · D10 ash · D6 swamp · D11 critters · D12 flies · D13 tumbleweed
- **Sky tie-in:** C7/G3 godrays · C8 cloud shadows · B12 underwater bubbles · G12 underwater caustics

**Status — Fire batch (2026-06-16): DONE.** Implemented + build-green + verified live in the showcase
(brazier close-up, all three palettes, night). The fire kit lands as *separate 3D node assemblies*
(not block textures — emission is texture-driven and we don't want to author per-fire PNGs), driven
explicitly by a conductor:
- **`assets/shaders/fire_common.gdshaderinc`** — `hash13/vnoise3/fbm3/fbm3_3/fire_ramp` ported verbatim
  from `godot-effects`, plus our own `qstep` band-quantizer for the chunky look. The shared include
  earmarked as **X1** — its first consumers (caustics / cloud shadows) can reuse it.
- **`assets/shaders/flame.gdshader`** — stylized cylindrical-billboard flame (QuadMesh, `blend_add`,
  emissive so Glow blooms it): rising-fbm teardrop silhouette + quantized `fire_ramp`, a per-frame
  `flicker` uniform and `col_*` palette uniforms. The blocky re-skin of `flame_column`/`flame_tongue`.
- **`scripts/core/Fire.cs`** — one fire = stacked flame billboards + a warm `OmniLight3D` + additive
  ember `GpuParticles3D` + (bigger sizes) a wind-bent smoke column. **A1** torch flame+breathing light,
  **A2** campfire/brazier/altar (a `FireKind` per size: Candle/Torch/Campfire/Brazier/Altar/Forge),
  **A3** embers, **A5** smoke, **A6** candle/lamp, **A7** holy-flame variant + **A10** forge (a
  `FirePalette`: Normal/Holy/Forge recolours the ramp, light and embers).
- **`scripts/core/FireController.cs`** — the conductor: one shared `FastNoiseLite` drives every fire's
  fast-twitch + slow-swell + surge flicker (ported from `pillar_of_fire.gd`), wanders the glow, scales
  energy with night, and **budgets live lights + particles by distance & count (X4 fire-LOD)**. Auto-
  lights an altar fire wherever a player places the `altar_fire` block (in lessons, where block-change
  events are armed). Owned by `GameSession`; `AddFire(pos, kind, palette)` is the one-line API lessons
  and the showcase use.
- **Showcase:** a new **Fire & Light** station (one of every size; **[H]** cycles Normal/Holy/Forge —
  H not F8, which is the editor's Stop shortcut).

**Playtest refinements (2026-06-16):** (1) smoke was way oversized — switched to a soft-alpha
`smoke.gdshader` (round fbm puff, COLOR.a life-fade) with much smaller, fewer puffs, so it reads as
thin wisps. (2) From straight overhead the vertical billboard flame goes edge-on and nearly vanishes —
added a flat **coal-bed base glow** disc (`firebase.gdshader`, a horizontal additive PlaneMesh at the
fire base) so a fire still reads as glowing coals from the top *and* pools warm light at the base from
the side. (This is the A2 "ground glow" element.) Known showcase nit surfaced in testing: the FX stage
ground has an unguarded east edge (x>40) you can walk off — fence it later.

**Deferred within Phase 1 fire (deliberately):**
- **A4 heat shimmer** — a screen-reading distortion pass (`ScreenFx`), better batched with G4 desert
  heat haze in Phase 2.
- **Placeable `torch`/`campfire` blocks** — needs torch/campfire textures (the asset pipeline); for now
  fires are placed via `AddFire` (showcase/lessons) + auto on the existing `altar_fire` block.
- **World scan to auto-light pre-placed emissive blocks** — `BlockChanged` only fires for armed
  PlayerEdits, so lesson-authored altar fires need an explicit `AddFire` (or a future startup scan).

**Status — Water batch 1 (2026-06-17): DONE (B1, B2, B9, B13).** Implemented + build-green + verified
live in the showcase (new **Rivers & Waterfall** station; falling curtain reads correctly, no shader
errors). The approach piggy-backs on the existing mesh + flood-fill rather than adding a sim:
- **`ChunkMesher.cs`** bakes two per-cell water-FX values into the free `Custom0.b/.a` float channels:
  a **flow vector** on top faces (**B1** — derived from the static block field: water spills toward open
  air, harder toward a drop) and a **falling-sheet flag** on vertical faces with water above (**B2**).
  Water-face greedy merging is *gated on these matching*, so a still pond stays one big quad (zero
  regression) while a river/curtain splits into the per-cell quads it needs.
- **`assets/shaders/water.gdshader`** reads those channels and renders all motion through the NORMAL
  (animated highlights), not bright additive patches: **B1** is a normal-based directional current
  (advect noise along the flow vector) with thin, sparse, speed-gated foam; **B2** is an aperiodic
  waterfall curtain (multi-layer vertically-scrolled `fbm` streaks + per-lane phase + domain warp,
  `qstep`-banded) with a churning lip-foam band.
- **B13** ripple rings live **inside the water shader** as an impact-uniform ring buffer
  (`VoxelWorld.AddRipple` → `vec4 ripples[12]` + `u_time`): each impact perturbs the surface normal as
  an expanding, fading, multi-crest ripple. Because it runs only on water fragments it never bleeds onto
  banks (the old flat-decal `ripple.gdshader` did), reads as deformation not a shockwave, and scales by
  an impact `size` (raindrop → tiny, stone → small, player → medium).
- **B9** splash-on-entry: the player's splash + a sized ripple; a thrown projectile splashes + ripples
  when it plops into water (`Projectile` takes a `VoxelWorld` to detect liquid — water has no collider).
- **Particles:** the shared `Fx` billboard now uses a soft radial dot (no more hard squares); the
  showcase waterfall mist sorts in front of the water (`render_priority`: water −1, spray +1) with
  proximity-fade soft particles.
- **Showcase:** a **Rivers & Waterfall** station (walled cliff source → curtain → catch pool + base mist)
  and a **Flowing River** station (a stepped cascade descending toward the player so every cell has a
  downstream gradient — the flow model needs a slope to read as a current; a flat channel reads still).

**Playtest redesign (2026-06-17, web-researched — Cyanilux/CaptainProton42/Catlike Coding/Godot docs):**
first pass read as a tiled "U-cup" curtain, big white flow blobs, a shockwave ripple that bled over the
water edge, and spray hidden behind the water. Rebuilt per the research: fbm-streak curtain, normal-based
flow, in-shader ripple ring buffer, soft + priority-sorted spray. Curtain bug-fixes: streaks now scroll
**down** (sign flip), the **lip row falls** too (a face with a drop in front, not just water above) so the
top isn't static, and that lip row gets a foam band so the surface→fall transition is a 2-block churn.

**Deferred within the water batch (deliberately):**
- **Generalised waterfall base spray** — the showcase mist is hand-placed; a generic "spray where a
  falling column lands" needs a controller (the 1-cell mesh snapshot can't see where a tall fall lands
  across chunk borders). Good follow-up, mirrors `FireController`.
- **B3 shoreline foam · B4 premium surface · B5 caustics · B6 wet waterline · B7 cave drips · B8 lava**
  — the rest of the Water catalog; a second water pass.
- Remaining Phase-1 **Sky-tie-in** batch (godrays/cloud-shadows/underwater caustics) is still ahead.

**Status - Ambient life batch (2026-06-17): DONE (D-arch + D1/D2/D3/D4/D7/D8/D9, commit ab33034).** One
self-contained `scripts/core/AmbientLifeDirector` (Node3D, owned by GameSession so every session gets one)
reads biome+time+weather+wind around the player and drives every living-world effect with restraint:
- **Particle fields** (the motes/fireflies GpuParticles pattern): leaves (D1) + blossom petals (D8) near
  trees; light-catching pollen (D2) + wind-borne dandelion fluff (D7) in daytime meadows.
- **Creatures** (billboard quads + 2-frame flap, the Fire `home + sin(t)` kernel): butterflies (D3); birds
  (D4) crossing the sky + a scriptable `ReleaseDove(from,to)` for lesson beats (Noah/baptism); fish (D9)
  darting under nearby water with the odd splashing jump (`Fx.Splash` + `World.AddRipple`).
- Context = shared `TerrainGenerator.BiomeAt` in the streamed sandbox, else nearby world blocks
  (leaves->trees, liquid+open-above->water) so it runs with no generator (showcase/lessons -> Plains). All
  billboard textures generated procedurally (no PNGs). Creature counts recompute on the throttled 0.4s tick.
- Showcase: new **Ambient Life** station (pond + grove) + **[L]** density hotkey (Off/Sparse/Normal/Lush);
  `--test-ambientlife` windowed smoke test. Adversarially reviewed (3 agents) -> fixed dove-culling, count
  thrash, fish-splash frame-drop, decorated-pond water scan, and Env/World teardown guards before commit.
- Deferred within Ambient life: D5 cave ambience, D6 swamp spores, D10 ash, D11 critters, D12 flies, D13
  tumbleweed (niche biome emitters); bees (no hives yet).

### Phase 2 — Weather drama + combat juice
- **Weather:** C1 rain splashes+ripples · C2 valley mist · C3 lightning+thunder · C4 rainbow · C5 sandstorm · C9 snow accumulation · C10 wind debris · **C13 volumetric atmosphere (FogVolume — campfire smoke volumes, valley mist, darkness pockets; upgrades C2)**
- **Combat:** E1 mining crack-stages · E2 textured shards · E5 sprint lines · E6 hit sparks · E7 swing trail · E8 sling trails · E9 damage feedback (incl. G5 chroma) · E10 low-health · E11 place poof · E12 enemy death/Goliath · E13 crit · E14 heal/pickup · E15 UI juice
- **Post:** G4 heat haze · G6 DoF dialogue · G8 cinematic letterbox

### Phase 3 — Biblical set-pieces (stylized) + director
- X3 MiracleFx director (build first)
- F1 Red Sea (stylized) · F2 pillar fire/cloud · F3 burning bush · F4 manna · F5 darkness · F6 fire from heaven · F7 rainbow covenant · F8 glory cloud · F9 blood · F10 Jericho · F11 transfiguration · F12 locusts · F13 star of Bethlehem · F14 dove · F15 crucifixion darkness · A8 glory motes

### Phase 4 — Stretch / heavy / sandbox
- A9 fire spread (gated) · B10 large stylized water (+ FFT only if a hero map ever needs it) · B11 boat wake
- H1 aurora · H2 meteors · H4 dust devils · H5 puddles · H6 footprints · H7 breath fog · H8 torch sizzle-out

---

## 4. Provenance & open follow-ups

- **Sources:** brainstormed 2026-06-16 via a 7-domain agent workflow (`fx-master-brainstorm`,
  run `wf_8d1d02be-de4`) over surveys of the two reference repos + the current engine.
- **Fleet-authored, full technique specs:** C (Weather/Sky), F (Biblical set-pieces),
  G (Post-processing). The raw workflow output (with extra per-idea detail, exact uniforms,
  and Bible references) was saved at the time to the run's task output; this doc distills it.
- **Hand-authored (during a platform API incident that killed 4 fleet agents):** A (Fire),
  B (Water), D (Ambient life), E (Combat), H (wildcards). **Follow-up:** re-run those four
  domains through the fleet once the API is stable to deepen them to the same spec level as
  C/F/G (exact node params, shader uniforms). Resume: `Workflow({scriptPath: ".../workflows/
  scripts/fx-master-brainstorm-wf_8d1d02be-de4.js", resumeFromRunId: "wf_8d1d02be-de4"})`.
- **Decisions locked:** all 4 focus areas in scope for v1; stylized-over-realistic is the
  governing aesthetic rule (esp. water / Red Sea); capture-everything (this doc) so nothing is
  lost across sessions.
