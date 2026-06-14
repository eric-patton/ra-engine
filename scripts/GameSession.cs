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
    public EnvironmentController Env { get; private set; }
    public Player Player { get; private set; }
    public GameHud Hud { get; private set; }
    public BlockInteractor Interactor { get; private set; }
    public BuildEditor Editor { get; private set; }
    public WeaponController Weapons { get; private set; }
    public DialogueBox Dialogue { get; private set; }
    public Narrator Narrator { get; private set; }
    public PauseMenu Pause { get; private set; }
    public Mode CurrentMode { get; private set; }
    public bool InDialogue { get; private set; }
    public float InteractRange = 3.8f;
    public System.Action ReturnToMenuRequested;
    private bool _wasFocused = true;

    public Inventory Inventory { get; private set; }
    public bool Survival { get; private set; }
    private CraftingMenu _craft;
    private bool _crafting;
    private Input.MouseModeEnum _preCraftMouse = Input.MouseModeEnum.Visible;

    public static readonly string[] DefaultPalette =
    {
        "grass", "dirt", "stone", "cobblestone", "planks", "oak_log", "mud_brick", "leaves", "lamp",
    };

    /// <summary>Builds the session. <paramref name="captureMouse"/> overrides the
    /// user's capture preference: <c>true</c> forces a grabbed cursor, <c>false</c>
    /// forces a free one (used by headless tests), and <c>null</c> (the normal
    /// path) honours <see cref="Settings.CaptureMode"/>.</summary>
    public void Setup(Vector3 spawn, bool creative, IEnumerable<string> palette = null, bool? captureMouse = null)
    {
        Env = Scenery.AddDaylight(this);

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

        Editor = new BuildEditor { Name = "Editor", World = World, Interactor = Interactor, Player = Player, Hotbar = Hud.Hotbar, Hud = Hud };
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

        Pause = new PauseMenu { Name = "PauseMenu" };
        Pause.CanPause = () => !InDialogue && !_crafting;
        Pause.OnReturnToMenu = () => ReturnToMenuRequested?.Invoke();
        AddChild(Pause);

        Player.HealthChanged += Hud.SetHealth;
        Player.AirChanged += Hud.SetAir;
        // seed the bars now, since Player._Ready already emitted its initial values
        Hud.SetHealth(Player.Health, Player.MaxHealth);
        Hud.SetAir(Player.Air, Player.MaxAir);

        SetMode(creative ? Mode.Build : Mode.Adventure);

        // Decide the starting cursor state: an explicit override wins, otherwise
        // honour the user's setting (default ClickToCapture = free cursor, so the
        // game never silently swallows the mouse).
        Player.Camera.Current = true;
        bool grab = captureMouse ?? (Settings.CaptureMode == Settings.MouseCapture.Always);
        Input.MouseMode = grab ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }

    /// <summary>Turn on the survival-style loop: blocks are gathered by breaking and
    /// spent by placing, the hotbar tracks stack counts, and a Tab crafting menu is
    /// available. <paramref name="startKit"/> seeds a few stacks so building is easy
    /// from the start.</summary>
    public void EnableSurvival(params (string block, int count)[] startKit)
    {
        Inventory = new Inventory();
        Survival = true;
        foreach (var (block, count) in startKit)
            Inventory.Add(BlockRegistry.IdOf(block), count);

        Interactor.Inventory = Inventory;
        Hud.Hotbar.BindInventory(World.Textures, Inventory);

        _craft = new CraftingMenu { Name = "Crafting" };
        AddChild(_craft);
        _craft.Setup(Inventory, World.Textures);
        _craft.OnClose = CloseCrafting;
    }

    /// <summary>Snapshot this sandbox for saving: seed, player position, time of day,
    /// inventory and the player's edit-deltas.</summary>
    public SaveData CaptureSave(string name, long savedUnix)
    {
        var d = new SaveData
        {
            Name = name,
            Seed = World.Seed,
            SavedUnix = savedUnix,
            PlayerPos = Player.GlobalPosition,
            TimeOfDay = Env?.TimeOfDay ?? 0.4f,
        };
        if (Inventory != null)
            foreach (ushort id in Inventory.Order)
                d.Inventory[BlockRegistry.Get(id).Name] = Inventory.Count(id);
        foreach (var (pos, id) in World.AllEdits())
            d.Edits.Add((pos.X, pos.Y, pos.Z, BlockRegistry.Get(id).Name));
        return d;
    }

    private void ToggleCrafting()
    {
        if (_crafting) CloseCrafting();
        else OpenCrafting();
    }

    private void OpenCrafting()
    {
        _crafting = true;
        _preCraftMouse = Input.MouseMode;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        Interactor.CanEdit = false;
        Hud.SetMouseHint("");
        _craft.Open();
    }

    private void CloseCrafting()
    {
        _crafting = false;
        _craft.Visible = false;
        Input.MouseMode = _preCraftMouse;
        Player.InputEnabled = true;
        SetMode(CurrentMode); // restore build/weapon interaction for the mode
    }

    public override void _Process(double delta)
    {
        if (Player == null) return;
        Hud.SetUnderwater(Player.HeadUnderwater);

        bool busy = InDialogue || _crafting || (Pause?.IsPaused ?? false);

        // Tab toggles the crafting menu (survival sandbox only).
        if (Survival && !InDialogue && !(Pause?.IsPaused ?? false)
            && Input.IsActionJustPressed(GameInput.Actions.Inventory))
        {
            ToggleCrafting();
            return;
        }
        if (_crafting) return;

        // Only the classic "Always" mode re-grabs the cursor when the window
        // regains focus; click-to-capture lets the player click back in instead,
        // so an alt-tab never silently steals the pointer again.
        bool focused = DisplayServer.WindowIsFocused(0);
        if (focused && !_wasFocused && !busy
            && Settings.CaptureMode == Settings.MouseCapture.Always)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            Player.SuppressActionsFor(0.2f);
        }
        _wasFocused = focused;

        // Grab/release the mouse on demand (M), in any capture mode.
        if (!busy && Input.IsActionJustPressed(GameInput.Actions.ToggleCapture))
        {
            bool wasCap = Input.MouseMode == Input.MouseModeEnum.Captured;
            Input.MouseMode = wasCap ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
            if (!wasCap) Player.SuppressActionsFor(0.2f);
            Hud.ShowBanner(wasCap ? "Mouse freed" : "Mouse look on", 1.2f);
        }

        // Hint the player how to look when the cursor is free during play.
        if (!busy && Input.MouseMode != Input.MouseModeEnum.Captured
            && Settings.CaptureMode == Settings.MouseCapture.ClickToCapture)
            Hud.SetMouseHint("Click to look around   ·   M to free the mouse   ·   arrows/numpad also look");
        else
            Hud.SetMouseHint("");

        if (InDialogue) return;

        if (Input.IsActionJustPressed(GameInput.Actions.ToggleBuild) && Player.InputEnabled)
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
                StartDialogueWith(best);
        }
        else
        {
            Hud.SetInteractPrompt("");
        }
    }

    /// <summary>In click-to-capture mode, a click in the world grabs the cursor so
    /// the player can look with the mouse. The triggering click is consumed and a
    /// brief action lockout keeps it from also breaking/placing or firing. (In
    /// "Off" mode the cursor stays free; in "Always" it is already captured.)</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (Settings.CaptureMode == Settings.MouseCapture.ClickToCapture
            && e is InputEventMouseButton { Pressed: true }
            && Input.MouseMode != Input.MouseModeEnum.Captured
            && !InDialogue && !_crafting && !(Pause?.IsPaused ?? false))
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            Player?.SuppressActionsFor(0.2f);
            GetViewport().SetInputAsHandled();
        }
    }

    private Npc _talkingNpc;
    private Input.MouseModeEnum _preDialogueMouse = Input.MouseModeEnum.Visible;

    public void StartDialogueWith(Npc npc)
    {
        StartDialogue(npc.Dialogue, npc);
    }

    public void StartDialogue(DialogueData data, Npc npc = null)
    {
        if (data == null || InDialogue) return;
        _talkingNpc = npc;
        InDialogue = true;
        Hud.SetInteractPrompt("");
        Hud.SetMouseHint("");
        Player.InputEnabled = false;
        Weapons.SetEnabled(false);
        Interactor.CanEdit = false;
        _preDialogueMouse = Input.MouseMode;          // restore exactly afterward
        Input.MouseMode = Input.MouseModeEnum.Visible; // free the cursor to click choices
        Dialogue.StartDialogue(data);
    }

    private void OnDialogueFinished()
    {
        InDialogue = false;
        Player.InputEnabled = true;
        SetMode(CurrentMode); // restores weapon/build interaction for the mode
        // Restore the cursor to whatever it was before the conversation, so we
        // never silently grab the mouse from a player who had it free.
        Input.MouseMode = _preDialogueMouse;
        var npc = _talkingNpc;
        _talkingNpc = null;
        npc?.EmitSignal(Npc.SignalName.Talked);
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
