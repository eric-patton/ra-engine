using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>Builds the render mesh(es) and collision shape for a chunk using
/// neighbour-aware face culling and classic per-vertex ambient occlusion.</summary>
public static class ChunkMesher
{
    public struct Result
    {
        public ArrayMesh Opaque;
        public ArrayMesh Water;
        public Shape3D Collision;
    }

    public static Result Build(VoxelWorld world, Chunk chunk)
    {
        var stOpaque = new SurfaceTool();
        var stWater = new SurfaceTool();
        stOpaque.Begin(Mesh.PrimitiveType.Triangles);
        stWater.Begin(Mesh.PrimitiveType.Triangles);
        stOpaque.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbaFloat);
        stWater.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbaFloat);

        int vOpaque = 0, vWater = 0;
        var colVerts = new List<Vector3>();

        Vector3I baseW = chunk.Coord * Chunk.Size;

        for (int y = 0; y < Chunk.Size; y++)
        for (int z = 0; z < Chunk.Size; z++)
        for (int x = 0; x < Chunk.Size; x++)
        {
            ushort id = chunk.GetLocal(x, y, z);
            if (id == 0) continue;
            var self = BlockRegistry.Get(id);

            int wx = baseW.X + x, wy = baseW.Y + y, wz = baseW.Z + z;
            var local = new Vector3(x, y, z);

            for (int f = 0; f < 6; f++)
            {
                Vector3I nrm = VoxelGeometry.Normals[f];
                var nCell = new Vector3I(wx + nrm.X, wy + nrm.Y, wz + nrm.Z);
                ushort nbId = world.GetBlockId(nCell);
                if (!ShouldDraw(self, id, BlockRegistry.Get(nbId), nbId)) continue;

                bool water = self.Render == RenderType.Water;
                SurfaceTool st = water ? stWater : stOpaque;
                int baseIdx = water ? vWater : vOpaque;

                int layer = self.FaceLayer[f];
                Vector3 normal = nrm;
                var tangent = new Plane(VoxelGeometry.Tangents[f], 1f);
                Vector3I du = VoxelGeometry.Du[f], dv = VoxelGeometry.Dv[f];

                for (int ci = 0; ci < 4; ci++)
                {
                    Vector2 uv = VoxelGeometry.Uvs[ci];
                    float ao = water ? 1f : VertexAo(world, nCell, du, dv, uv);
                    st.SetNormal(normal);
                    st.SetTangent(tangent);
                    st.SetUV(uv);
                    st.SetCustom(0, new Color(layer, ao, 0f, 0f));
                    st.AddVertex(local + VoxelGeometry.Corners[f][ci]);
                }
                foreach (int t in VoxelGeometry.TriOrder)
                    st.AddIndex(baseIdx + t);

                if (water) vWater += 4; else vOpaque += 4;

                if (self.Solid)
                    foreach (int t in VoxelGeometry.TriOrder)
                        colVerts.Add(local + VoxelGeometry.Corners[f][t]);
            }
        }

        var res = new Result();
        res.Opaque = vOpaque > 0 ? stOpaque.Commit() : null;
        res.Water = vWater > 0 ? stWater.Commit() : null;
        if (colVerts.Count > 0)
        {
            var shape = new ConcavePolygonShape3D();
            shape.SetFaces(colVerts.ToArray());
            res.Collision = shape;
        }
        return res;
    }

    private static bool ShouldDraw(BlockType self, ushort selfId, BlockType nb, ushort nbId)
    {
        if (nb.IsAir) return true;
        if (nb.Opaque) return false;
        if (nbId == selfId) return false; // same transparent material: hide internal faces
        return true;
    }

    /// <summary>Classic 0..1 ambient occlusion for one face corner from the
    /// three blocks diagonally in front of the face.</summary>
    private static float VertexAo(VoxelWorld world, Vector3I airCell, Vector3I du, Vector3I dv, Vector2 uv)
    {
        int su = uv.X > 0.5f ? 1 : -1;
        int sv = uv.Y > 0.5f ? 1 : -1;
        bool s1 = world.IsOpaque(airCell + du * su);
        bool s2 = world.IsOpaque(airCell + dv * sv);
        bool corner = world.IsOpaque(airCell + du * su + dv * sv);
        int level = (s1 && s2) ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (corner ? 1 : 0));
        return level / 3f;
    }
}
