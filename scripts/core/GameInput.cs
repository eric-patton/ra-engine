using Godot;

namespace RAEngine.Core;

/// <summary>Registers all input actions at runtime so the project works without
/// hand-editing project.godot. Keys use physical scancodes (layout-independent).</summary>
public static class GameInput
{
    private static bool _done;

    public static void Setup()
    {
        if (_done) return;
        _done = true;

        Key(Actions.Forward, Godot.Key.W);
        Key(Actions.Back, Godot.Key.S);
        Key(Actions.Left, Godot.Key.A);
        Key(Actions.Right, Godot.Key.D);
        Key(Actions.Jump, Godot.Key.Space);
        Key(Actions.Sprint, Godot.Key.Shift);
        Key(Actions.Crouch, Godot.Key.Ctrl);
        Key(Actions.Interact, Godot.Key.E);
        Key(Actions.Pause, Godot.Key.Escape);
        Key(Actions.ToggleMode, Godot.Key.G);
        Key(Actions.Inventory, Godot.Key.Tab);

        Mouse(Actions.Primary, MouseButton.Left);
        Mouse(Actions.Secondary, MouseButton.Right);
        Wheel(Actions.HotbarNext, MouseButton.WheelDown);
        Wheel(Actions.HotbarPrev, MouseButton.WheelUp);

        // Keyboard camera look (so the game is playable without a mouse). Arrow
        // keys work on every keyboard; the numpad mirror suits a full keyboard.
        Key(Actions.LookUp, Godot.Key.Up);
        Key(Actions.LookUp, Godot.Key.Kp8);
        Key(Actions.LookDown, Godot.Key.Down);
        Key(Actions.LookDown, Godot.Key.Kp2);
        Key(Actions.LookLeft, Godot.Key.Left);
        Key(Actions.LookLeft, Godot.Key.Kp4);
        Key(Actions.LookRight, Godot.Key.Right);
        Key(Actions.LookRight, Godot.Key.Kp6);

        // Keyboard place/break (the "primary/secondary" action without a mouse).
        // Numpad +/- are mnemonic (add/remove a block); ,/. work on laptops.
        Key(Actions.KbBreak, Godot.Key.KpSubtract);
        Key(Actions.KbBreak, Godot.Key.Comma);
        Key(Actions.KbPlace, Godot.Key.KpAdd);
        Key(Actions.KbPlace, Godot.Key.Period);

        // Grab/release the mouse on demand (works in every capture mode).
        Key(Actions.ToggleCapture, Godot.Key.M);

        for (int i = 0; i < 9; i++)
            Key($"hotbar_{i + 1}", (Key)((int)Godot.Key.Key1 + i));

        // level editor
        Key(Actions.EditorSave, Godot.Key.F5);
        Key(Actions.EditorLoad, Godot.Key.F9);
        Key(Actions.MarkA, Godot.Key.Z);
        Key(Actions.MarkB, Godot.Key.X);
        Key(Actions.FillRegion, Godot.Key.F);
        Key(Actions.ClearRegion, Godot.Key.R);
        Key(Actions.Capture, Godot.Key.C);
        Key(Actions.Stamp, Godot.Key.V);
        Key(Actions.ToggleBuild, Godot.Key.B);

        // teacher tools
        Key(Actions.Teacher, Godot.Key.F1);
        Key(Actions.Screenshot, Godot.Key.F2);
        Key(Actions.Debug, Godot.Key.F3);
    }

    private static void Ensure(string action)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
    }

    private static void Key(string action, Key key)
    {
        Ensure(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    private static void Mouse(string action, MouseButton button)
    {
        Ensure(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }

    private static void Wheel(string action, MouseButton button)
    {
        Ensure(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }

    public static class Actions
    {
        public const string Forward = "move_forward";
        public const string Back = "move_back";
        public const string Left = "move_left";
        public const string Right = "move_right";
        public const string Jump = "jump";
        public const string Sprint = "sprint";
        public const string Crouch = "crouch";
        public const string Primary = "primary";
        public const string Secondary = "secondary";
        public const string Interact = "interact";
        public const string Pause = "pause";
        public const string ToggleMode = "toggle_mode";
        public const string Inventory = "inventory";
        public const string HotbarNext = "hotbar_next";
        public const string HotbarPrev = "hotbar_prev";
        public const string LookUp = "look_up";
        public const string LookDown = "look_down";
        public const string LookLeft = "look_left";
        public const string LookRight = "look_right";
        public const string KbBreak = "kb_break";
        public const string KbPlace = "kb_place";
        public const string ToggleCapture = "toggle_capture";
        public const string EditorSave = "editor_save";
        public const string EditorLoad = "editor_load";
        public const string MarkA = "editor_mark_a";
        public const string MarkB = "editor_mark_b";
        public const string FillRegion = "editor_fill";
        public const string ClearRegion = "editor_clear";
        public const string Capture = "editor_capture";
        public const string Stamp = "editor_stamp";
        public const string ToggleBuild = "toggle_build";
        public const string Teacher = "teacher";
        public const string Screenshot = "screenshot";
        public const string Debug = "debug_hud";
    }
}
