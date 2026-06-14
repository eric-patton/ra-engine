using System.Collections.Generic;
using Godot;
using RAEngine.Core;

namespace RAEngine.PlayerSys;

/// <summary>A first-person "held block" viewmodel: a small textured cube of the
/// currently selected hotbar block, parented to the player's camera head at a
/// bottom-right offset (mirroring <c>WeaponController</c>). It rebuilds when the
/// hotbar selection changes and reuses the shared voxel material + the block's
/// per-face texture layers, so the in-hand block matches the world exactly.</summary>
public partial class HeldItem : Node3D
{
    public Player Player;
    public VoxelWorld World;

    private MeshInstance3D _cube;
    private readonly Dictionary<ushort, ArrayMesh> _meshes = new();
    private bool _visibleMode = true;
    private ushort _current;

    /// <summary>Parent the cube to the camera head. Call once after the player exists.</summary>
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
        _cube.Position = new Vector3(0.30f, -0.28f, -0.5f);
        _cube.RotationDegrees = new Vector3(-12, 28, 6);
        _cube.Scale = Vector3.One * 0.22f;
    }

    /// <summary>Show/hide with the mode (held in build mode, hidden in adventure
    /// where the weapon viewmodel shows instead).</summary>
    public void SetShown(bool on)
    {
        _visibleMode = on;
        if (_cube != null) _cube.Visible = on && _current != 0;
    }

    public void OnSelectionChanged(int blockId)
    {
        _current = (ushort)blockId;
        if (_cube == null) return;
        if (_current == 0) { _cube.Visible = false; return; }
        _cube.Mesh = MeshFor(_current);
        _cube.MaterialOverride = World?.Textures?.Material;
        _cube.Visible = _visibleMode;
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
                st.SetNormal(normal);
                st.SetTangent(tangent);
                st.SetUV(VoxelGeometry.Uvs[v]);
                st.SetCustom(0, new Color(layer, 1f, 0f, 0f));
                st.AddVertex(VoxelGeometry.Corners[f][v] - center);
            }
            foreach (int t in VoxelGeometry.TriOrder) st.AddIndex(baseIdx + t);
            baseIdx += 4;
        }
        return st.Commit();
    }
}
