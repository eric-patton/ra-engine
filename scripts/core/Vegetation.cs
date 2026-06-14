using Godot;

namespace RAEngine.Core;

/// <summary>Shared assets for grass-tuft scatter: a procedurally drawn blade
/// texture, a two-quad "cross" mesh, and the swaying material. All cached, so
/// every chunk's MultiMesh reuses one mesh + one material.</summary>
public static class Vegetation
{
    public const float BladeWidth = 0.7f;
    public const float BladeHeight = 0.55f;

    private static Mesh _mesh;
    private static Material _material;

    public static Mesh CrossMesh => _mesh ??= BuildCross();
    public static Material Material => _material ??= BuildMaterial();

    private static Material BuildMaterial()
    {
        var shader = GD.Load<Shader>("res://assets/shaders/vegetation.gdshader");
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("tuft", BuildTuftTexture());
        return mat;
    }

    /// <summary>Two perpendicular quads forming an X, standing on the ground. UVs
    /// run v=0 at the top (matching the image's first row) to v=1 at the base.</summary>
    private static Mesh BuildCross()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float w = BladeWidth * 0.5f, h = BladeHeight;
        AddQuad(st, new Vector3(-w, 0, 0), new Vector3(w, 0, 0), h); // X–Y plane
        AddQuad(st, new Vector3(0, 0, -w), new Vector3(0, 0, w), h); // Z–Y plane
        st.GenerateNormals();
        return st.Commit();
    }

    private static void AddQuad(SurfaceTool st, Vector3 baseA, Vector3 baseB, float h)
    {
        Vector3 topA = baseA + new Vector3(0, h, 0);
        Vector3 topB = baseB + new Vector3(0, h, 0);
        // two triangles, wound so the quad faces both ways (cull_disabled)
        st.SetUV(new Vector2(0, 0)); st.AddVertex(topA);
        st.SetUV(new Vector2(1, 0)); st.AddVertex(topB);
        st.SetUV(new Vector2(1, 1)); st.AddVertex(baseB);

        st.SetUV(new Vector2(0, 0)); st.AddVertex(topA);
        st.SetUV(new Vector2(1, 1)); st.AddVertex(baseB);
        st.SetUV(new Vector2(0, 1)); st.AddVertex(baseA);
    }

    /// <summary>Draw a handful of tapered green blades on a transparent canvas.</summary>
    private static ImageTexture BuildTuftTexture()
    {
        const int W = 48, H = 48;
        var img = Image.CreateEmpty(W, H, true, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));

        uint seed = 1234567u;
        float Rand() { seed = seed * 1664525u + 1013904223u; return (seed >> 8) / 16777216f; }

        int blades = 6;
        for (int b = 0; b < blades; b++)
        {
            float baseX = (b + 0.5f) / blades + (Rand() - 0.5f) * 0.06f;
            float topX = baseX + (Rand() - 0.5f) * 0.28f;
            float height = 0.55f + Rand() * 0.4f; // fraction of H
            var low = new Color(0.16f, 0.42f, 0.12f);
            var high = new Color(0.42f, 0.72f, 0.26f);

            int topRow = (int)((1f - height) * H);
            for (int y = H - 1; y >= topRow; y--)
            {
                float t = (float)(H - 1 - y) / Mathf.Max(1, H - 1 - topRow); // 0 base .. 1 tip
                float cx = Mathf.Lerp(baseX, topX, t) * W;
                float halfW = Mathf.Lerp(1.6f, 0.4f, t); // taper toward the tip
                var col = low.Lerp(high, t);
                int x0 = (int)(cx - halfW), x1 = (int)(cx + halfW);
                for (int x = x0; x <= x1; x++)
                    if (x >= 0 && x < W) img.SetPixel(x, y, col);
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}
