using Godot;

namespace RAEngine.Core;

public enum Face : byte { PosX = 0, NegX = 1, PosY = 2, NegY = 3, PosZ = 4, NegZ = 5 }

public enum RenderType : byte { None, Opaque, Water }

/// <summary>Definition of one block type. Texture names refer to folders under
/// <c>assets/textures/blocks/</c>; layer indices are resolved when the
/// <see cref="BlockTextures"/> arrays are built.</summary>
public sealed class BlockType
{
    public ushort Id;
    public string Name;
    public string DisplayName;

    /// <summary>Texture folder name per face (index by <see cref="Face"/>).</summary>
    public readonly string[] FaceTex = new string[6];
    /// <summary>Resolved texture-array layer per face.</summary>
    public readonly int[] FaceLayer = new int[6];

    public RenderType Render = RenderType.Opaque;
    public bool Solid = true;       // generates collision
    public bool Opaque = true;      // fully hides the neighbouring face behind it
    public bool Climbable = false;
    public bool Hazard = false;
    public float HazardDamage = 0f;
    public bool Emissive = false;
    /// <summary>Grass tufts scatter on top of this block where the cell above is air.</summary>
    public bool SpawnsVegetation = false;
    /// <summary>Seconds to mine this block by hand at base speed. 0 (or less) breaks
    /// instantly; larger is tougher. Set per type in <see cref="BlockRegistry"/>.</summary>
    public float Hardness = 0.6f;

    public bool IsAir => Render == RenderType.None;
    public bool IsLiquid => Render == RenderType.Water;

    public BlockType SetFaces(string all)
    {
        for (int i = 0; i < 6; i++) FaceTex[i] = all;
        return this;
    }

    public BlockType SetFaces(string top, string bottom, string side)
    {
        FaceTex[(int)Face.PosY] = top;
        FaceTex[(int)Face.NegY] = bottom;
        FaceTex[(int)Face.PosX] = FaceTex[(int)Face.NegX] =
            FaceTex[(int)Face.PosZ] = FaceTex[(int)Face.NegZ] = side;
        return this;
    }
}

/// <summary>Static cube face geometry shared by the chunk mesher.
/// Corners are ordered so that du × dv = outward normal; UVs run (0,0),(1,0),
/// (1,1),(0,1) across corners 0..3; tangent = du, binormal sign = +1.</summary>
public static class VoxelGeometry
{
    public static readonly Vector3I[] Normals =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    // o, o+du, o+du+dv, o+dv  for each face (unit cube 0..1).
    public static readonly Vector3[][] Corners =
    {
        new[] { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) }, // +X (du=+Y, dv=+Z)
        new[] { new Vector3(0,0,0), new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0) }, // -X (du=+Z, dv=+Y)
        new[] { new Vector3(0,1,0), new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0) }, // +Y (du=+Z, dv=+X)
        new[] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1) }, // -Y (du=+X, dv=+Z)
        new[] { new Vector3(0,0,1), new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1) }, // +Z (du=+X, dv=+Y)
        new[] { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) }, // -Z (du=+Y, dv=+X)
    };

    public static readonly Vector3[] Tangents =
    {
        new(0, 1, 0), new(0, 0, 1), new(0, 0, 1), new(1, 0, 0), new(1, 0, 0), new(0, 1, 0),
    };

    // Integer in-plane axes (du = tangent, dv) per face, used for AO sampling.
    public static readonly Vector3I[] Du =
    {
        new(0, 1, 0), new(0, 0, 1), new(0, 0, 1), new(1, 0, 0), new(1, 0, 0), new(0, 1, 0),
    };

    public static readonly Vector3I[] Dv =
    {
        new(0, 0, 1), new(0, 1, 0), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0), new(1, 0, 0),
    };

    public static readonly Vector2[] Uvs =
    {
        new(0, 0), new(1, 0), new(1, 1), new(0, 1),
    };

    // Godot treats clockwise screen-space winding as front-facing; the corner
    // order above is math-CCW, so emit reversed to make outward faces visible.
    public static readonly int[] TriOrder = { 0, 2, 1, 0, 3, 2 };
}
