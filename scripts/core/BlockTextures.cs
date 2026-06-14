using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>Loads the per-block PNGs into Texture2DArrays and builds the shared
/// voxel ShaderMaterial. Roughness/metallic/AO are packed into a single ORM
/// array (R=AO, G=Roughness, B=Metallic) at load time, while the on-disk files
/// stay as separate, human-editable PNGs.</summary>
public sealed class BlockTextures
{
    public const int TexSize = 64;
    private const string Root = "res://assets/textures/blocks";

    public Texture2DArray Albedo;
    public Texture2DArray Normal;
    public Texture2DArray Orm;
    public Texture2DArray Emission;
    public ShaderMaterial Material;
    public int LayerCount { get; private set; }

    private readonly Dictionary<string, int> _layerOf = new();

    public static BlockTextures Build()
    {
        BlockRegistry.EnsureInit();
        var bt = new BlockTextures();
        bt.Load();
        return bt;
    }

    private void Load()
    {
        // 1. Collect the unique set of texture names referenced by any block face.
        var names = new List<string>();
        var seen = new HashSet<string>();
        foreach (var block in BlockRegistry.All)
        foreach (var t in block.FaceTex)
            if (!string.IsNullOrEmpty(t) && seen.Add(t))
                names.Add(t);
        names.Sort(); // deterministic layer order

        var albedos = new Godot.Collections.Array<Image>();
        var normals = new Godot.Collections.Array<Image>();
        var orms = new Godot.Collections.Array<Image>();
        var emissions = new Godot.Collections.Array<Image>();

        for (int i = 0; i < names.Count; i++)
        {
            string n = names[i];
            _layerOf[n] = i;

            albedos.Add(WithMips(LoadOr($"{Root}/{n}/albedo.png", Image.Format.Rgba8, new Color(0.8f, 0.2f, 0.8f))));
            normals.Add(WithMips(LoadOr($"{Root}/{n}/normal.png", Image.Format.Rgb8, new Color(0.5f, 0.5f, 1f))));
            orms.Add(WithMips(BuildOrm(n)));
            emissions.Add(WithMips(LoadOr($"{Root}/{n}/emission.png", Image.Format.Rgb8, Colors.Black)));
        }

        LayerCount = names.Count;
        Albedo = MakeArray(albedos);
        Normal = MakeArray(normals);
        Orm = MakeArray(orms);
        Emission = MakeArray(emissions);

        // 2. Resolve each block's per-face layer index.
        foreach (var block in BlockRegistry.All)
        for (int f = 0; f < 6; f++)
        {
            string t = block.FaceTex[f];
            block.FaceLayer[f] = (t != null && _layerOf.TryGetValue(t, out int l)) ? l : 0;
        }

        // 3. Build the shared material.
        var shader = GD.Load<Shader>("res://assets/shaders/voxel.gdshader");
        Material = new ShaderMaterial { Shader = shader };
        Material.SetShaderParameter("albedo_tex", Albedo);
        Material.SetShaderParameter("normal_tex", Normal);
        Material.SetShaderParameter("orm_tex", Orm);
        Material.SetShaderParameter("emission_tex", Emission);

        GD.Print($"[Blocks] Built texture arrays: {LayerCount} layers @ {TexSize}px.");
    }

    public int LayerFor(string name) => _layerOf.TryGetValue(name, out int l) ? l : 0;

    private readonly Dictionary<ushort, Color> _avgColor = new();

