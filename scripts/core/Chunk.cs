using Godot;

namespace RAEngine.Core;

/// <summary>One cubic region of the voxel world. Owns its render mesh(es) and
/// a static collision body. Block ids are stored in a flat array.</summary>
public sealed partial class Chunk : Node3D
{
    public const int Size = 16;
    public const int Volume = Size * Size * Size;

    public Vector3I Coord;
    public readonly ushort[] Blocks = new ushort[Volume];
    public bool Dirty = true;
    public int SolidCount;

    /// <summary>Bumped on every block mutation. A mesh job captures this at dispatch
    /// time; if it differs when the job's result returns, the chunk changed while
    /// meshing off-thread and the stale result is discarded and re-meshed.</summary>
    public int MeshVersion;

    /// <summary>True once <see cref="ApplyMesh"/> has run at least once, so the
    /// streamer/player can tell a freshly created chunk apart from a meshed one.</summary>
    public bool Meshed { get; private set; }

    private MeshInstance3D _opaque;
    private MeshInstance3D _water;
    private CollisionShape3D _col;
    private MultiMeshInstance3D _veg;

    public static int Index(int x, int y, int z) => (y * Size + z) * Size + x;
    public static bool InBounds(int x, int y, int z) =>
        x >= 0 && x < Size && y >= 0 && y < Size && z >= 0 && z < Size;

    public ushort GetLocal(int x, int y, int z) => Blocks[Index(x, y, z)];

    public void SetLocal(int x, int y, int z, ushort id)
    {
        int i = Index(x, y, z);
        ushort old = Blocks[i];
        if (old == id) return;
        if (old == 0 && id != 0) SolidCount++;
        else if (old != 0 && id == 0) SolidCount--;
        Blocks[i] = id;
        Dirty = true;
        MeshVersion++;
    }

    public void RecomputeSolid()
    {
        SolidCount = 0;
        for (int i = 0; i < Volume; i++)
            if (Blocks[i] != 0) SolidCount++;
        MeshVersion++;
    }

    public override void _Ready()
    {
        Position = Coord * Size;
        _opaque = new MeshInstance3D { Name = "Opaque" };
        AddChild(_opaque);
        _water = new MeshInstance3D { Name = "Water" };
        AddChild(_water);
        var body = new StaticBody3D { Name = "Body" };
        AddChild(body);
        _col = new CollisionShape3D();
        body.AddChild(_col);
    }

    public void ApplyMesh(ArrayMesh opaque, Material opaqueMat, ArrayMesh water, Material waterMat, Shape3D collision)
    {
        _opaque.Mesh = opaque;
        _opaque.MaterialOverride = opaqueMat;
        _water.Mesh = water;
        _water.MaterialOverride = waterMat;
        _col.Shape = collision;
        _col.Disabled = collision == null;
        Meshed = true;
    }

    /// <summary>Replace this chunk's grass-tuft MultiMesh (freeing any previous one).
    /// Passing null clears it. Built fresh on every remesh.</summary>
    public void SetVegetation(MultiMeshInstance3D veg)
    {
        if (_veg != null) { _veg.QueueFree(); _veg = null; }
        if (veg != null) { _veg = veg; AddChild(veg); }
    }

    /// <summary>True when this chunk has solid collision geometry the player can
    /// stand on. The streamer uses it to avoid dropping the player into the void
    /// before the ground under them has finished meshing.</summary>
    public bool HasCollision => _col != null && _col.Shape != null && !_col.Disabled;
}
