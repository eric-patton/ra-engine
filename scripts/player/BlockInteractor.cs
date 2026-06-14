using Godot;
using RAEngine.Core;
using RAEngine.UI;

namespace RAEngine.PlayerSys;

/// <summary>Targets blocks along the camera ray, draws a selection outline, and
/// breaks/places blocks. Placement is blocked when it would intersect the
/// player. Editing can be disabled (e.g. story sections of a lesson).</summary>
public partial class BlockInteractor : Node3D
{
    public VoxelWorld World;
    public Player Player;
    public Hotbar Hotbar;
    /// <summary>When set (survival sandbox), breaking collects blocks and placing
    /// consumes them. Null (creative/editor) means unlimited blocks.</summary>
    public Inventory Inventory;
    public bool CanEdit = true;
    public float Reach = 6f;

    private MeshInstance3D _highlight;
    private VoxelRay.Hit _target;
    private float _breakTimer, _placeTimer;
    private const float RepeatDelay = 0.18f;

    // Gradual mining: holding break chips away at the targeted block over a
    // hardness-scaled time, with a progressive "cracks" overlay on the block.
    private MeshInstance3D _crack;
    private StandardMaterial3D _crackMat;
    private ImageTexture[] _crackStages;
    private Vector3I _miningCell;
    private bool _mining;
    private float _miningProgress; // 0..1
    private int _crackStage = -1;
    private float _hitSfx;

    /// <summary>The block currently under the crosshair (for the level editor).</summary>
    public VoxelRay.Hit CurrentTarget => _target;

    public override void _Ready()
    {
        _highlight = new MeshInstance3D { Name = "Highlight", Mesh = BuildWireCube() };
        _highlight.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0, 0, 0, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _highlight.Visible = false;
        AddChild(_highlight);

        // Crack overlay: a slightly inflated, unshaded, transparent cube whose
        // albedo is swapped through procedurally generated damage stages as mining
        // progresses. Built once; the stage textures are generated on first use.
        _crackMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Back,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        };
        _crack = new MeshInstance3D
        {
            Name = "Crack",
            Mesh = new BoxMesh { Size = new Vector3(1.02f, 1.02f, 1.02f) },
            MaterialOverride = _crackMat,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_crack);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (World == null || Player == null) return;
        UpdateTarget();

        // Don't break/place when editing is off, the player can't act, or we just
        // re-captured the mouse (so the re-focus click doesn't also dig/build).
        if (!CanEdit || !Player.InputEnabled || Player.ActionsSuppressed)
        {
            _breakTimer = _placeTimer = 0f;
            ResetMining();
            return;
        }

