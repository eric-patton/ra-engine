# RA Engine — Claude Code notes

Godot 4.6 C# voxel game (Minecraft-like, for a weekly Bible class), built and tested
live through the **godot-ai** MCP bridge with the editor open. Build with
`dotnet build "RA Engine.csproj"`.

## Capturing in-game screenshots — always use the free-fly photo camera

When you need to see or screenshot something in the *running* game (a scene, an effect,
a particular framing), **use the built-in free-fly "photo mode" camera** — don't try to
aim the first-person player camera. The bridge can't drive FPS look (relative mouse
motion can't be injected) and the player collides/falls; the photo camera has no
collision, renders clean shots (no viewmodel/held block), and is built to be positioned
precisely over the bridge.

How to use it:

1. **Reach a session.** `project_run(mode=main)` → poll `editor_state` until
   `game_capture_ready=true`. The game boots to the main menu (CLI flags aren't passed
   when launched from the editor), so click in with `game_manage` `get_ui_elements` +
   `input_mouse`. The session lives under `/root/Game/Session` (player at
   `/root/Game/Session/Player`).
2. **Enter photo mode: press `P`** (`game_manage input_key` `{key:"P"}` — send `pressed:true`
   then `pressed:false`). The player freezes and the free cam
   (`/root/Game/Session/FreeCam`) becomes the current camera. Press `P` again to hand
   control back to the player (it returns exactly where it was).
3. **Press `T` for step mode — this is the precise, overshoot-free control.** Each
   WASD / Space / Ctrl tap moves an exact **2 m**, each arrow tap turns an exact **15°**
   (hold **Shift** for fine: 0.5 m / 5°). Because each step fires on key-down, one tap
   (down+up) = exactly one increment no matter how long the bridge round-trip takes — so
   it never overshoots. (Without `T` it's smooth continuous flight — WASD/Space/Ctrl,
   mouse + arrows, wheel = speed — good for *gross* moves, but a held key keeps moving
   for the whole round-trip and overshoots wildly, so don't use continuous mode to aim.)
4. **Verify + shoot.** Read the exact transform back with `game_manage get_node_info`
   on `/root/Game/Session/FreeCam` (`position`, `rotation` in radians, `fov`; the
   bottom-left on-screen readout shows the same in degrees, so it's baked into the
   screenshot). Capture with `editor_screenshot(source="game", max_resolution=0)`.

**Recommended workflow:** fly roughly into place in continuous mode, then press `T` and
use step taps for exact final position + aim. This input-driven-with-readback loop is
necessary because the bridge can only inject input and read properties on a running
game — it can't set a running node's transform directly.

Keys: **P** = toggle photo camera, **T** = toggle step mode. Both are letters on
purpose — the embedded editor steals the **F5–F8** row even when the game is focused
(F8 = Stop silently kills the running game), so avoid those for any in-game hotkey you
drive over the bridge.
