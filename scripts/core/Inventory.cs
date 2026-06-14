using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>A simple block inventory: a count per block id, kept in pickup order so
/// the hotbar layout stays stable as the player gathers. Used by the survival-style
/// sandbox (break to collect, place to consume, craft to convert).</summary>
public sealed class Inventory
{
    public const int Max = 999;

    private readonly List<ushort> _order = new();
    private readonly Dictionary<ushort, int> _counts = new();

    public event System.Action Changed;

    /// <summary>Block ids in pickup order (drives the hotbar slot order).</summary>
    public IReadOnlyList<ushort> Order => _order;

    public int Count(ushort id) => _counts.TryGetValue(id, out int c) ? c : 0;
    public bool Has(ushort id, int n = 1) => id != 0 && Count(id) >= n;

    public void Add(ushort id, int n = 1)
    {
        if (id == 0 || n <= 0) return;
        if (!_counts.ContainsKey(id)) { _counts[id] = 0; _order.Add(id); }
        _counts[id] = Mathf.Min(Max, _counts[id] + n);
        Changed?.Invoke();
    }

    public bool TryConsume(ushort id, int n = 1)
    {
        if (!Has(id, n)) return false;
        _counts[id] -= n;
        if (_counts[id] <= 0) { _counts.Remove(id); _order.Remove(id); }
        Changed?.Invoke();
        return true;
    }

    /// <summary>True if a whole recipe's inputs are present.</summary>
    public bool CanAfford(Recipe r)
    {
        foreach (var (block, count) in r.Inputs)
            if (Count(BlockRegistry.IdOf(block)) < count) return false;
        return true;
    }

    /// <summary>Consume a recipe's inputs and add its output. No-op + false if the
    /// inputs aren't all present.</summary>
    public bool Craft(Recipe r)
    {
        if (!CanAfford(r)) return false;
        foreach (var (block, count) in r.Inputs)
            TryConsume(BlockRegistry.IdOf(block), count);
        Add(BlockRegistry.IdOf(r.Output.block), r.Output.count);
        return true;
    }
}
