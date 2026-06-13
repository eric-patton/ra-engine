using System;
using System.Collections.Generic;
using Godot;

namespace RAEngine.Tools;

/// <summary>
/// Procedurally generates the stylized PBR block-texture library and writes it
/// to <c>assets/textures/blocks/&lt;name&gt;/</c> as plain PNG files the user can
/// open and replace at any time:
///   albedo.png    (sRGB color)
///   normal.png    (tangent-space normal map, derived from a height field)
///   roughness.png (grayscale, linear)
///   metallic.png  (grayscale, only for metals)
///   emission.png  (sRGB color, only for glowing blocks)
///
/// All noise is seamlessly tileable (periodic value-noise lattice) so block
/// faces repeat without visible seams. Everything is deterministic: regenerating
/// produces identical files, so re-running never disturbs textures the user kept.
///
/// Run headless with:  Godot ... -- --gen-textures
/// </summary>
public static class TextureForge
{
    public const int Size = 64;
    public const string OutRoot = "res://assets/textures/blocks";

    // ---- public entry -----------------------------------------------------

    public static void GenerateAll()
    {
        var recipes = BuildRecipes();
        GD.Print($"[Forge] Generating {recipes.Count} block textures at {Size}x{Size} -> {OutRoot}");
        int ok = 0;
        foreach (var r in recipes)
        {
            try
            {
                var set = r.Build(Size);
                Save(r.Name, set, Size);
                ok++;
                GD.Print($"[Forge]   + {r.Name}");
            }
            catch (Exception e)
            {
                GD.PushError($"[Forge] FAILED {r.Name}: {e.Message}\n{e.StackTrace}");
            }
        }
        GD.Print($"[Forge] Done: {ok}/{recipes.Count} texture sets written.");
    }

    // ---- recipe table -----------------------------------------------------

    private sealed class Recipe
    {
        public string Name;
        public Func<int, TexSet> Build;
        public Recipe(string name, Func<int, TexSet> build) { Name = name; Build = build; }
    }

