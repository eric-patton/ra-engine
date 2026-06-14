using System.Collections.Generic;
using Godot;
using RAEngine.Core;
using RAEngine.Dialogue;
using RAEngine.NpcSys;
using RAEngine.World;

namespace RAEngine.Lessons;

/// <summary>A peaceful exploration lesson: walk through the Garden of Eden, name
/// the animals, swim the river, and reach the Tree of Life. (Genesis 1–2.)
/// Showcases the calm, story-driven side of the engine — no combat.</summary>
public sealed class CreationGarden : ILesson
{
    public string Id => "creation";
    public string Title => "The Garden of Eden";
    public string Subtitle => "Creation — Genesis 1–2";
    public Vector3 Spawn => new(32, 3, 52);
    public float? TimeOfDay => Core.EnvironmentController.Dawn; // the freshness of a new creation

    private GameSession _s;
    private int _named;
    private readonly HashSet<Npc> _namedSet = new();

    public void Build(GameSession session)
    {
        _s = session;
        var w = session.World;

        BuildGarden(w);
        w.MarkAllDirty();
        w.RebuildAllNow();

        session.SetMode(GameSession.Mode.Build); // peaceful: let them shape the garden too, no weapon
        session.Weapons.Equip(null);

        session.Hud.SetObjectives(new[]
        {
            "Name the animals of the garden (0/3)",
            "Cross the river to the Tree of Life",
        });
        session.Narrator.ShowMany(new[]
        {
            "In the beginning God created the heavens and the earth.",
            "And God planted a garden eastward in Eden, and there He put the man.",
        });

        // animals to name
        AddAnimal(w, "Lion", new Vector3(24, 1, 22), new Color(0.78f, 0.6f, 0.3f), new Color(0.6f, 0.45f, 0.2f),
            NameDialogue("lion", "Lion", "Ari", "Leo"));
        AddAnimal(w, "Ox", new Vector3(41, 1, 20), new Color(0.5f, 0.38f, 0.28f), new Color(0.7f, 0.6f, 0.5f),
            NameDialogue("ox", "Ox", "Behemah", "Shor"));
        AddAnimal(w, "Dove", new Vector3(30, 1, 9), new Color(0.92f, 0.92f, 0.95f), new Color(0.8f, 0.8f, 0.85f),
            NameDialogue("dove", "Dove", "Yonah", "Columba"));

        // narration as the player walks toward the river and the Tree
        AddNarration(new Vector3(32, 2, 44), new Vector3(64, 6, 3),
            "And God said, Let the land produce vegetation -- and the trees grew, each bearing fruit.");
        AddNarration(new Vector3(32, 2, 31), new Vector3(64, 6, 3),
            "Let the waters be gathered together, and let the dry land appear. (Swim across the river.)");
        AddNarration(new Vector3(32, 2, 20), new Vector3(64, 6, 3),
            "Let the land bring forth living creatures. The man gave names to all of them.");

        // reaching the Tree of Life completes the lesson
        var treeTrigger = NarrationTrigger.Create(new Vector3(32, 3, 12), new Vector3(6, 8, 6), session.Narrator,
            "In the midst of the garden stood the Tree of Life.");
        w.AddChild(treeTrigger);
        treeTrigger.Entered += () =>
        {
            session.Hud.CompleteObjective(1);
            session.Narrator.Show("And God saw everything that He had made, and behold, it was very good.");
            session.Hud.ShowCenter("The Garden of Eden\nAnd it was very good.", 0f);
            // celebrate reaching the Tree of Life: a fanfare and a shower of golden motes
            Core.AudioManager.Play("fanfare");
            Core.Fx.Burst(new Vector3(32, 11, 12), Core.FxKind.Sparkle, new Color(1f, 0.95f, 0.6f), 48);
        };
    }

    private void AddAnimal(VoxelWorld w, string name, Vector3 pos, Color fur, Color belly, DialogueData dlg)
    {
        var a = new Npc { NpcName = name, Beast = true, Skin = fur, Robe = belly, Dialogue = dlg };
        w.AddChild(a);
        a.GlobalPosition = pos;
        a.Talked += () =>
        {
            if (!_namedSet.Add(a)) return; // count each animal once, not each chat
            _named++;
            _s.Hud.SetObjectives(new[]
            {
                $"Name the animals of the garden ({Mathf.Min(_named, 3)}/3)",
                "Cross the river to the Tree of Life",
            });
            if (_named >= 3)
            {
                _s.Hud.CompleteObjective(0);
                _s.Narrator.Show("Adam gave names to all the animals of the field.");
            }
        };
    }

    private void AddNarration(Vector3 pos, Vector3 size, params string[] lines)
    {
        var t = NarrationTrigger.Create(pos, size, _s.Narrator, lines);
        _s.World.AddChild(t);
    }

    private static DialogueData NameDialogue(string animal, params string[] names)
    {
        var d = new DialogueData { Start = "0" };
        var choices = new List<DialogueChoice>();
        foreach (string n in names) choices.Add(new DialogueChoice { Text = n, Next = "c" });
        d.Nodes["0"] = new DialogueLine { Speaker = "Eden", Text = $"A {animal} comes near, waiting to be named.", Choices = choices };
        d.Nodes["c"] = new DialogueLine { Speaker = "Adam", Text = "\"So shall it be called,\" said the man.", Next = null };
        return d;
    }

    // ---- terrain ----------------------------------------------------------

    private static void BuildGarden(VoxelWorld w)
    {
        WorldGen.FlatGround(w, 0, 63, 0, 63, 0);

        // gentle hills
        // (kept subtle so the garden reads as open and inviting)
        ushort grass = BlockRegistry.IdOf("grass");
        ushort dirt = BlockRegistry.IdOf("dirt");

        // a 3-deep river band the player must swim across (z 28..31)
        ushort water = BlockRegistry.IdOf("water");
        ushort sand = BlockRegistry.IdOf("sand");
        ushort stone = BlockRegistry.IdOf("stone");
        for (int x = 0; x <= 63; x++)
        {
            for (int z = 28; z <= 31; z++)
            {
                for (int y = 0; y >= -2; y--) w.SetBlock(x, y, z, water, false);
                w.SetBlock(x, -3, z, stone, false);
            }
            w.SetBlock(x, 0, 27, sand, false);
            w.SetBlock(x, 0, 32, sand, false);
        }

        // scattered fruit trees (deterministic positions, clear of river + spawn)
        var spots = new (int x, int z, int h)[]
        {
            (10, 44, 4), (52, 46, 5), (18, 38, 4), (46, 40, 5),
            (8, 18, 4), (56, 16, 5), (20, 8, 4), (44, 6, 4), (14, 24, 5),
        };
        foreach (var (x, z, h) in spots)
            WorldGen.Tree(w, new Vector3I(x, 1, z), h);

        // the Tree of Life — a tall tree on a small mound, ringed with gold and a glowing crown
        ushort gold = BlockRegistry.IdOf("gold_block");
        ushort lamp = BlockRegistry.IdOf("lamp");
        for (int x = -2; x <= 2; x++)
        for (int z = -2; z <= 2; z++)
        {
            if (Mathf.Abs(x) + Mathf.Abs(z) <= 3)
                w.SetBlock(32 + x, 1, 12 + z, grass, false);
        }
        WorldGen.Tree(w, new Vector3I(32, 2, 12), 7);
        foreach (var (dx, dz) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            w.SetBlock(32 + dx, 2, 12 + dz, gold, false);
        w.SetBlock(32, 10, 12, lamp, false);
    }
}
