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
            return;
        }

        // Break/place come from the mouse buttons WHILE the cursor is captured, or
        // from the keyboard (+/- · ,/.) at any time — so keyboard-only play works.
        float dt = (float)delta;
        bool mouse = Input.MouseMode == Input.MouseModeEnum.Captured;
        StepAction(mouse, GameInput.Actions.Primary, GameInput.Actions.KbBreak, ref _breakTimer, TryBreak, dt);
        StepAction(mouse, GameInput.Actions.Secondary, GameInput.Actions.KbPlace, ref _placeTimer, TryPlace, dt);
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