        // Break is a gradual hold (mining); place stays an instant, auto-repeating
        // action. Both read the mouse button WHILE the cursor is captured, or the
        // keyboard (+/- · ,/.) at any time — so keyboard-only play works.
        float dt = (float)delta;
        bool mouse = Input.MouseMode == Input.MouseModeEnum.Captured;
        UpdateMining(mouse, dt);
        StepAction(mouse, GameInput.Actions.Secondary, GameInput.Actions.KbPlace, ref _placeTimer, TryPlace, dt);
    }

    /// <summary>Chip away at the targeted block while the break button is held. The
    /// time to break scales with the block's <see cref="BlockType.Hardness"/>;
    /// changing target, releasing, or looking away resets progress.</summary>
    private void UpdateMining(bool mouse, float dt)
    {
        bool held = (mouse && Input.IsActionPressed(GameInput.Actions.Primary))
                    || Input.IsActionPressed(GameInput.Actions.KbBreak);

        if (!held || !_target.Ok) { ResetMining(); return; }

        // (Re)start when we begin holding or the target moves to a new block.
        if (!_mining || _target.Block != _miningCell)
        {
            _mining = true;
            _miningCell = _target.Block;
            _miningProgress = 0f;
            _hitSfx = 0f;
        }

        var b = World.GetBlock(_miningCell);
        if (b.IsAir) { ResetMining(); return; }

        if (b.Hardness <= 0f) { BreakAt(_miningCell); ResetMining(); return; } // instant

        _miningProgress += dt / b.Hardness;

        // Periodic soft "tap" while mining (quiet, throttled).
        _hitSfx -= dt;
        if (_hitSfx <= 0f) { AudioManager.Play("step", 0.7f, -7f); _hitSfx = 0.26f; }

        if (_miningProgress >= 1f)
        {
            BreakAt(_miningCell);
            // Released-and-held chain mining: clear progress so the next block
            // (re-acquired next frame) starts from zero rather than insta-breaking.
            _mining = false;
            _miningProgress = 0f;
        }
        UpdateCrackVisual();
    }

    private void ResetMining()
    {
        if (!_mining && _miningProgress == 0f && (_crack == null || !_crack.Visible)) return;
        _mining = false;
        _miningProgress = 0f;
        _crackStage = -1;
        if (_crack != null) _crack.Visible = false;
    }

    private void UpdateCrackVisual()
    {
        if (_crack == null) return;
        if (!_mining || _miningProgress <= 0f) { _crack.Visible = false; return; }
        EnsureCrackStages();
        int stage = Mathf.Clamp((int)(_miningProgress * _crackStages.Length), 0, _crackStages.Length - 1);
        if (stage != _crackStage)
        {
            _crackStage = stage;
            _crackMat.AlbedoTexture = _crackStages[stage];
            // Each new crack stage spits a few chips and gives a tiny impact kick, so
            // chipping away at a block feels weighty (fires ~8 times over a full mine).
            var mb = World.GetBlock(_miningCell);
            Color chipTint = World.Textures?.AverageColor(mb) ?? new Color(0.6f, 0.6f, 0.6f);
            Fx.Burst((Vector3)_miningCell + new Vector3(0.5f, 0.5f, 0.5f), FxKind.Debris, chipTint, 4);
            Fx.Shake(0.035f);
        }
        _crack.GlobalPosition = (Vector3)_miningCell + new Vector3(0.5f, 0.5f, 0.5f);
        _crack.Visible = true;
    }

    /// <summary>Generate the 8 cumulative crack-damage stages once. Each stage draws
    /// the previous cracks plus a couple more jagged lines radiating from the centre,
    /// onto a transparent texture so only the dark cracks show over the block.</summary>
    private void EnsureCrackStages()
    {
        if (_crackStages != null) return;
        const int n = 8, s = 64;
        _crackStages = new ImageTexture[n];
        for (int stage = 0; stage < n; stage++)
        {
            var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
            img.Fill(new Color(0, 0, 0, 0));
            int cracks = 2 + stage * 2; // cumulative: crack c is deterministic in c
            for (int c = 0; c < cracks; c++)
            {
                uint h = ValueNoise2D.Hash(c, 7, 1234);
                float ang = (h % 360u) * Mathf.Pi / 180f;
                float len = s * (0.28f + ((h >> 9) % 100u) / 100f * 0.22f);
                DrawCrack(img, s / 2f, s / 2f, ang, len, h);
            }
            img.GenerateMipmaps();
            _crackStages[stage] = ImageTexture.CreateFromImage(img);
        }
    }

    private static void DrawCrack(Image img, float x, float y, float ang, float len, uint seed)
    {
        int s = img.GetWidth();
        int steps = (int)len;
        for (int i = 0; i < steps; i++)
        {
            uint hh = ValueNoise2D.Hash((int)(seed & 0xFFFF), i, 99);
            float jitter = ((hh % 100u) / 100f - 0.5f) * 0.7f; // jagged wander
            float a = ang + jitter;
            x += Mathf.Cos(a);
            y += Mathf.Sin(a);
            if (x < 1 || y < 1 || x > s - 2 || y > s - 2) break;
            PlotDark(img, (int)x, (int)y, 0.85f);
            PlotDark(img, (int)x + 1, (int)y, 0.4f);
            PlotDark(img, (int)x, (int)y + 1, 0.4f);
        }
    }

    private static void PlotDark(Image img, int x, int y, float a)
    {
        if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) return;
        float na = Mathf.Max(img.GetPixel(x, y).A, a);
        img.SetPixel(x, y, new Color(0.04f, 0.03f, 0.02f, na));
    }

    // Fire once on press, then auto-repeat while held. Break and place use
    // independent timers, so holding one never suppresses the other. The mouse
    // button only counts when the cursor is captured; the keyboard key always does.
    private void StepAction(bool mouse, string mouseAct, string keyAct, ref float timer, System.Func<bool> act, float dt)
    {
        bool just = (mouse && Input.IsActionJustPressed(mouseAct)) || Input.IsActionJustPressed(keyAct);
        bool held = (mouse && Input.IsActionPressed(mouseAct)) || Input.IsActionPressed(keyAct);
        if (just) { act(); timer = RepeatDelay; return; }
        if (held)
        {
            timer -= dt;
            if (timer <= 0f) { act(); timer = RepeatDelay; }
        }
        else timer = 0f;
    }

    private void UpdateTarget()
    {
        // Show the targeting outline whenever the player can act — including in
        // click-to-capture / keyboard modes where the cursor isn't captured.
        if (!Player.InputEnabled)
        {
            _highlight.Visible = false;
            _target = default;
            return;
        }
        Camera3D cam = Player.Camera;
        Vector3 origin = cam.GlobalPosition;
        Vector3 dir = -cam.GlobalTransform.Basis.Z;
        _target = VoxelRay.Cast(World, origin, dir, Reach);
        if (_target.Ok)
        {
            _highlight.Visible = true;
            _highlight.GlobalPosition = _target.Block;
        }
        else _highlight.Visible = false;
    }

    public bool TryBreak()
    {
        if (!_target.Ok) return false;
        return BreakAt(_target.Block);
    }

    public bool TryPlace()
    {
        if (!_target.Ok || Hotbar == null) return false;
        return PlaceAt(_target.Prev, Hotbar.SelectedBlockId);
    }

    public bool BreakAt(Vector3I cell)
    {
        var b = World.GetBlock(cell);
        if (b.IsAir) return false;
        World.SetBlock(cell, 0);
        if (!b.IsLiquid) Inventory?.Add(b.Id); // collect the dropped block
        AudioManager.Play("break");
        if (!b.IsLiquid)
        {
            // Throw a burst of debris tinted by the broken block (brown crumbs, green
            // flecks, grey chips) and a small kick for impact.
            Color tint = World.Textures?.AverageColor(b) ?? new Color(0.6f, 0.6f, 0.6f);
            Fx.Burst((Vector3)cell + new Vector3(0.5f, 0.5f, 0.5f), FxKind.Debris, tint, 16);
            Fx.Shake(0.05f);
        }
        return true;
    }

    public bool PlaceAt(Vector3I cell, ushort id)
    {
        if (id == 0) return false;
        if (Inventory != null && !Inventory.Has(id)) return false; // nothing to place
        var existing = World.GetBlock(cell);
        if (!existing.IsAir && !existing.IsLiquid) return false;
        if (WouldHitPlayer(cell) && BlockRegistry.Get(id).Solid) return false;
        World.SetBlock(cell, id);
        Inventory?.TryConsume(id);
        AudioManager.Play("place");
        // A soft dust poof, lightened from the placed block's colour.
        Color poof = (World.Textures?.AverageColor(BlockRegistry.Get(id)) ?? Colors.White).Lerp(Colors.White, 0.4f);
        Fx.Burst((Vector3)cell + new Vector3(0.5f, 0.5f, 0.5f), FxKind.Poof, poof, 10);
        return true;
    }

    private bool WouldHitPlayer(Vector3I cell)
    {
        if (Player == null) return false;
        Vector3 p = Player.GlobalPosition;            // feet (bottom of the capsule)
        const float r = 0.36f;                        // capsule radius (0.35) + a hair
        float h = Player.CollisionHeight;             // 1.7 standing, 1.1 crouching
        var pMin = new Vector3(p.X - r, p.Y, p.Z - r);
        var pMax = new Vector3(p.X + r, p.Y + h, p.Z + r);
        var cMin = (Vector3)cell;
        var cMax = cMin + Vector3.One;
        return pMin.X < cMax.X && pMax.X > cMin.X
            && pMin.Y < cMax.Y && pMax.Y > cMin.Y
            && pMin.Z < cMax.Z && pMax.Z > cMin.Z;
    }

    private static ArrayMesh BuildWireCube()
    {
        // 12 edges of a unit cube, inset slightly to avoid z-fighting.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);
        float a = -0.002f, b = 1.002f;
        Vector3[] c =
        {
            new(a, a, a), new(b, a, a), new(b, a, b), new(a, a, b),
            new(a, b, a), new(b, b, a), new(b, b, b), new(a, b, b),
        };
        int[,] edges =
        {
            { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
            { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
        };
        for (int i = 0; i < 12; i++)
        {
            st.AddVertex(c[edges[i, 0]]);
            st.AddVertex(c[edges[i, 1]]);
        }
        return st.Commit();
    }
}
