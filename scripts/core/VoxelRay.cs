using Godot;

namespace RAEngine.Core;

/// <summary>Amanatides &amp; Woo voxel DDA: steps cell-by-cell along a ray and
/// returns the first selectable block, the empty cell in front of it (for
/// placement), and the hit face normal.</summary>
public static class VoxelRay
{
    public struct Hit
    {
        public bool Ok;
        public Vector3I Block;   // the block that was hit
        public Vector3I Prev;    // empty cell adjacent on the entry face
        public Vector3I Normal;  // face normal pointing back toward the ray origin
        public float Distance;
    }

    public static Hit Cast(VoxelWorld world, Vector3 origin, Vector3 dir, float maxDist = 6f)
    {
        var hit = new Hit();
        dir = dir.Normalized();
        if (dir == Vector3.Zero) return hit;

        int x = Mathf.FloorToInt(origin.X);
        int y = Mathf.FloorToInt(origin.Y);
        int z = Mathf.FloorToInt(origin.Z);

        int stepX = dir.X > 0 ? 1 : (dir.X < 0 ? -1 : 0);
        int stepY = dir.Y > 0 ? 1 : (dir.Y < 0 ? -1 : 0);
        int stepZ = dir.Z > 0 ? 1 : (dir.Z < 0 ? -1 : 0);

        float tMaxX = IntBound(origin.X, dir.X);
        float tMaxY = IntBound(origin.Y, dir.Y);
        float tMaxZ = IntBound(origin.Z, dir.Z);
        float tDeltaX = dir.X != 0 ? Mathf.Abs(1f / dir.X) : float.PositiveInfinity;
        float tDeltaY = dir.Y != 0 ? Mathf.Abs(1f / dir.Y) : float.PositiveInfinity;
        float tDeltaZ = dir.Z != 0 ? Mathf.Abs(1f / dir.Z) : float.PositiveInfinity;

        var normal = Vector3I.Zero;
        float t = 0f;

        for (int i = 0; i < 512; i++)
        {
            var b = world.GetBlock(x, y, z);
            if (!b.IsAir && !b.IsLiquid)
            {
                hit.Ok = true;
                hit.Block = new Vector3I(x, y, z);
                hit.Normal = normal;
                hit.Prev = hit.Block + normal;
                hit.Distance = t;
                return hit;
            }

            if (tMaxX < tMaxY && tMaxX < tMaxZ)
            {
                if (tMaxX > maxDist) break;
                x += stepX; t = tMaxX; tMaxX += tDeltaX; normal = new Vector3I(-stepX, 0, 0);
            }
            else if (tMaxY < tMaxZ)
            {
                if (tMaxY > maxDist) break;
                y += stepY; t = tMaxY; tMaxY += tDeltaY; normal = new Vector3I(0, -stepY, 0);
            }
            else
            {
                if (tMaxZ > maxDist) break;
                z += stepZ; t = tMaxZ; tMaxZ += tDeltaZ; normal = new Vector3I(0, 0, -stepZ);
            }
        }
        return hit;
    }

    private static float IntBound(float s, float ds)
    {
        if (ds == 0) return float.PositiveInfinity;
        if (ds < 0) return IntBound(-s, -ds);
        float frac = s - Mathf.Floor(s);
        return (1f - frac) / ds;
    }
}
