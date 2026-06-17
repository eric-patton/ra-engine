using System.Collections.Generic;
using Godot;
using RAEngine.Core;

namespace RAEngine.PlayerSys;

/// <summary>A first-person "held block" viewmodel: a small textured cube of the
/// currently selected hotbar block, parented to the player's camera head at a
/// bottom-right offset (mirroring <c>WeaponController</c>). It rebuilds when the
/// hotbar selection changes and reuses the shared voxel material + the block's
/// per-face texture layers, so the in-hand block matches the world exactly.
/// When no block is selected an empty-hand (fist) viewmodel is shown instead.</summary>
public partial class HeldItem : Node3D
{
    public Player Player;
    public VoxelWorld World;

    private MeshInstance3D _cube;
    private MeshInstance3D _hand;            // empty-hand fist, shown when _current == 0
    private readonly Dictionary<ushort, ArrayMesh> _meshes = new();
    private bool _visibleMode = true;
    private ushort _current;

    // Resting transforms for both viewmodels (must match what AttachViewmodel sets).
    private static readonly Vector3 CubeRestPos = new(0.30f, -0.28f, -0.5f);
    private static readonly Vector3 CubeRestRot = new(-12f, 28f, 6f);
    private static readonly Vector3 HandRestPos = new(0.28f, -0.26f, -0.48f);
    private static readonly Vector3 HandRestRot = new(-8f, 22f, 4f);

    // Swing animation state: one tween runs at a time; cadence timer gates repeats.
    private Tween _swingTween;
    private float _swingCadence;            // countdown to next auto-swing while held
    private const float SwingCadence = 0.25f;
    private const float SwingOut   = 0.06f;
    private const float SwingBack  = 0.14f;

