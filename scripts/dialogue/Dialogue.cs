using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace RAEngine.Dialogue;

/// <summary>One line of dialogue. <see cref="Choices"/>, when present, branches;
/// otherwise the conversation advances to <see cref="Next"/> (null/empty ends it).</summary>
public sealed class DialogueLine
{
    public string Speaker { get; set; } = "";
    public string Text { get; set; } = "";
    public string Next { get; set; }
    public List<DialogueChoice> Choices { get; set; }
}

public sealed class DialogueChoice
{
    public string Text { get; set; } = "";
    public string Next { get; set; }
}

/// <summary>A whole conversation, addressable by node id. Authored either in
/// JSON under <c>assets/dialogue/&lt;id&gt;.json</c> (teacher-editable) or built
/// inline in C#.</summary>
public sealed class DialogueData
{
    public string Start { get; set; } = "0";
    public Dictionary<string, DialogueLine> Nodes { get; set; } = new();

    public DialogueLine Get(string id) => id != null && Nodes.TryGetValue(id, out var n) ? n : null;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static DialogueData FromJsonFile(string resPath)
    {
        if (!FileAccess.FileExists(resPath))
        {
            GD.PushError($"[Dialogue] missing file {resPath}");
            return null;
        }
        string text = FileAccess.GetFileAsString(resPath);
        try
        {
            return JsonSerializer.Deserialize<DialogueData>(text, JsonOpts);
        }
        catch (System.Exception e)
        {
            GD.PushError($"[Dialogue] parse error in {resPath}: {e.Message}");
            return null;
        }
    }

    /// <summary>Build a simple one-speaker, linear conversation.</summary>
    public static DialogueData Linear(string speaker, params string[] lines)
    {
        var d = new DialogueData { Start = "0" };
        for (int i = 0; i < lines.Length; i++)
            d.Nodes[i.ToString()] = new DialogueLine
            {
                Speaker = speaker,
                Text = lines[i],
                Next = i < lines.Length - 1 ? (i + 1).ToString() : null,
            };
        return d;
    }
}

/// <summary>Loads and caches dialogue JSON files by id.</summary>
public static class Dialogues
{
    private const string Root = "res://assets/dialogue";
    private static readonly Dictionary<string, DialogueData> Cache = new();

    public static DialogueData Load(string id)
    {
        if (Cache.TryGetValue(id, out var d)) return d;
        d = DialogueData.FromJsonFile($"{Root}/{id}.json");
        if (d != null) Cache[id] = d;
        return d;
    }

    public static void Register(string id, DialogueData data) => Cache[id] = data;
}
