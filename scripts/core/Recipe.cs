namespace RAEngine.Core;

/// <summary>A fixed crafting recipe: a few block inputs in, one block stack out.
/// Blocks are referenced by name so recipes survive id changes.</summary>
public sealed class Recipe
{
    public string Name;
    public (string block, int count)[] Inputs;
    public (string block, int count) Output;

    public Recipe(string name, (string, int) output, params (string block, int count)[] inputs)
    {
        Name = name;
        Output = output;
        Inputs = inputs;
    }
}

/// <summary>The short, fixed recipe list shown in the crafting menu. Deliberately
/// simple — convert raw gathered blocks into nicer building blocks.</summary>
public static class CraftBook
{
    public static readonly Recipe[] All =
    {
        new("Wood Planks", ("planks", 4), ("oak_log", 1)),
        new("Stone Bricks", ("stone_brick", 4), ("cobblestone", 4)),
        new("Cobblestone", ("cobblestone", 1), ("stone", 1)),
        new("Sandstone", ("sandstone", 1), ("sand", 4)),
        new("Clay Bricks", ("brick", 4), ("clay", 4)),
        new("Mud Bricks", ("mud_brick", 4), ("dirt", 4)),
        new("Thatch", ("thatch", 2), ("leaves", 4)),
        new("Lamp", ("lamp", 1), ("planks", 4), ("gold_block", 1)),
    };
}