    /// <summary>Parent both viewmodels to the camera head. Call once after the player exists.</summary>
    public void AttachViewmodel()
    {
        if (Player == null || _cube != null) return;

        _cube = new MeshInstance3D
        {
            Name = "HeldCube",
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Player.Head.AddChild(_cube);
        _cube.Position = CubeRestPos;
        _cube.RotationDegrees = CubeRestRot;
        _cube.Scale = Vector3.One * 0.22f;

        // Empty-hand fist: a short, slightly tapered box in skin tone.
        _hand = new MeshInstance3D
        {
            Name = "HeldHand",
            Mesh = new BoxMesh { Size = new Vector3(0.10f, 0.10f, 0.22f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.88f, 0.72f, 0.57f) },
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Player.Head.AddChild(_hand);
        _hand.Position = HandRestPos;
        _hand.RotationDegrees = HandRestRot;
    }

    /// <summary>Show/hide both viewmodels with the mode (held in build mode, hidden in
    /// adventure where the weapon viewmodel shows instead).</summary>
    public void SetShown(bool on)
    {
        _visibleMode = on;
        if (_cube != null) _cube.Visible = on && _current != 0;
        if (_hand != null) _hand.Visible = on && _current == 0;
    }

    public void OnSelectionChanged(int blockId)
    {
        _current = (ushort)blockId;
        if (_cube == null) return;
        if (_current == 0)
        {
            _cube.Visible = false;
            if (_hand != null) _hand.Visible = _visibleMode;
            return;
        }
        _cube.Mesh = MeshFor(_current);
        _cube.MaterialOverride = World?.Textures?.Material;
        _cube.Visible = _visibleMode;
        if (_hand != null) _hand.Visible = false;
    }

    /// <summary>Play a quick Minecraft-style punch swing on whichever viewmodel is
    /// currently active. Kills any in-flight tween so rapid swings stay snappy.</summary>
    public void Swing()
    {
        // Pick the active viewmodel.
        MeshInstance3D vm = (_current != 0) ? _cube : _hand;
        if (vm == null || !vm.Visible) return;

        Vector3 restPos = (_current != 0) ? CubeRestPos : HandRestPos;
        Vector3 restRot = (_current != 0) ? CubeRestRot : HandRestRot;

        // Swing: dip forward/down 45° and nudge closer, then spring back.
        Vector3 swingRot = restRot + new Vector3(-45f, 0f, 0f);
        Vector3 swingPos = restPos + new Vector3(0f, -0.04f, 0.08f);

        _swingTween?.Kill();
        _swingTween = CreateTween().SetParallel(false);
        // Out: rotate and dip simultaneously, then spring both back.
        _swingTween.TweenProperty(vm, "rotation_degrees", swingRot, SwingOut);
        _swingTween.Parallel().TweenProperty(vm, "position", swingPos, SwingOut);
        _swingTween.TweenProperty(vm, "rotation_degrees", restRot, SwingBack);
        _swingTween.Parallel().TweenProperty(vm, "position", restPos, SwingBack);
    }

    public override void _Process(double delta)
    {
        if (!_visibleMode || Player == null || !Player.InputEnabled || Player.ActionsSuppressed)
        {
            _swingCadence = 0f;
            return;
        }

        bool mouse = Input.MouseMode == Input.MouseModeEnum.Captured;

        // While mining (break held): swing on a repeating cadence.
        bool breaking = (mouse && Input.IsActionPressed(GameInput.Actions.Primary))
                        || Input.IsActionPressed(GameInput.Actions.KbBreak);
        if (breaking)
        {
            _swingCadence -= (float)delta;
            if (_swingCadence <= 0f)
            {
                Swing();
                _swingCadence = SwingCadence;
            }
        }
        else
        {
            _swingCadence = 0f;  // reset so the next press fires immediately
        }

        // On place (just pressed): single swing.
        bool placing = (mouse && Input.IsActionJustPressed(GameInput.Actions.Secondary))
                       || Input.IsActionJustPressed(GameInput.Actions.KbPlace);
        if (placing) Swing();
    }

    private ArrayMesh MeshFor(ushort id)
    {
        if (_meshes.TryGetValue(id, out var m)) return m;
        m = BuildCube(BlockRegistry.Get(id));
        _meshes[id] = m;
        return m;
    }

    /// <summary>Build a single unit cube centred on the origin, textured per face via
    /// the voxel shader's CUSTOM0 layer channel (AO = 1 so it is evenly lit).</summary>
    private static ArrayMesh BuildCube(BlockType b)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbaFloat);
        Vector3 center = new(0.5f, 0.5f, 0.5f);
        int baseIdx = 0;
        for (int f = 0; f < 6; f++)
        {
            float layer = b.FaceLayer[f];
            Vector3 normal = VoxelGeometry.Normals[f];
            var tangent = new Plane(VoxelGeometry.Tangents[f], 1f);
            for (int v = 0; v < 4; v++)
            {
                Vector3 corner = VoxelGeometry.Corners[f][v]; // unit cube, components in [0,1]
                // Mirror ChunkMesher.EmitQuad UVs so the in-hand block matches the world.
                // Side faces (+/-X, +/-Z) map V to world-up so directional side textures
                // (the grass fringe, brick courses) stay upright instead of flipping upside
                // down; top/bottom faces (f 2,3) use the default mapping.
                Vector2 uv;
                if (f == 2 || f == 3)
                    uv = VoxelGeometry.Uvs[v];
                else
                {
                    float hCoord = (f == 0 || f == 1) ? corner.Z : corner.X; // +/-X uses Z, +/-Z uses X
                    uv = new Vector2(hCoord, 1f - corner.Y);   // V flipped: image-top sits at block-top
                }
                st.SetNormal(normal);
                st.SetTangent(tangent);
                st.SetUV(uv);
                st.SetCustom(0, new Color(layer, 1f, 0f, 0f));
                st.AddVertex(corner - center);
            }
            foreach (int t in VoxelGeometry.TriOrder) st.AddIndex(baseIdx + t);
            baseIdx += 4;
        }
        return st.Commit();
    }
}
