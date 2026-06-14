using Godot;
using RAEngine.Dialogue;

namespace RAEngine.UI;

/// <summary>Modal conversation panel. Advances on Space/Enter/click when a line
/// has no choices; otherwise shows clickable choice buttons. Large, high-contrast
/// text for a mixed-age audience.</summary>
public partial class DialogueBox : CanvasLayer
{
    [Signal] public delegate void FinishedEventHandler();

    private Control _root;
    private Panel _panel;
    private Label _speaker;
    private Label _text;
    private Label _continue;
    private VBoxContainer _choices;
    private DialogueData _data;
    private DialogueLine _current;
    private readonly System.Collections.Generic.HashSet<string> _visited = new();
    public bool Active { get; private set; }

    public override void _Ready()
    {
        Layer = 5;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _panel = new Panel();
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.06f, 0.09f, 0.92f),
            BorderColor = new Color(0.9f, 0.8f, 0.45f, 0.9f),
            BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3,
            CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10, CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 16, ContentMarginBottom = 16,
        });
        _root.AddChild(_panel);

        _speaker = new Label();
        _speaker.AddThemeFontSizeOverride("font_size", 22);
        _speaker.AddThemeColorOverride("font_color", new Color(1f, 0.88f, 0.5f));
        _panel.AddChild(_speaker);

        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _text.AddThemeFontSizeOverride("font_size", 20);
        _panel.AddChild(_text);

        _choices = new VBoxContainer();
        _choices.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(_choices);

        _continue = new Label { Text = "▼  Space / Click", HorizontalAlignment = HorizontalAlignment.Right };
        _continue.AddThemeFontSizeOverride("font_size", 14);
        _continue.Modulate = new Color(1, 1, 1, 0.6f);
        _panel.AddChild(_continue);

        Visible = false;
        GetViewport().SizeChanged += Relayout;
        Relayout();
    }

    private void Relayout()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        float margin = 48, h = 200;
        _panel.Position = new Vector2(margin, vp.Y - h - 30);
        _panel.Size = new Vector2(vp.X - margin * 2, h);
        float inner = _panel.Size.X - 44;
        _speaker.Position = new Vector2(22, 14);
        _text.Position = new Vector2(22, 50);
        _text.Size = new Vector2(inner, 90);
        _choices.Position = new Vector2(30, 92);
        _choices.Size = new Vector2(inner - 16, 90);
        _continue.Position = new Vector2(22, h - 30);
        _continue.Size = new Vector2(inner, 20);
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Relayout;
    }

    public void StartDialogue(DialogueData data)
    {
        if (data == null) { EmitSignal(SignalName.Finished); return; }
        _data = data;
        Active = true;
        Visible = true;
        _visited.Clear();
        ShowNode(data.Start);
    }

    private void ShowNode(string id)
    {
        _current = _data.Get(id);
        if (_current == null) { Finish(); return; }
        if (!_visited.Add(id))
        {
            // A Next-chain looped back on itself — end rather than trap the player
            // with input locked. (Choice navigation clears the trail, so menus that
            // legitimately revisit a hub node still work.)
            GD.PushWarning($"[Dialogue] cycle detected at node '{id}'; ending to avoid a lockup.");
            Finish();
            return;
        }

        _speaker.Text = _current.Speaker;
        _text.Text = _current.Text;

        foreach (Node c in _choices.GetChildren()) c.QueueFree();
        bool hasChoices = _current.Choices is { Count: > 0 };
        if (hasChoices)
        {
            foreach (var choice in _current.Choices)
            {
                var b = new Button { Text = "• " + choice.Text };
                b.AddThemeFontSizeOverride("font_size", 18);
                string next = choice.Next;
                b.Pressed += () => { _visited.Clear(); ShowNode(next); };
                _choices.AddChild(b);
            }
        }
        _continue.Visible = !hasChoices;
    }

    public override void _Input(InputEvent e)
    {
        if (!Active) return;
        if (_current?.Choices is { Count: > 0 }) return; // must pick a choice

        bool advance = e.IsActionPressed(Core.GameInput.Actions.Jump)
            || (e is InputEventKey k && k.Pressed && (k.Keycode == Key.Enter || k.Keycode == Key.Space))
            || (e is InputEventMouseButton m && m.Pressed && m.ButtonIndex == MouseButton.Left);
        if (advance)
        {
            GetViewport().SetInputAsHandled();
            Advance();
        }
    }

    /// <summary>Advance past the current line (no-op while choices are pending).
    /// Public so scripts and tests can drive a conversation.</summary>
    public void Advance()
    {
        if (!Active || _current == null) return;
        if (_current.Choices is { Count: > 0 }) return;
        if (string.IsNullOrEmpty(_current.Next)) Finish();
        else ShowNode(_current.Next);
    }

    public bool HasChoices => _current?.Choices is { Count: > 0 };

    /// <summary>Pick the i-th choice of the current line (for scripts/tests).</summary>
    public void Choose(int i)
    {
        if (!Active || _current?.Choices is not { } cs || i < 0 || i >= cs.Count) return;
        _visited.Clear(); // an explicit choice is a fresh branch (hubs may revisit)
        ShowNode(cs[i].Next);
    }

    private void Finish()
    {
        Active = false;
        Visible = false;
        EmitSignal(SignalName.Finished);
    }
}
