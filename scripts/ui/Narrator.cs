using System.Collections.Generic;
using Godot;

namespace RAEngine.UI;

/// <summary>Top-of-screen narration overlay with a queue and fade. Styled like a
/// storyteller's caption, distinct from character dialogue.</summary>
public partial class Narrator : CanvasLayer
{
    private readonly Queue<(string text, float dur)> _queue = new();
    private Panel _panel;
    private Label _label;
    private Timer _timer;
    private bool _showing;

    public override void _Ready()
    {
        Layer = 4;
        _panel = new Panel();
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.07f, 0.05f, 0.82f),
            BorderColor = new Color(0.85f, 0.78f, 0.6f, 0.7f),
            BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _label.AddThemeFontSizeOverride("font_size", 22);
        _label.AddThemeColorOverride("font_color", new Color(0.97f, 0.93f, 0.82f));
        _panel.AddChild(_label);
        _panel.Modulate = new Color(1, 1, 1, 0);
        _panel.Visible = false;

        _timer = new Timer { OneShot = true };
        AddChild(_timer);
        _timer.Timeout += Next;

        GetViewport().SizeChanged += Relayout;
        Relayout();
    }

    private void Relayout()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        float w = Mathf.Min(900, vp.X - 80);
        _panel.Size = new Vector2(w, 90);
        _panel.Position = new Vector2((vp.X - w) / 2f, 40);
        _label.Position = new Vector2(20, 12);
        _label.Size = new Vector2(w - 40, 66);
    }

    /// <summary>Queue a narration line. Duration scales with length if &lt;= 0.</summary>
    public void Show(string text, float duration = 0f)
    {
        if (duration <= 0f) duration = Mathf.Clamp(text.Length * 0.06f, 2.5f, 8f);
        _queue.Enqueue((text, duration));
        if (!_showing) Next();
    }

    public void ShowMany(IEnumerable<string> lines)
    {
        foreach (var l in lines) Show(l);
    }

    public void Clear()
    {
        _queue.Clear();
        _timer.Stop();
        _showing = false;
        _panel.Visible = false;
    }

    private void Next()
    {
        if (_queue.Count == 0)
        {
            _showing = false;
            var t = CreateTween();
            t.TweenProperty(_panel, "modulate:a", 0f, 0.3f);
            t.TweenCallback(Callable.From(() => _panel.Visible = false));
            return;
        }
        _showing = true;
        var (text, dur) = _queue.Dequeue();
        _label.Text = text;
        _panel.Visible = true;
        var tw = CreateTween();
        tw.TweenProperty(_panel, "modulate:a", 1f, 0.3f);
        _timer.Start(dur);
    }
}
