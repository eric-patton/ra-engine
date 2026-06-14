using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>Central catalogue of block types. Air is always id 0.
/// Definitions live in code for now; the layout is data-friendly so a JSON
/// loader can be added later without touching call sites.</summary>
public static class BlockRegistry
{
    private static readonly List<BlockType> ById = new();
    private static readonly Dictionary<string, BlockType> ByName = new();
    private static bool _init;

    public static IReadOnlyList<BlockType> All => ById;
    public static int Count => ById.Count;

    public static BlockType Air { get; private set; }

    public static void EnsureInit()
    {
        if (_init) return;
        _init = true;
        Define();
    }

    public static BlockType Get(ushort id) => id < ById.Count ? ById[id] : Air;
    public static BlockType Get(string name) => ByName.TryGetValue(name, out var b) ? b : Air;
    public static bool TryId(string name, out ushort id)
    {
        if (ByName.TryGetValue(name, out var b)) { id = b.Id; return true; }
        id = 0; return false;
    }
    public static ushort IdOf(string name) => ByName.TryGetValue(name, out var b) ? b.Id : (ushort)0;

    private static BlockType Add(string name, string display)
    {
        var b = new BlockType { Id = (ushort)ById.Count, Name = name, DisplayName = display };
        ById.Add(b);
        ByName[name] = b;
        return b;
    }

    private static void Define()
    {
        // id 0 — air
        var air = Add("air", "Air");
        air.Render = RenderType.None;
        air.Solid = false;
        air.Opaque = false;
        Air = air;

        // natural terrain
        Add("grass", "Grass").SetFaces(top: "grass_top", bottom: "dirt", side: "grass_side")
            .SpawnsVegetation = true;
        Add("dirt", "Dirt").SetFaces("dirt");
        Add("stone", "Stone").SetFaces("stone");
        Add("cobblestone", "Cobblestone").SetFaces("cobblestone");
        Add("sand", "Sand").SetFaces("sand");
        Add("sandstone", "Sandstone").SetFaces("sandstone");
        Add("gravel", "Gravel").SetFaces("gravel");
        Add("clay", "Clay").SetFaces("clay");
        Add("snow", "Snow").SetFaces("snow");

        // underground ores & deposits (placed by the terrain generator at depth)
        Add("coal_ore", "Coal Ore").SetFaces("coal_ore");
        Add("copper_ore", "Copper Ore").SetFaces("copper_ore");
        Add("iron_ore", "Iron Ore").SetFaces("iron_ore");
        Add("gold_ore", "Gold Ore").SetFaces("gold_ore");
        Add("bedrock", "Bedrock").SetFaces("bedrock");

        // water (liquid)
        var water = Add("water", "Water").SetFaces("water");
        water.Render = RenderType.Water;
        water.Solid = false;
        water.Opaque = false;

        // wood & plants
        Add("oak_log", "Oak Log").SetFaces(top: "log_top", bottom: "log_top", side: "log_side");
        Add("planks", "Wood Planks").SetFaces("planks");
        var leaves = Add("leaves", "Leaves").SetFaces("leaves");
        leaves.Opaque = false; // lets light/faces through to neighbours a bit
        var olive = Add("olive_leaves", "Olive Leaves").SetFaces("olive_leaves");
        olive.Opaque = false;

        // building / biblical
        Add("mud_brick", "Mud Brick").SetFaces("mud_brick");
        Add("stone_brick", "Stone Brick").SetFaces("stone_brick");
        Add("brick", "Brick").SetFaces("brick");
        Add("plaster", "Plaster").SetFaces("plaster");
        Add("thatch", "Thatch").SetFaces("thatch");
        Add("cloth_red", "Red Cloth").SetFaces("cloth_red");
        Add("cloth_blue", "Blue Cloth").SetFaces("cloth_blue");
        Add("cloth_cream", "Cream Cloth").SetFaces("cloth_cream");

        // metals
        Add("gold_block", "Gold Block").SetFaces("gold_block");
        Add("bronze_block", "Bronze Block").SetFaces("bronze_block");

        // emissive
        Add("lamp", "Lamp").SetFaces("lamp").Emissive = true;
        var fire = Add("altar_fire", "Altar Fire").SetFaces("altar_fire");
        fire.Emissive = true;
        fire.Hazard = true;
        fire.HazardDamage = 3f;

        // Mining hardness (seconds to break by hand at base speed). Anything not
        // listed keeps the BlockType default (0.6).
        SetHard(0.5f, "grass", "dirt", "sand", "gravel", "snow", "clay");
        SetHard(0.2f, "leaves", "olive_leaves");
        SetHard(0.6f, "thatch", "lamp");
        SetHard(1.6f, "stone", "cobblestone", "sandstone", "coal_ore");
        SetHard(2.0f, "oak_log", "planks", "mud_brick", "stone_brick", "brick", "plaster",
                       "cloth_red", "cloth_blue", "cloth_cream", "copper_ore");
        SetHard(2.8f, "iron_ore", "gold_ore");
        SetHard(3.0f, "gold_block", "bronze_block");
        SetHard(6.0f, "bedrock");

        // Footstep / break / mining sound family. Default is Stone, so only the
        // softer / distinct materials need listing.
        SetMat(MaterialSound.Grass, "grass", "leaves", "olive_leaves");
        SetMat(MaterialSound.Dirt, "dirt", "clay", "mud_brick");
        SetMat(MaterialSound.Sand, "sand", "sandstone", "gravel");
        SetMat(MaterialSound.Wood, "oak_log", "planks", "thatch");
        SetMat(MaterialSound.Snow, "snow");
        SetMat(MaterialSound.Metal, "gold_block", "bronze_block", "lamp");
        SetMat(MaterialSound.Cloth, "cloth_red", "cloth_blue", "cloth_cream");

        GD.Print($"[Blocks] Registered {ById.Count} block types.");
    }

    private static void SetHard(float seconds, params string[] names)
    {
        foreach (var n in names)
            if (ByName.TryGetValue(n, out var b)) b.Hardness = seconds;
    }

    private static void SetMat(MaterialSound mat, params string[] names)
    {
        foreach (var n in names)
            if (ByName.TryGetValue(n, out var b)) b.Material = mat;
    }
}