    /// <summary>The average albedo colour of a block's representative face — used to
    /// tint break/place particles so dirt throws brown crumbs, leaves green flecks,
    /// stone grey chips. Cached per block id (reuses the UI icon image).</summary>
    public Color AverageColor(BlockType b)
    {
        if (_avgColor.TryGetValue(b.Id, out var cached)) return cached;
        var avg = new Color(0.7f, 0.7f, 0.7f);
        Image img = GetIcon(b)?.GetImage();
        if (img != null)
        {
            if (img.IsCompressed()) img.Decompress();
            int w = img.GetWidth(), h = img.GetHeight();
            int sx = Mathf.Max(1, w / 8), sy = Mathf.Max(1, h / 8);
            double r = 0, g = 0, bl = 0; int n = 0;
            for (int y = 0; y < h; y += sy)
            for (int x = 0; x < w; x += sx)
            {
                Color px = img.GetPixel(x, y);
                r += px.R; g += px.G; bl += px.B; n++;
            }
            if (n > 0) avg = new Color((float)(r / n), (float)(g / n), (float)(bl / n));
        }
        _avgColor[b.Id] = avg;
        return avg;
    }

    private readonly Dictionary<ushort, Texture2D> _icons = new();

    /// <summary>A small Texture2D for UI (hotbar/inventory), from the block's
    /// representative face albedo. Cached per block id.</summary>
    public Texture2D GetIcon(BlockType b)
    {
        if (_icons.TryGetValue(b.Id, out var cached)) return cached;
        string name = b.FaceTex[(int)Face.PosZ] ?? b.FaceTex[(int)Face.PosY];
        Texture2D tex = null;
        if (!string.IsNullOrEmpty(name))
        {
            var img = LoadPng($"{Root}/{name}/albedo.png");
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        _icons[b.Id] = tex;
        return tex;
    }

    // ---- helpers ----------------------------------------------------------

    private static Texture2DArray MakeArray(Godot.Collections.Array<Image> imgs)
    {
        var arr = new Texture2DArray();
        arr.CreateFromImages(imgs);
        return arr;
    }

    private static Image WithMips(Image img)
    {
        img.GenerateMipmaps();
        return img;
    }

    /// <summary>Decode a PNG straight from bytes. Avoids the engine's
    /// "loaded as image file" export warning and ignores import settings.</summary>
    private static Image LoadPng(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        byte[] bytes = FileAccess.GetFileAsBytes(path);
        if (bytes == null || bytes.Length == 0) return null;
        var img = new Image();
        return img.LoadPngFromBuffer(bytes) == Error.Ok ? img : null;
    }

    private static Image LoadOr(string path, Image.Format fmt, Color fallback)
    {
        Image img = LoadPng(path);
        if (img == null)
        {
            img = Image.CreateEmpty(TexSize, TexSize, false, fmt);
            img.Fill(fallback);
            return img;
        }
        if (img.GetWidth() != TexSize || img.GetHeight() != TexSize)
            img.Resize(TexSize, TexSize, Image.Interpolation.Lanczos);
        if (img.GetFormat() != fmt)
            img.Convert(fmt);
        return img;
    }

    /// <summary>Compose ORM: R=AO (ao.png or 1), G=Roughness (roughness.png or
    /// 0.85), B=Metallic (metallic.png or 0).</summary>
    private Image BuildOrm(string name)
    {
        Image ao = LoadGray($"{Root}/{name}/ao.png", 1f);
        Image rough = LoadGray($"{Root}/{name}/roughness.png", 0.85f);
        Image metal = LoadGray($"{Root}/{name}/metallic.png", 0f);

        var orm = Image.CreateEmpty(TexSize, TexSize, false, Image.Format.Rgb8);
        for (int y = 0; y < TexSize; y++)
        for (int x = 0; x < TexSize; x++)
            orm.SetPixel(x, y, new Color(ao.GetPixel(x, y).R, rough.GetPixel(x, y).R, metal.GetPixel(x, y).R));
        return orm;
    }

    private static Image LoadGray(string path, float fallback)
    {
        Image img = LoadPng(path);
        if (img == null)
        {
            img = Image.CreateEmpty(TexSize, TexSize, false, Image.Format.L8);
            img.Fill(new Color(fallback, fallback, fallback));
            return img;
        }
        if (img.GetWidth() != TexSize || img.GetHeight() != TexSize)
            img.Resize(TexSize, TexSize, Image.Interpolation.Lanczos);
        return img;
    }
}
