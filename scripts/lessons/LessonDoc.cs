using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using RAEngine.Dialogue;

namespace RAEngine.Lessons;

/// <summary>The on-disk shape of a JSON-authored lesson (res://assets/lessons/&lt;id&gt;.json),
/// deserialized by System.Text.Json and played by <see cref="JsonLesson"/>. Deliberately plain,
/// flat, and forgiving: mutable classes with defaults (so a sparse file never yields a null
/// surprise), vectors/colors as float arrays (Godot structs can't be bound directly), and every
/// enum-like value as a free string parsed in the interpreter (a typo degrades to a default + a
/// warning rather than failing the whole load).</summary>
public sealed class LessonDoc
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Subtitle { get; set; } = "";
    public float[] Spawn { get; set; }                  // [x,y,z]; null -> (0,3,0)
    public string Time { get; set; }                    // "dawn"/"morning"/"noon"/"dusk"/"night"/"cycle" or a 0..1 number
    public string Mood { get; set; }                    // calm | hope | solemn; null -> calm
    public string Mode { get; set; }                    // "build" | "adventure"; null -> adventure
    public bool Disarm { get; set; }                    // remove the player's weapon (peaceful lessons)

    [JsonConverter(typeof(StringOrArrayConverter))]
    public string[] Intro { get; set; }                 // opening narration (string or array)

    public List<TerrainOp> Terrain { get; set; } = new();
    public List<NarrationDto> Narrations { get; set; } = new();
    public List<NpcDto> Npcs { get; set; } = new();
    public List<EnemyDto> Enemies { get; set; } = new();
    public QuestDto Quest { get; set; }                 // null -> free exploration
    public ChapterDto Chapter { get; set; }             // present -> appears in the campaign menu
}

/// <summary>One terrain build verb. A single flat type for ALL ops (no polymorphic arrays, which
/// System.Text.Json handles poorly): each verb reads only the fields it needs.</summary>
public sealed class TerrainOp
{
    public string Op { get; set; }                      // flat|set|fill|clear|line|mound|tree|hut|tent|altar
    public float[] At { get; set; }                     // set/tree/hut/tent/altar origin
    public float[] From { get; set; }                   // fill/clear/line corner A
    public float[] To { get; set; }                     // fill/clear/line corner B
    public string Block { get; set; }                   // block name; "air"/null -> 0
    public int X0 { get; set; } public int X1 { get; set; }
    public int Z0 { get; set; } public int Z1 { get; set; } public int Y { get; set; }   // flat bounds
    public int X { get; set; } public int Z { get; set; } public int R { get; set; } public int H { get; set; } // mound (x,z,r,h)
    public int W { get; set; } public int D { get; set; }                                 // hut footprint (w x d), H = height
    public int Height { get; set; }                     // tree trunk height
    public string Top { get; set; } public string Fill { get; set; } public string Base { get; set; } public int Depth { get; set; } // flat overrides
    public string Wall { get; set; } public string Roof { get; set; } public string Cloth { get; set; }         // hut/tent materials
}

public sealed class NarrationDto
{
    public string Id { get; set; }                      // null -> pure flavour; set -> a Reach objective target
    public float[] Pos { get; set; }
    public float[] Size { get; set; }                   // null -> [4,4,4]
    public bool Once { get; set; } = true;

    [JsonConverter(typeof(StringOrArrayConverter))]
    public string[] Lines { get; set; }
}

public sealed class NpcDto
{
    public string Name { get; set; } = "Villager";
    public float[] Pos { get; set; }
    public bool Beast { get; set; }
    public float[] Skin { get; set; }
    public float[] Robe { get; set; }
    public float[] Accent { get; set; }

    [JsonConverter(typeof(StringOrArrayConverter))]
    public string[] Say { get; set; }                   // inline LINEAR dialogue (speaker = Name) — easiest
    public string DialogueId { get; set; }              // reference res://assets/dialogue/<id>.json
    public DialogueData Dialogue { get; set; }          // inline BRANCHING dialogue (reuses the dialogue shape)
}

public sealed class EnemyDto
{
    public string Type { get; set; } = "Soldier";       // Soldier | Wolf | Giant (factory)
    public string Name { get; set; }                    // overrides the type name -> the Defeat key & wake target
    public float[] Pos { get; set; }
    public float? Health { get; set; }
    public float? Scale { get; set; }
    public bool Dormant { get; set; }                   // Target = null until an effect wakes it
}

public sealed class QuestDto
{
    public List<ObjectiveDto> Objectives { get; set; } = new();
    public EffectDto OnComplete { get; set; }           // the whole-quest finale
}

public sealed class ObjectiveDto
{
    public string Kind { get; set; }                    // talk | defeat | reach | break | place | collect
    public string Key { get; set; }                     // npc name / enemy name / trigger id
    public string Block { get; set; }                   // alias for Key on break/place/collect
    public int Count { get; set; } = 1;                 // talk + no key => "talk any N"
    public string Label { get; set; } = "";
    public bool Optional { get; set; }
    public EffectDto OnComplete { get; set; }           // per-objective flourish
}

/// <summary>The data form of a lesson's OnComplete closure: one flat object, every field optional,
/// applied in a fixed order (wake -> narrate -> center -> banner -> mood -> sound -> fx).</summary>
public sealed class EffectDto
{
    [JsonConverter(typeof(StringOrArrayConverter))]
    public string[] Narrate { get; set; }               // Narrator.ShowMany (accepts a single string too)
    public string Center { get; set; }                  // Hud.ShowCenter (sticky)
    public string Banner { get; set; }                  // Hud.ShowBanner
    public string Sound { get; set; }                   // AudioManager.Play (e.g. "fanfare", "chime")
    public string Mood { get; set; }                    // AudioManager.SetMusicMood (calm|hope|solemn)
    public string Wake { get; set; }                    // enemy name -> Target = player ("*" = all)
    public FxDto Fx { get; set; }
}

public sealed class FxDto
{
    public string Kind { get; set; }                    // poof | debris | splash | sparkle | dust
    public float[] At { get; set; }
    public float[] Tint { get; set; }                   // rgb(a); null -> white
    public int Count { get; set; }                      // 0 -> Fx default
}

public sealed class ChapterDto
{
    public string[] Requires { get; set; }              // lesson ids that must be completed first
    public int Order { get; set; }                      // sort order among JSON chapters
}

/// <summary>Lets narration/intro/say/narrate fields accept EITHER a single string OR a string
/// array — a real ergonomics win for hand-authored JSON.</summary>
public sealed class StringOrArrayConverter : JsonConverter<string[]>
{
    public override string[] Read(ref Utf8JsonReader reader, System.Type t, JsonSerializerOptions o)
    {
        if (reader.TokenType == JsonTokenType.String) return new[] { reader.GetString() };
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String) list.Add(reader.GetString());
                else reader.Skip(); // step over a nested array/object so the reader stays in sync
            }
            return list.ToArray();
        }
        if (reader.TokenType == JsonTokenType.Null) return null;
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter w, string[] v, JsonSerializerOptions o)
    {
        w.WriteStartArray();
        if (v != null) foreach (string s in v) w.WriteStringValue(s);
        w.WriteEndArray();
    }
}