    private static List<Recipe> BuildRecipes()
    {
        return new List<Recipe>
        {
            // --- natural terrain ---
            new("grass_top",   s => GrassTop(s, seed: 11)),
            new("grass_side",  s => GrassSide(s, seed: 12)),
            new("dirt",        s => Speckle(s, C("#6b4a2b"), C("#54381f"), C("#7d5836"), grain:0.55f, rough:0.95f, roughVar:0.05f, relief:0.35f, seed:21)),
            new("stone",       s => Speckle(s, C("#8a8a8f"), C("#6f6f76"), C("#9a9aa0"), grain:0.4f,  rough:0.85f, roughVar:0.1f,  relief:0.45f, seed:22)),
            new("cobblestone", s => Cobble(s, seed: 23)),
            new("sand",        s => Speckle(s, C("#e3d29c"), C("#d3bf86"), C("#efe1b4"), grain:0.7f,  rough:0.9f,  roughVar:0.06f, relief:0.18f, seed:24)),
            new("sandstone",   s => Layered(s, C("#dcc78f"), C("#cdb579"), C("#e8d8a6"), bands:6, rough:0.8f, relief:0.4f, seed:25)),
            new("gravel",      s => Speckle(s, C("#7c7770"), C("#5d5851"), C("#928c84"), grain:0.85f, rough:0.95f, roughVar:0.08f, relief:0.5f,  seed:26)),
            new("clay",        s => Speckle(s, C("#a7a3b0"), C("#928e9c"), C("#b8b4c2"), grain:0.25f, rough:0.7f,  roughVar:0.05f, relief:0.15f, seed:27)),
            new("snow",        s => Speckle(s, C("#f4f6fb"), C("#e4e9f4"), C("#ffffff"), grain:0.5f,  rough:0.6f,  roughVar:0.1f,  relief:0.2f,  seed:28)),
            new("water",       s => Water(s, seed: 29)),

            // --- wood & plants ---
            new("log_side",    s => LogSide(s, seed: 31)),
            new("log_top",     s => LogTop(s, seed: 32)),
            new("planks",      s => Planks(s, C("#9c6f3a"), C("#7d5526"), planks:4, seed:33)),
            new("leaves",      s => Leaves(s, C("#5b8b3a"), C("#3f6b27"), C("#76a64b"), seed:34)),
            new("olive_leaves",s => Leaves(s, C("#6b7d4a"), C("#505f34"), C("#8a9c63"), seed:35)),

            // --- building / biblical ---
            new("mud_brick",   s => Bricks(s, C("#b58a5a"), C("#caa279"), C("#8f6b41"), cols:3, rows:4, mortar:0.12f, rough:0.92f, seed:41)),
            new("stone_brick", s => Bricks(s, C("#8c8c92"), C("#9b9ba1"), C("#6c6c72"), cols:3, rows:6, mortar:0.10f, rough:0.85f, seed:42)),
            new("brick",       s => Bricks(s, C("#a14b39"), C("#b85c47"), C("#d8cdbb"), cols:4, rows:8, mortar:0.16f, rough:0.88f, seed:43)),
            new("plaster",     s => Speckle(s, C("#e7ddc8"), C("#d8cdb4"), C("#f3ecdb"), grain:0.18f, rough:0.78f, roughVar:0.06f, relief:0.12f, seed:44)),
            new("thatch",      s => Thatch(s, seed: 45)),
            new("cloth_red",   s => Cloth(s, C("#a83a32"), C("#d65a4e"), seed:46)),
            new("cloth_blue",  s => Cloth(s, C("#36567f"), C("#5277a8"), seed:47)),
            new("cloth_cream", s => Cloth(s, C("#cab184"), C("#e3cfa6"), seed:48)),

            // --- metals ---
            new("gold_block",  s => Metal(s, C("#e8c14a"), C("#fff0a8"), roughBase:0.28f, seed:51)),
            new("bronze_block",s => Metal(s, C("#a9712f"), C("#d39a4e"), roughBase:0.42f, seed:52)),

            // --- emissive / special ---
            new("lamp",        s => Glow(s, C("#caa14f"), C("#ffdf9b"), C("#ffcf6e"), seed:61)),
            new("altar_fire",  s => Glow(s, C("#7a2a12"), C("#ff7a2a"), C("#ffd24a"), seed:62, hot:true)),
        };
    }

    // ---- shared data holder ----------------------------------------------

    private sealed class TexSet
    {
        public Color[] Albedo;       // size*size, sRGB
        public float[] Height;       // size*size, 0..1
        public float[] Rough;        // size*size, 0..1
        public float[] Metal;        // optional
        public Color[] Emission;     // optional, sRGB
        public bool[] AlphaMask;     // optional cutout (true = opaque)
        public float NormalStrength = 2.0f;
    }

    // ---- material builders -----------------------------------------------

