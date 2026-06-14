using System.Collections.Generic;
using Godot;
using RAEngine.Core;

namespace RAEngine.UI;

/// <summary>Bottom-of-screen block palette. Number keys 1-9 and the mouse wheel
/// change the selection; the selected block is what gets placed. Laid out
/// manually from the viewport size so it stays centred at any resolution.</summary>
public partial class Hotbar : Control
{
    private const int Slots = 9;
    private const int SlotSize = 60;
    private const int Gap = 6;

    private readonly List<ushort> _blocks = new();
    private readonly Panel[] _panels = new Panel[Slots];
    private readonly TextureRect[] _icons = new TextureRect[Slots];
    private Label _nameLabel;
    private int _selected;
    private BlockTextures _tex;

    public ushort SelectedBlockId => _selected < _blocks.Count ? _blocks[_selected] : (ushort)0;

    public void Init(BlockTextures tex, IEnumerable<ushort> blocks)
    {
        _tex = tex;
        _blocks.Clear();
        _blocks.AddRange(blocks);
        Build();
        Select(0);
    }

    private bool _connected;

    private void Build()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        foreach (Node c in GetChildren()) c.QueueFree(); // support reconfigure without leaking

        for (int i = 0; i < Slots; i++)
        {
            var panel = new Panel { CustomMinimumSize = new Vector2(SlotSize, SlotSize), Size = new Vector2(SlotSize, SlotSize) };
            panel.AddThemeStyleboxOverride("panel", SlotStyle(false));
            AddChild(panel);
            _panels[i] = panel;

            var icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            };
            icon.SetAnchorsPreset(LayoutPreset.FullRect);
            icon.OffsetLeft = 6; icon.OffsetTop = 6; icon.OffsetRight = -6; icon.OffsetBottom = -6;
            icon.MouseFilter = MouseFilterEnum.Ignore;
            panel.AddChild(icon);
            _icons[i] = icon;

            var num = new Label { Text = (i + 1).ToString(), Position = new Vector2(5, 2) };
            num.AddThemeFontSizeOverride("font_size", 12);
            num.Modulate = new Color(1, 1, 1, 0.6f);
            panel.AddChild(num);

            if (i < _blocks.Count && _tex != null)
                _icons[i].Texture = _tex.GetIcon(BlockRegistry.Get(_blocks[i]));
        }

        _nameLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _nameLabel.AddThemeFontSizeOverride("font_size", 18);
        _nameLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _nameLabel.AddThemeConstantOverride("outline_size", 4);
        _nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_nameLabel);

        if (!_connected) { GetViewport().SizeChanged += Relayout; _connected = true; }
        Relayout();
    }

    private void Relayout()
    {
        Vector2 vp = GetViewportRect().Size;
        int totalW = Slots * SlotSize + (Slots - 1) * Gap;
        float startX = (vp.X - totalW) / 2f;
        float y = vp.Y - SlotSize - 28;
        for (int i = 0; i < Slots; i++)
            _panels[i].Position = new Vector2(startX + i * (SlotSize + Gap), y);
        if (_nameLabel != null)
        {
            _nameLabel.Position = new Vector2(0, y - 30);
            _nameLabel.Size = new Vector2(vp.X, 24);
        }
    }

    private static StyleBoxFlat SlotStyle(bool selected)
    {
        var s = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.10f, 0.66f),
            BorderColor = selected ? new Color(1f, 0.92f, 0.5f) : new Color(0.6f, 0.6f, 0.65f, 0.7f),
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
        };
        int b = selected ? 4 : 2;
        s.BorderWidthLeft = s.BorderWidthRight = s.BorderWidthTop = s.BorderWidthBottom = b;
        return s;
    }

    public void Select(int index)
    {
        int count = Mathf.Min(Slots, _blocks.Count); // only cycle through visible slots
        if (count == 0) return;
        index = ((index % count) + count) % count;
        if (_selected < Slots) _panels[_selected].AddThemeStyleboxOverride("panel", SlotStyle(false));
        _selected = index;
        if (_selected < Slots) _panels[_selected].AddThemeStyleboxOverride("panel", SlotStyle(true));
        _nameLabel.Text = BlockRegistry.Get(SelectedBlockId).DisplayName;
    }

    public void Next() => Select(_selected + 1);
    public void Prev() => Select(_selected - 1);

    public override void _ExitTree()
    {
        if (_connected) { GetViewport().SizeChanged -= Relayout; _connected = false; }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(GameInput.Actions.HotbarNext)) Next();
        else if (e.IsActionPressed(GameInput.Actions.HotbarPrev)) Prev();
        else
        {
            for (int i = 0; i < Slots && i < _blocks.Count; i++)
                if (e.IsActionPressed($"hotbar_{i + 1}")) { Select(i); return; }
        }
    }
}
