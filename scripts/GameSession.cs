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
    private HeldItem _held;
    public DialogueBox Dialogue { get; private set; }
    public Narrator Narrator { get; private set; }
    public PauseMenu Pause { get; private set; }
    public Mode CurrentMode { get; private set; }
    public bool InDialogue { get; private set; }
    public float InteractRange = 3.8f;
    public System.Action ReturnToMenuRequested;
    private bool _wasFocused = true;

    /// <summary>The id of the lesson driving this session (carried by QuestCompleted),
    /// or null for the sandbox.</summary>
    public string LessonId { get; set; }

    /// <summary>The active objective tracker, or null when there is no quest.</summary>
    public Quests.QuestTracker Quest { get; private set; }

    // Session-level relays the quest tracker binds to. Routing through the session
    // (rather than the spawned nodes) means a freed NPC/enemy/trigger is irrelevant —
    // the tracker only ever holds a reference to this session.
    [Signal] public delegate void NpcTalkedEventHandler(string npcName);
    [Signal] public delegate void EnemyDefeatedEventHandler(string enemyTypeName);
    [Signal] public delegate void PlayerReachedEventHandler(string triggerId);
    [Signal] public delegate void QuestCompletedEventHandler(string lessonId);

    public Inventory Inventory { get; private set; }
    public bool Survival { get; private set; }
    private CraftingMenu _craft;
    private bool _crafting;
    private Input.MouseModeEnum _preCraftMouse = Input.MouseModeEnum.Visible;

    // Teacher tools.
    private TeacherPanel _teacher;
    private bool _teacherOpen;
    private bool _presentMode;
    private Input.MouseModeEnum _preTeacherMouse = Input.MouseModeEnum.Visible;
    private readonly List<(string name, Vector3 pos)> _waypoints = new();
    private readonly List<RAEngine.World.Signpost> _signposts = new();

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
        Env.SetWeatherFollow(Player); // weather + ambient particles track the player (sandbox & lessons)

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

        // First-person "held block": shows the selected hotbar block in hand.
        _held = new HeldItem { Name = "HeldItem", Player = Player, World = World };
        Player.AddChild(_held);
        _held.AttachViewmodel();
        Hud.Hotbar.SelectionChanged += _held.OnSelectionChanged;
        _held.OnSelectionChanged(Hud.Hotbar.SelectedBlockId);

        Narrator = new Narrator { Name = "Narrator" };
        AddChild(Narrator);
        Dialogue = new DialogueBox { Name = "DialogueBox" };
        AddChild(Dialogue);
        Dialogue.Finished += OnDialogueFinished;

        Pause = new PauseMenu { Name = "PauseMenu" };
        Pause.CanPause = () => !InDialogue && !_crafting && !_teacherOpen && !_presentMode;
        Pause.OnReturnToMenu = () => ReturnToMenuRequested?.Invoke();
        AddChild(Pause);

        Player.HealthChanged += Hud.SetHealth;
        Player.AirChanged += Hud.SetAir;
        Player.HealthChanged += (cur, max) => Hud.SetLowHealth(max > 0 ? cur / max : 0f);
        // seed the bars now, since Player._Ready already emitted its initial values
        Hud.SetHealth(Player.Health, Player.MaxHealth);
        Hud.SetAir(Player.Air, Player.MaxAir);

        // Screen "juice": a camera shaker (shakes only the camera's local offset, so
        // mouse-look stays intact) plus a HUD hurt-flash. Register them with the
        // app-wide Fx facade so any code can trigger them in one line.
        var screenFx = new ScreenFx { Name = "ScreenFx", Camera = Player.Camera };
        Player.AddChild(screenFx);
        Fx.OnShake = screenFx.AddTrauma;
        Fx.OnFlash = Hud.Flash;
        Player.Hurt += OnPlayerHurt;

        SetMode(creative ? Mode.Build : Mode.Adventure);

        // Decide the starting cursor state: an explicit override wins, otherwise
        // honour the user's setting (default ClickToCapture = free cursor, so the
        // game never silently swallows the mouse).
        Player.Camera.Current = true;
        bool grab = captureMouse ?? (Settings.CaptureMode == Settings.MouseCapture.Always);
        Input.MouseMode = grab ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;

        EnableTeacherTools();
    }

    // ---- teacher tools ----------------------------------------------------

    private void EnableTeacherTools()
    {
        Hud.SetCompassEnabled(true);
        _teacher = new TeacherPanel { Name = "TeacherPanel" };
        AddChild(_teacher);
        _teacher.GetSafe = () => Player.SafeMode;
        _teacher.SetSafe = on =>
        {
            Player.SafeMode = on;
            Hud.ShowBanner(on ? "Safe mode ON — no combat damage" : "Safe mode OFF", 1.6f);
        };
        _teacher.OnPresent = EnterPresentMode;
        _teacher.OnPlaceSignpost = PlaceSignpostAtLook;
        _teacher.OnAddWaypoint = AddWaypointHere;
        _teacher.GetWaypoints = () => _waypoints;
        _teacher.OnTeleport = TeleportToWaypoint;
        _teacher.OnClose = CloseTeacher;
    }

    private void ToggleTeacher()
    {
        if (_teacherOpen) CloseTeacher();
        else OpenTeacher();
    }

    private void OpenTeacher()
    {
        _teacherOpen = true;
        _preTeacherMouse = Input.MouseMode;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        Hud.SetMouseHint("");
        _teacher.Open();
    }

    private void CloseTeacher()
    {
        _teacherOpen = false;
        _teacher.Visible = false;
        Input.MouseMode = _preTeacherMouse;
        Player.InputEnabled = true;
    }

    private void EnterPresentMode()
    {
        _presentMode = true;
        Hud.Visible = false;
        // keep the cursor free so the teacher can move the mouse off-screen
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.SuppressActionsFor(0.2f);
    }

    private void ExitPresentMode()
    {
        _presentMode = false;
        Hud.Visible = true;
    }

    private void AddWaypointHere()
    {
        _waypoints.Add(($"Waypoint {_waypoints.Count + 1}", Player.GlobalPosition));
        Hud.ShowBanner("Waypoint set", 1.4f);
    }

    private void TeleportToWaypoint(int index)
    {
        if (index < 0 || index >= _waypoints.Count) return;
        Player.Velocity = Vector3.Zero;
        Player.GlobalPosition = _waypoints[index].pos + Vector3.Up * 0.2f;
        Hud.ShowBanner($"Teleported to {_waypoints[index].name}", 1.6f);
    }

    private void PlaceSignpostAtLook(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) text = "…";
        // Find the cell the player is looking at; sit the post just in front of it.
        Camera3D cam = Player.Camera;
        var hit = Core.VoxelRay.Cast(World, cam.GlobalPosition, -cam.GlobalTransform.Basis.Z, 8f);
        Vector3 pos = hit.Ok ? (Vector3)hit.Prev + new Vector3(0.5f, 0f, 0.5f)
                             : Player.GlobalPosition - Player.GlobalTransform.Basis.Z * 2f;
        var sign = RAEngine.World.Signpost.Create(pos, text);
        World.AddChild(sign);
        _signposts.Add(sign);
        Hud.ShowBanner("Signpost placed", 1.6f);
    }

    /// <summary>Restore signposts and waypoints from a save (called after Setup).</summary>
    public void RestoreTeacherState(
        IEnumerable<(Vector3 pos, string text)> signposts,
        IEnumerable<(string name, Vector3 pos)> waypoints)
    {
        foreach (var (pos, text) in signposts)
        {
            var s = RAEngine.World.Signpost.Create(pos, text);
            World.AddChild(s);
            _signposts.Add(s);
        }
        _waypoints.AddRange(waypoints);
    }

    public IReadOnlyList<(string name, Vector3 pos)> Waypoints => _waypoints;
    public IEnumerable<(Vector3 pos, string text)> Signposts()
    {
        foreach (var s in _signposts) yield return (s.GlobalPosition, s.Text);
    }

    private async void TakeScreenshot()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://screenshots"));
        var img = GetViewport().GetTexture().GetImage();
        string path = $"user://screenshots/shot_{Time.GetTicksMsec()}.png";
        img.SavePng(path);
        Hud.ShowBanner($"Saved {path}", 2f);
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
        foreach (var s in _signposts)
            d.Signposts.Add((s.GlobalPosition, s.Text));
        d.Waypoints.AddRange(_waypoints);
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
        // Light tint at the waterline (body in water), strong murk when submerged.
        Hud.SetUnderwater(Player.HeadUnderwater ? 1f : (Player.InWater ? 0.35f : 0f));
        Hud.SetAirDarken(Player.MaxAir > 0f ? 1f - Player.Air / Player.MaxAir : 0f);

        // Keep the compass pointing where the player faces (North = -Z).
        Vector3 fwd = -Player.GlobalTransform.Basis.Z;
        Hud.SetHeading(Mathf.RadToDeg(Mathf.Atan2(fwd.X, -fwd.Z)));

        // HUD clock follows the time of day.
        if (Env != null) Hud.SetClock(Env.ClockText());

        // F3 debug HUD (any mode): FPS, draw calls, chunk pipeline, player position.
        if (Input.IsActionJustPressed(GameInput.Actions.Debug)) Hud.ToggleDebug();
        if (Hud.DebugVisible)
        {
            Vector3 pp = Player.GlobalPosition;
            long draws = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            Hud.SetDebug(
                $"FPS {Engine.GetFramesPerSecond()}   draws {draws}\n" +
                $"chunks {World.ChunkCount}   meshing {World.MeshingCount}   dirty {World.DirtyCount}\n" +
                $"pos  {pp.X:F0}, {pp.Y:F0}, {pp.Z:F0}");
        }

        // Present mode: HUD is hidden; F2 grabs a screenshot, Esc returns.
        if (_presentMode)
        {
            if (Input.IsActionJustPressed(GameInput.Actions.Screenshot)) TakeScreenshot();
            if (Input.IsActionJustPressed(GameInput.Actions.Pause)) ExitPresentMode();
            return;
        }

        bool busy = InDialogue || _crafting || _teacherOpen || (Pause?.IsPaused ?? false);

        // F1 toggles the teacher panel (any mode, unless mid-dialogue/pause).
        if (!InDialogue && !_crafting && !(Pause?.IsPaused ?? false)
            && Input.IsActionJustPressed(GameInput.Actions.Teacher))
        {
            ToggleTeacher();
            return;
        }
        if (_teacherOpen) return;

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
            && !InDialogue && !_crafting && !_teacherOpen && !_presentMode
            && !(Pause?.IsPaused ?? false))
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
        if (npc != null) EmitSignal(SignalName.NpcTalked, npc.NpcName);
    }

    /// <summary>Switch between block-building and weapon-combat interaction.</summary>
    public void SetMode(Mode mode)
    {
        CurrentMode = mode;
        bool build = mode == Mode.Build;
        Interactor.CanEdit = build;
        Editor.SetEnabled(build);
        Weapons.SetEnabled(!build);
        _held?.SetShown(build);
        Hud.SetHotbarVisible(build);
        Hud.SetWeaponVisible(!build);
        Hud.SetWeapon(Weapons.Current?.Name ?? "");
    }

    public Enemy SpawnEnemy(EnemyType type, Vector3 position)
    {
        var e = new Enemy { Type = type, Target = Player, Name = $"Enemy_{type.Name}" };
        World.AddChild(e);
        e.GlobalPosition = position;
        e.Defeated += () => EmitSignal(SignalName.EnemyDefeated, type.Name);
        return e;
    }

    /// <summary>Build a narration trigger that also fires a quest "Reach" objective with
    /// <paramref name="id"/> when the player first enters it. (Pure-flavour narration
    /// can still use <see cref="RAEngine.World.NarrationTrigger.Create"/> directly.)</summary>
    public RAEngine.World.NarrationTrigger AddTrigger(Vector3 pos, Vector3 size, string id, params string[] lines)
    {
        var t = RAEngine.World.NarrationTrigger.Create(pos, size, Narrator, lines);
        t.Id = id;
        t.Entered += () => EmitSignal(SignalName.PlayerReached, id);
        World.AddChild(t);
        return t;
    }

    /// <summary>Begin tracking a lesson's quest. Called after the lesson has built its
    /// world, so arming block events here never counts terrain as player progress.</summary>
    public void StartQuest(Quests.Quest quest)
    {
        if (quest == null || Quest != null) return; // one quest per session
        Quest = new Quests.QuestTracker(this, quest);
        ValidateQuest(quest);
        NpcTalked += name => Quest.OnTalk(name);
        EnemyDefeated += name => Quest.OnDefeat(name);
        PlayerReached += id => Quest.OnReach(id);
        World.BlockChanged += OnWorldBlockChanged;
        World.EmitBlockChanges = true; // arm AFTER Build(): terrain is already laid down
        Quest.Begin();
    }

    /// <summary>Emit QuestCompleted (called by the tracker, which is not a Node itself).</summary>
    internal void NotifyQuestComplete() => EmitSignal(SignalName.QuestCompleted, LessonId ?? "");

    private void OnWorldBlockChanged(Vector3I pos, int oldId, int newId, int cause)
        => Quest?.OnBlockChanged((ushort)oldId, (ushort)newId);

    /// <summary>Warn loudly if a Defeat/Reach objective names a target that does not
    /// exist, so a typo fails visibly instead of an objective that never completes.</summary>
    private void ValidateQuest(Quests.Quest quest)
    {
        foreach (var o in quest.Objectives)
        {
            if (o.Kind == Quests.ObjectiveKind.Defeat && o.Key != null && !HasEnemyType(o.Key))
                GD.PushWarning($"[Quest] no spawned enemy of type '{o.Key}' for objective '{o.Label}'");
            if (o.Kind == Quests.ObjectiveKind.Reach && o.Key != null && !HasTrigger(o.Key))
                GD.PushWarning($"[Quest] no trigger with id '{o.Key}' for objective '{o.Label}'");
            if ((o.Kind == Quests.ObjectiveKind.Break || o.Kind == Quests.ObjectiveKind.Place
                 || o.Kind == Quests.ObjectiveKind.Collect)
                && (!BlockRegistry.TryId(o.Key, out ushort bid) || bid == 0))
                GD.PushWarning($"[Quest] block objective '{o.Label}' has no valid block name '{o.Key}'");
        }
    }

    private bool HasEnemyType(string typeName)
    {
        foreach (Node n in GetTree().GetNodesInGroup("enemy"))
            if (n is Enemy e && string.Equals(e.Type.Name, typeName, System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private bool HasTrigger(string id)
    {
        foreach (Node n in World.GetChildren())
            if (n is RAEngine.World.NarrationTrigger t && t.Id == id) return true;
        return false;
    }

    /// <summary>A landed hit flashes the screen red and kicks the camera, both scaled
    /// by the hit size. (SafeMode/Creative already suppress damage, so this never
    /// fires for a protected class.)</summary>
    private void OnPlayerHurt(float amount, string cause)
    {
        Hud.FlashHurt(Mathf.Clamp(0.35f + amount / 45f, 0.35f, 0.85f));
        Fx.Shake(Mathf.Clamp(amount / 25f, 0.12f, 0.5f));
    }

    public override void _ExitTree()
    {
        // Drop the static FX handlers that point at this session's camera/HUD, so the
        // app-wide Fx facade never calls into a freed node after the session ends.
        if (Fx.OnShake != null || Fx.OnFlash != null)
        {
            Fx.OnShake = null;
            Fx.OnFlash = null;
        }

        // Drop the world-block subscription explicitly (World is freed in the same pass,
        // but this keeps the tracker from being called into during teardown).
        if (World != null && GodotObject.IsInstanceValid(World))
            World.BlockChanged -= OnWorldBlockChanged;
    }
}
