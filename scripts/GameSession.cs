using System.Collections.Generic;
using Godot;
using RAEngine.Combat;
using RAEngine.Core;
using RAEngine.Dialogue;
using RAEngine.NpcSys;
using RAEngine.PlayerSys;
using RAEngine.UI;

namespace RAEngine;

/// <summary>Assembles a playable session: environment, voxel world, player, HUD
/// and block interaction, all wired together. Reused by the sandbox and by
/// lessons (which generate a world and configure objectives on top).</summary>
public partial class GameSession : Node3D
{
    public enum Mode { Build, Adventure }

    public VoxelWorld World { get; private set; }
    public Player Player { get; private set; }
    public GameHud Hud { get; private set; }
    public BlockInteractor Interactor { get; private set; }
    public BuildEditor Editor { get; private set; }
    public WeaponController Weapons { get; private set; }
    public DialogueBox Dialogue { get; private set; }
    public Narrator Narrator { get; private set; }
    public Mode CurrentMode { get; private set; }
    public bool InDialogue { get; private set; }
    public float InteractRange = 3.8f;

    public static readonly string[] DefaultPalette =
    {
        "grass", "dirt", "stone", "cobblestone", "planks", "oak_log", "mud_brick", "leaves", "lamp",
    };

    public void Setup(Vector3 spawn, bool creative, IEnumerable<string> palette = null, bool captureMouse = true)
    {
        Scenery.AddDaylight(this);

        World = new VoxelWorld { Name = "World" };
        AddChild(World); // _Ready builds textures + material

        Player = new Player { Name = "Player", World = World };
        AddChild(Player);
        Player.SetCreative(creative);
        Player.GlobalPosition = spawn;

        Hud = new GameHud { Name = "Hud" };
        AddChild(Hud);

        var ids = new List<ushort>();
        foreach (string n in palette ?? DefaultPalette)
            if (BlockRegistry.TryId(n, out ushort id)) ids.Add(id);
        Hud.Configure(World.Textures, ids);

        Interactor = new BlockInteractor { Name = "Interactor", World = World, Player = Player, Hotbar = Hud.Hotbar };
        AddChild(Interactor);

        Editor = new BuildEditor { Name = "Editor", World = World, Interactor = Interactor, Hotbar = Hud.Hotbar, Hud = Hud };
        AddChild(Editor);

        Weapons = new WeaponController { Name = "Weapons", Player = Player, ProjectileParent = this };
        Player.AddChild(Weapons);
        Weapons.AttachViewmodel();
        Weapons.Equip(Weapon.Sling());
        Weapons.WeaponChanged += Hud.SetWeapon;

        Narrator = new Narrator { Name = "Narrator" };
        AddChild(Narrator);
        Dialogue = new DialogueBox { Name = "DialogueBox" };
        AddChild(Dialogue);
        Dialogue.Finished += OnDialogueFinished;

        Player.HealthChanged += Hud.SetHealth;
        Player.AirChanged += Hud.SetAir;

        SetMode(creative ? Mode.Build : Mode.Adventure);

        if (captureMouse) Player.MakeCurrent();
        else Player.Camera.Current = true;
    }

    public override void _Process(double delta)
    {
        if (Player == null || InDialogue) return;

        if (Input.IsActionJustPressed(GameInput.Actions.ToggleBuild) && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            SetMode(CurrentMode == Mode.Build ? Mode.Adventure : Mode.Build);
            Hud.ShowBanner(CurrentMode == Mode.Build ? "Build mode" : "Adventure mode", 1.5f);
        }

        // find the nearest talkable NPC within reach
        Npc best = null;
        float bestDist = InteractRange;
        foreach (Node n in GetTree().GetNodesInGroup("npc"))
        {
            if (n is not Npc npc || npc.Dialogue == null) continue;
            float d = npc.GlobalPosition.DistanceTo(Player.GlobalPosition);
            if (d < bestDist) { bestDist = d; best = npc; }
        }

        if (best != null)
        {
            Hud.SetInteractPrompt($"[E]  Talk to {best.NpcName}");
            if (Input.IsActionJustPressed(GameInput.Actions.Interact))
                StartDialogue(best.Dialogue);
        }
        else
        {
            Hud.SetInteractPrompt("");
        }
    }

    public void StartDialogue(DialogueData data)
    {
        if (data == null || InDialogue) return;
        InDialogue = true;
        Hud.SetInteractPrompt("");
        Player.InputEnabled = false;
        Weapons.SetEnabled(false);
        Interactor.CanEdit = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Dialogue.StartDialogue(data);
    }

    private void OnDialogueFinished()
    {
        InDialogue = false;
        Player.InputEnabled = true;
        SetMode(CurrentMode); // restores weapon/build interaction for the mode
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Switch between block-building and weapon-combat interaction.</summary>
    public void SetMode(Mode mode)
    {
        CurrentMode = mode;
        bool build = mode == Mode.Build;
        Interactor.CanEdit = build;
        Editor.SetEnabled(build);
        Weapons.SetEnabled(!build);
        Hud.SetHotbarVisible(build);
        Hud.SetWeaponVisible(!build);
        Hud.SetWeapon(Weapons.Current?.Name ?? "");
    }

    public Enemy SpawnEnemy(EnemyType type, Vector3 position)
    {
        var e = new Enemy { Type = type, Target = Player, Name = $"Enemy_{type.Name}" };
        World.AddChild(e);
        e.GlobalPosition = position;
        return e;
    }
}