    private static TexSet GrassTop(int s, int seed)
    {
        var t = NewSet(s);
        Color dark = C("#3f6b27"), mid = C("#4f8030"), light = C("#6fa845"), dry = C("#8a9c43");
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            float n = Fbm(u, v, 6, 4, seed);
            float blades = Fbm(u * 1.0f, v * 1.0f, 16, 2, seed + 7);
            Color col = LerpCol(dark, mid, Smooth(n));
            col = LerpCol(col, light, Math.Max(0, blades - 0.55f) * 1.6f);
            col = LerpCol(col, dry, Math.Max(0, Fbm(u, v, 3, 2, seed + 9) - 0.7f) * 1.2f);
            int i = y * s + x;
            t.Albedo[i] = col;
            t.Height[i] = 0.4f + blades * 0.6f;
            t.Rough[i] = 0.85f + n * 0.1f;
        }
        t.NormalStrength = 1.4f;
        return t;
    }

    private static TexSet GrassSide(int s, int seed)
    {
        // dirt base with a grassy fringe spilling over the top edge
        var t = Speckle(s, C("#6b4a2b"), C("#54381f"), C("#7d5836"), grain: 0.55f, rough: 0.95f, roughVar: 0.05f, relief: 0.35f, seed: seed);
        Color gdark = C("#3f6b27"), gmid = C("#4f8030"), glight = C("#6fa845");
        for (int y = 0; y < s; y++)
        {
            // grass occupies the top ~35% with an irregular lower border
            float v = y / (float)s;
            for (int x = 0; x < s; x++)
            {
                float u = x / (float)s;
                float border = 0.32f + Fbm(u, 0.5f, 16, 2, seed + 3) * 0.14f;
                if (v < border)
                {
                    float n = Fbm(u, v, 12, 3, seed + 5);
                    Color g = LerpCol(gdark, gmid, Smooth(n));
                    g = LerpCol(g, glight, Math.Max(0, n - 0.6f) * 1.8f);
                    int i = y * s + x;
                    t.Albedo[i] = g;
                    t.Height[i] = 0.55f + n * 0.45f;
                    t.Rough[i] = 0.85f + n * 0.1f;
                }
            }
        }
        return t;
    }

    /// <summary>Generic granular material: layered fbm between three palette colors.</summary>
    private static TexSet Speckle(int s, Color baseC, Color darkC, Color lightC,
        float grain, float rough, float roughVar, float relief, int seed)
    {
        var t = NewSet(s);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            float n = Fbm(u, v, 8, 4, seed);
            float fine = Fbm(u, v, 24, 2, seed + 13);
            float mix = Clamp01(Smooth(n) * (1 - grain) + fine * grain);
            Color col = LerpCol(darkC, baseC, Clamp01(mix * 1.5f));
            col = LerpCol(col, lightC, Math.Max(0, fine - 0.62f) * 1.7f);
            int i = y * s + x;
            t.Albedo[i] = col;
            t.Height[i] = Clamp01(0.5f + (mix - 0.5f) * relief * 2f);
            t.Rough[i] = Clamp01(rough + (fine - 0.5f) * roughVar * 2f);
        }
        t.NormalStrength = 1.0f + relief * 2.5f;
        return t;
    }

    /// <summary>Sedimentary horizontal bands (sandstone).</summary>
    private static TexSet Layered(int s, Color baseC, Color darkC, Color lightC, int bands, float rough, float relief, int seed)
    {
        var t = NewSet(s);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            float wobble = (Fbm(u, v, 8, 3, seed) - 0.5f) * 0.08f;
            float band = (v + wobble) * bands;
            float bf = band - (float)Math.Floor(band);
            float shade = 0.5f + (float)Math.Sin(bf * Math.PI) * 0.5f;
            float n = Fbm(u, v, 20, 2, seed + 5);
            Color col = LerpCol(darkC, baseC, shade);
            col = LerpCol(col, lightC, Math.Max(0, n - 0.6f) * 1.4f);
            int i = y * s + x;
            t.Albedo[i] = col;
            t.Height[i] = Clamp01(0.4f + shade * relief);
            t.Rough[i] = rough + (n - 0.5f) * 0.08f;
        }
        t.NormalStrength = 1.6f;
        return t;
    }

    private static TexSet Cobble(int s, int seed)
    {
        // Voronoi-ish cobbles via jittered cell points; mortar in the gaps.
        var t = NewSet(s);
        Color stone = C("#8a8a8f"), stoneD = C("#6c6c72"), stoneL = C("#a3a3a9"), mortar = C("#4c4a48");
        int cells = 4;
        var pts = new Vector2[cells * cells];
        var tint = new float[cells * cells];
        for (int cy = 0; cy < cells; cy++)
        for (int cx = 0; cx < cells; cx++)
        {
            int ci = cy * cells + cx;
            pts[ci] = new Vector2((cx + 0.5f + (Hash2(cx, cy, seed) - 0.5f) * 0.7f) / cells,
                                  (cy + 0.5f + (Hash2(cx, cy, seed + 1) - 0.5f) * 0.7f) / cells);
            tint[ci] = Hash2(cx, cy, seed + 2);
        }
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            float d0 = 9, d1 = 9; int best = 0;
            // Each cell once; toroidal distance handles wrapping (keeps it seamless).
            for (int cy = 0; cy < cells; cy++)
            for (int cx = 0; cx < cells; cx++)
            {
                int ci = cy * cells + cx;
                float dx = Math.Abs(u - pts[ci].X); dx = Math.Min(dx, 1 - dx);
                float dy = Math.Abs(v - pts[ci].Y); dy = Math.Min(dy, 1 - dy);
                float d = dx * dx + dy * dy;
                if (d < d0) { d1 = d0; d0 = d; best = ci; }
                else if (d < d1) { d1 = d; }
            }
            float edge = (float)(Math.Sqrt(d1) - Math.Sqrt(d0));
            float mortarMask = Smooth(Clamp01(edge / 0.05f)); // 0 in gaps, 1 inside stone
            float n = Fbm(u, v, 24, 2, seed + 9);
            Color col = LerpCol(stoneD, stone, tint[best]);
            col = LerpCol(col, stoneL, Math.Max(0, n - 0.6f) * 1.2f);
            col = LerpCol(mortar, col, mortarMask);
            int i = y * s + x;
            t.Albedo[i] = col;
            t.Height[i] = 0.2f + mortarMask * 0.8f;
            t.Rough[i] = 0.8f + (1 - mortarMask) * 0.15f;
        }
        t.NormalStrength = 2.6f;
        return t;
    }

    private static TexSet Bricks(int s, Color brickA, Color brickB, Color mortarColor,
        int cols, int rows, float mortar, float rough, int seed)
    {
        var t = NewSet(s);
        float bw = 1f / cols, bh = 1f / rows;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            int row = (int)(v / bh);
            float rowOffset = (row % 2 == 0) ? 0f : 0.5f * bw;
            float bu = ((u + rowOffset) % bw) / bw;   // 0..1 within brick
            float bv = (v % bh) / bh;
            int brickId = row * 31 + (int)((u + rowOffset) / bw) * 7;
            float m = mortar;
            bool isMortar = bu < m || bu > 1 - m || bv < m * (bw / bh) || bv > 1 - m * (bw / bh);
            float n = Fbm(u, v, 20, 2, seed + brickId);
            float tintv = Hash2(brickId, row, seed);
            Color col = LerpCol(brickA, brickB, tintv);
            col = LerpCol(col, LerpCol(col, Colors.Black, 0.25f), Math.Max(0, 0.5f - n) * 0.6f);
            int i = y * s + x;
            if (isMortar)
            {
                t.Albedo[i] = LerpCol(mortarColor, LerpCol(mortarColor, Colors.Black, 0.2f), n);
                t.Height[i] = 0.15f + n * 0.1f;
                t.Rough[i] = Clamp01(rough + 0.06f);
            }
            else
            {
                t.Albedo[i] = col;
                t.Height[i] = 0.85f + n * 0.1f;
                t.Rough[i] = Clamp01(rough + (n - 0.5f) * 0.1f);
            }
        }
        t.NormalStrength = 1.6f;
        return t;
    }

    private static TexSet Planks(int s, Color woodA, Color woodB, int planks, int seed)
    {
        var t = NewSet(s);
        float pw = 1f / planks;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            int plank = (int)(u / pw);
            float pu = (u % pw) / pw;
            float grain = Fbm(v * 4f, u * 0.5f, 12, 3, seed + plank * 13);
            float lines = (float)Math.Abs(Math.Sin((v * 18f + grain * 3f) * Math.PI));
            Color col = LerpCol(woodB, woodA, Hash2(plank, 0, seed) * 0.5f + 0.4f);
            col = LerpCol(col, LerpCol(col, Colors.Black, 0.35f), Math.Max(0, lines - 0.7f) * 0.8f);
            bool gap = pu < 0.04f || pu > 0.96f;
            int i = y * s + x;
            if (gap)
            {
                t.Albedo[i] = LerpCol(col, Colors.Black, 0.5f);
                t.Height[i] = 0.2f;
                t.Rough[i] = 0.95f;
            }
            else
            {
                t.Albedo[i] = col;
                t.Height[i] = 0.6f + grain * 0.3f - Math.Max(0, lines - 0.8f) * 0.3f;
                t.Rough[i] = 0.8f + grain * 0.1f;
            }
        }
        t.NormalStrength = 1.8f;
        return t;
    }

    private static TexSet LogSide(int s, int seed)
    {
        var t = NewSet(s);
        Color bark = C("#5a4023"), barkD = C("#3f2c16"), barkL = C("#6e5230");
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            float ridge = Fbm(u * 6f, v * 0.6f, 12, 3, seed);
            float vlines = (float)Math.Abs(Math.Sin((u * 10f + ridge * 2f) * Math.PI));
            Color col = LerpCol(barkD, bark, Smooth(ridge));
            col = LerpCol(col, barkL, Math.Max(0, vlines - 0.6f) * 0.8f);
            int i = y * s + x;
            t.Albedo[i] = col;
            t.Height[i] = 0.4f + vlines * 0.6f;
            t.Rough[i] = 0.95f;
        }
        t.NormalStrength = 2.6f;
        return t;
    }

    private static TexSet LogTop(int s, int seed)
    {
        var t = NewSet(s);
        Color wood = C("#b08a46"), woodD = C("#8a6a36"), core = C("#9c7a40");
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s - 0.5f, v = y / (float)s - 0.5f;
            float r = (float)Math.Sqrt(u * u + v * v) * 2f;
            float rings = (float)Math.Abs(Math.Sin(r * 22f + Fbm(x / (float)s, y / (float)s, 8, 2, seed) * 2f));
            Color col = LerpCol(wood, woodD, Math.Max(0, rings - 0.5f));
            col = LerpCol(col, core, Math.Max(0, 0.15f - r) * 4f);
            int i = y * s + x;
            t.Albedo[i] = col;
            t.Height[i] = 0.5f + rings * 0.2f;
            t.Rough[i] = 0.85f;
        }
        t.NormalStrength = 1.2f;
        return t;
    }

    private static TexSet Leaves(int s, Color baseC, Color darkC, Color lightC, int seed)
    {
        var t = NewSet(s);
        t.AlphaMask = new bool[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            float clump = Fbm(u, v, 10, 3, seed);
            float detail = Fbm(u, v, 28, 2, seed + 4);
            Color col = LerpCol(darkC, baseC, Smooth(clump));
            col = LerpCol(col, lightC, Math.Max(0, detail - 0.62f) * 1.8f);
            int i = y * s + x;
            t.Albedo[i] = col;
            t.Height[i] = 0.3f + clump * 0.7f;
            t.Rough[i] = 0.9f;
            // small holes for a leafy silhouette
            t.AlphaMask[i] = detail > 0.22f;
        }
        t.NormalStrength = 1.6f;
        return t;
    }

    private static TexSet Thatch(int s, int seed)
    {
        var t = NewSet(s);
        Color straw = C("#c9a24e"), strawD = C("#9c7a32"), strawL = C("#e3c46e");
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            float strand = Fbm(u * 2f, v * 14f, 16, 2, seed);
            float lines = (float)Math.Abs(Math.Sin((v * 22f + strand * 3f) * Math.PI));
            Color col = LerpCol(strawD, straw, Smooth(strand));
            col = LerpCol(col, strawL, Math.Max(0, lines - 0.55f) * 1.2f);
            int i = y * s + x;
            t.Albedo[i] = col;
            t.Height[i] = 0.3f + lines * 0.7f;
            t.Rough[i] = 0.97f;
        }
        t.NormalStrength = 2.2f;
        return t;
    }

    private static TexSet Cloth(int s, Color baseC, Color lightC, int seed)
    {
        var t = NewSet(s);
        Color dark = LerpCol(baseC, Colors.Black, 0.25f);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            // woven weave: interleaved warp/weft
            float warp = (float)Math.Sin(u * s * Math.PI);
            float weft = (float)Math.Sin(v * s * Math.PI);
            float weave = (warp * weft) * 0.5f + 0.5f;
            float stripe = ((int)(v * 8) % 4 == 0) ? 0.85f : 1f; // subtle banded pattern
            float n = Fbm(u, v, 16, 2, seed);
            Color col = LerpCol(dark, baseC, weave);
            col = LerpCol(col, lightC, Math.Max(0, n - 0.6f) * 0.8f);
            col = LerpCol(col, dark, (1 - stripe));
            int i = y * s + x;
            t.Albedo[i] = col;
            t.Height[i] = 0.4f + weave * 0.5f;
            t.Rough[i] = 0.85f + n * 0.1f;
        }
        t.NormalStrength = 1.0f;
        return t;
    }

    private static TexSet Metal(int s, Color baseC, Color sheenC, float roughBase, int seed)
    {
        var t = NewSet(s);
        t.Metal = new float[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            float n = Fbm(u, v, 14, 3, seed);
            float scratch = Fbm(u * 8f, v * 0.5f, 20, 2, seed + 9);
            Color col = LerpCol(baseC, sheenC, Math.Max(0, n - 0.55f) * 1.6f);
            col = LerpCol(col, LerpCol(baseC, Colors.Black, 0.3f), Math.Max(0, 0.45f - n) * 0.8f);
            int i = y * s + x;
            t.Albedo[i] = col;
            t.Height[i] = 0.5f + (n - 0.5f) * 0.3f;
            t.Rough[i] = Clamp01(roughBase + (scratch - 0.5f) * 0.18f);
            t.Metal[i] = 1.0f;
        }
        t.NormalStrength = 0.6f;
        return t;
    }

    private static TexSet Glow(int s, Color baseC, Color midC, Color hotC, int seed, bool hot = false)
    {
        var t = NewSet(s);
        t.Emission = new Color[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            float n = Fbm(u, v, 10, 3, seed);
            float core = hot ? Fbm(u, v * 0.7f, 8, 3, seed + 5) : n;
            Color col = LerpCol(baseC, midC, Smooth(core));
            col = LerpCol(col, hotC, Math.Max(0, core - 0.55f) * 1.8f);
            int i = y * s + x;
            t.Albedo[i] = col;
            t.Height[i] = 0.5f + (n - 0.5f) * 0.2f;
            t.Rough[i] = 0.6f;
            float e = hot ? Clamp01(core * core * 1.4f) : Clamp01((core - 0.3f) * 1.5f);
            t.Emission[i] = LerpCol(midC, hotC, e) * (hot ? 1.4f : 0.9f);
        }
        t.NormalStrength = 0.8f;
        return t;
    }

    private static TexSet Water(int s, int seed)
    {
        var t = NewSet(s);
        Color deep = C("#1f5d86"), shallow = C("#3f86b0"), foam = C("#bfe3f0");
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float u = x / (float)s, v = y / (float)s;
            float n = Fbm(u, v, 8, 3, seed);
            Color col = LerpCol(deep, shallow, Smooth(n));
            col = LerpCol(col, foam, Math.Max(0, n - 0.78f) * 1.5f);
            int i = y * s + x;
            t.Albedo[i] = col;
            t.Height[i] = 0.5f + (n - 0.5f) * 0.4f;
            t.Rough[i] = 0.08f + n * 0.05f;
        }
        t.NormalStrength = 0.7f;
        return t;
    }

    // ---- save / encode ----------------------------------------------------

    private static void Save(string name, TexSet t, int s)
    {
        string dir = $"{OutRoot}/{name}";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));

        var albedo = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            int i = y * s + x;
            Color c = t.Albedo[i];
            c.A = (t.AlphaMask != null && !t.AlphaMask[i]) ? 0f : 1f;
            albedo.SetPixel(x, y, c);
        }
        albedo.SavePng($"{dir}/albedo.png");

        NormalFromHeight(BlurHeight(t.Height, s, 1), s, t.NormalStrength).SavePng($"{dir}/normal.png");
        GrayPng(t.Rough, s).SavePng($"{dir}/roughness.png");

        if (t.Metal != null)
            GrayPng(t.Metal, s).SavePng($"{dir}/metallic.png");

        if (t.Emission != null)
        {
            var em = Image.CreateEmpty(s, s, false, Image.Format.Rgb8);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                em.SetPixel(x, y, Clamp(t.Emission[y * s + x]));
            em.SavePng($"{dir}/emission.png");
        }
    }

    // Global damping so derived normals stay subtle (avoids over-saturated maps).
    private const float NormalScale = 0.5f;

    private static Image NormalFromHeight(float[] h, int s, float strength)
    {
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgb8);
        float k = strength * NormalScale;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float hl = h[Idx(x - 1, y, s)], hr = h[Idx(x + 1, y, s)];
            float hd = h[Idx(x, y - 1, s)], hu = h[Idx(x, y + 1, s)];
            var n = new Vector3(-(hr - hl) * k, -(hu - hd) * k, 1f).Normalized();
            img.SetPixel(x, y, new Color(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f));
        }
        return img;
    }

    private static float[] BlurHeight(float[] src, int s, int passes)
    {
        var a = (float[])src.Clone();
        var b = new float[s * s];
        for (int p = 0; p < passes; p++)
        {
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float sum = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    sum += a[Idx(x + dx, y + dy, s)];
                b[y * s + x] = sum / 9f;
            }
            (a, b) = (b, a);
        }
        return a;
    }

    private static Image GrayPng(float[] v, int s)
    {
        var img = Image.CreateEmpty(s, s, false, Image.Format.L8);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float g = Clamp01(v[y * s + x]);
            img.SetPixel(x, y, new Color(g, g, g));
        }
        return img;
    }

    // ---- noise & helpers --------------------------------------------------

    private static TexSet NewSet(int s) => new TexSet
    {
        Albedo = new Color[s * s],
        Height = new float[s * s],
        Rough = new float[s * s],
    };

    private static int Idx(int x, int y, int s)
    {
        x = ((x % s) + s) % s;
        y = ((y % s) + s) % s;
        return y * s + x;
    }

    private static float Hash2(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 362437);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private static float TileValue(float u, float v, int cells, int seed)
    {
        float x = u * cells, y = v * cells;
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        float fx = x - x0, fy = y - y0;
        int x0m = ((x0 % cells) + cells) % cells, y0m = ((y0 % cells) + cells) % cells;
        int x1m = (x0m + 1) % cells, y1m = (y0m + 1) % cells;
        float a = Hash2(x0m, y0m, seed), b = Hash2(x1m, y0m, seed);
        float c = Hash2(x0m, y1m, seed), d = Hash2(x1m, y1m, seed);
        float sx = Smooth(fx), sy = Smooth(fy);
        return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy;
    }

    private static float Fbm(float u, float v, int baseCells, int octaves, int seed)
    {
        float sum = 0, amp = 0.5f, norm = 0; int cells = baseCells;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * TileValue(u, v, cells, seed + o * 101);
            norm += amp; amp *= 0.5f; cells *= 2;
        }
        return sum / norm;
    }

    private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private static Color Clamp(Color c) =>
        new Color(Clamp01(c.R), Clamp01(c.G), Clamp01(c.B), 1f);

    private static Color LerpCol(Color a, Color b, float t)
    {
        t = Clamp01(t);
        return new Color(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, 1f);
    }

    private static Color C(string hex) => new Color(hex);
}
