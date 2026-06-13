using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>An in-memory copy of a cuboid of blocks (a prefab/clipboard).
/// Stores block names so it survives changes to numeric block ids.</summary>
public sealed class Structure
{
    public Vector3I Size;
    public string[] Palette;   // index 0 is always "air"
    public ushort[] Cells;     // palette indices, length Size.X*Y*Z

    public int Index(int x, int y, int z) => (y * Size.Z + z) * Size.X + x;
}

/// <summary>Saves/loads voxel worlds and structures to binary files. Blocks are
/// stored by name (a palette) so files keep working if the registry changes,
/// and chunk data is run-length encoded.</summary>
public static class WorldIO
{
    private const uint WorldMagic = 0x52574C44; // "RWLD"
    private const uint StructMagic = 0x52535452; // "RSTR"
    private const uint Version = 1;

    // ---- worlds -----------------------------------------------------------

    public static bool SaveWorld(VoxelWorld world, string path)
    {
        EnsureDir(path);
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f == null) { GD.PushError($"[WorldIO] cannot write {path}: {FileAccess.GetOpenError()}"); return false; }

        var idToPal = new Dictionary<ushort, int> { [0] = 0 };
        var names = new List<string> { "air" };
        var chunks = new List<Chunk>();
        foreach (var ch in world.Chunks.Values)
            if (ch.SolidCount > 0) chunks.Add(ch);
        foreach (var ch in chunks)
            foreach (ushort id in ch.Blocks)
                if (!idToPal.ContainsKey(id)) { idToPal[id] = names.Count; names.Add(BlockRegistry.Get(id).Name); }

        f.Store32(WorldMagic);
        f.Store32(Version);
        f.Store32((uint)names.Count);
        foreach (string n in names) f.StorePascalString(n);
        f.Store32((uint)chunks.Count);
        foreach (var ch in chunks)
        {
            f.Store32((uint)ch.Coord.X);
            f.Store32((uint)ch.Coord.Y);
            f.Store32((uint)ch.Coord.Z);
            WriteRle(f, ch.Blocks, idToPal);
        }
        GD.Print($"[WorldIO] saved {chunks.Count} chunks, {names.Count} block types -> {path}");
        return true;
    }

    public static bool LoadWorld(VoxelWorld world, string path)
    {
        if (!FileAccess.FileExists(path)) { GD.PushError($"[WorldIO] missing {path}"); return false; }
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null || f.Get32() != WorldMagic) { GD.PushError($"[WorldIO] bad file {path}"); return false; }
        f.Get32(); // version

        int paletteCount = (int)f.Get32();
        var palToId = new ushort[paletteCount];
        for (int i = 0; i < paletteCount; i++) palToId[i] = BlockRegistry.IdOf(f.GetPascalString());

        world.Clear();
        int chunkCount = (int)f.Get32();
        for (int c = 0; c < chunkCount; c++)
        {
            int x = (int)f.Get32(), y = (int)f.Get32(), z = (int)f.Get32();
            var data = ReadRle(f, Chunk.Volume, palToId);
            world.LoadChunk(new Vector3I(x, y, z), data);
        }
        world.RebuildAllNow();
        GD.Print($"[WorldIO] loaded {chunkCount} chunks from {path}");
        return true;
    }

    // ---- structures (prefabs) --------------------------------------------

    public static Structure Capture(VoxelWorld world, Vector3I a, Vector3I b)
    {
        Vector3I min = new(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Min(a.Z, b.Z));
        Vector3I max = new(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y), Mathf.Max(a.Z, b.Z));
        var size = max - min + Vector3I.One;

        var idToPal = new Dictionary<ushort, int> { [0] = 0 };
        var names = new List<string> { "air" };
        var cells = new ushort[size.X * size.Y * size.Z];
        var s = new Structure { Size = size };
        for (int y = 0; y < size.Y; y++)
        for (int z = 0; z < size.Z; z++)
        for (int x = 0; x < size.X; x++)
        {
            ushort id = world.GetBlockId(min.X + x, min.Y + y, min.Z + z);
            if (!idToPal.TryGetValue(id, out int pal)) { pal = names.Count; idToPal[id] = pal; names.Add(BlockRegistry.Get(id).Name); }
            cells[s.Index(x, y, z)] = (ushort)pal;
        }
        s.Palette = names.ToArray();
        s.Cells = cells;
        return s;
    }

    public static void Stamp(VoxelWorld world, Structure s, Vector3I origin, bool includeAir = false)
    {
        var palToId = new ushort[s.Palette.Length];
        for (int i = 0; i < s.Palette.Length; i++) palToId[i] = BlockRegistry.IdOf(s.Palette[i]);
        for (int y = 0; y < s.Size.Y; y++)
        for (int z = 0; z < s.Size.Z; z++)
        for (int x = 0; x < s.Size.X; x++)
        {
            ushort id = palToId[s.Cells[s.Index(x, y, z)]];
            if (id == 0 && !includeAir) continue;
            world.SetBlock(origin.X + x, origin.Y + y, origin.Z + z, id, remesh: false);
        }
        world.MarkAllDirty();
    }

    public static bool SaveStructure(Structure s, string path)
    {
        EnsureDir(path);
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f == null) return false;
        f.Store32(StructMagic);
        f.Store32(Version);
        f.Store32((uint)s.Size.X); f.Store32((uint)s.Size.Y); f.Store32((uint)s.Size.Z);
        f.Store32((uint)s.Palette.Length);
        foreach (string n in s.Palette) f.StorePascalString(n);
        var idToPal = new Dictionary<ushort, int>();
        for (ushort i = 0; i < s.Palette.Length; i++) idToPal[i] = i;
        WriteRle(f, s.Cells, idToPal);
        return true;
    }

    public static Structure LoadStructure(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null || f.Get32() != StructMagic) return null;
        f.Get32();
        var size = new Vector3I((int)f.Get32(), (int)f.Get32(), (int)f.Get32());
        int pc = (int)f.Get32();
        var palette = new string[pc];
        for (int i = 0; i < pc; i++) palette[i] = f.GetPascalString();
        var identity = new ushort[pc];
        for (ushort i = 0; i < pc; i++) identity[i] = i;
        var cells = ReadRle(f, size.X * size.Y * size.Z, identity);
        return new Structure { Size = size, Palette = palette, Cells = cells };
    }

    // ---- helpers ----------------------------------------------------------

    private static void WriteRle(FileAccess f, ushort[] data, Dictionary<ushort, int> idToPal)
    {
        var runs = new List<(ushort pal, ushort len)>();
        int i = 0;
        while (i < data.Length)
        {
            ushort id = data[i];
            int len = 1;
            while (i + len < data.Length && data[i + len] == id && len < 60000) len++;
            runs.Add(((ushort)idToPal[id], (ushort)len));
            i += len;
        }
        f.Store32((uint)runs.Count);
        foreach (var (pal, len) in runs) { f.Store16(pal); f.Store16(len); }
    }

    private static ushort[] ReadRle(FileAccess f, int count, ushort[] palToId)
    {
        var data = new ushort[count];
        int runCount = (int)f.Get32();
        int idx = 0;
        for (int r = 0; r < runCount && idx < count; r++)
        {
            ushort pal = f.Get16();
            int len = f.Get16();
            ushort id = pal < palToId.Length ? palToId[pal] : (ushort)0;
            for (int k = 0; k < len && idx < count; k++) data[idx++] = id;
        }
        return data;
    }

    private static void EnsureDir(string path)
    {
        int slash = path.LastIndexOf('/');
        if (slash < 0) return;
        string dir = path.Substring(0, slash);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
    }
}
