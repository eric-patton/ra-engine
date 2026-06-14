using System.Collections.Generic;
using Godot;
using RAEngine.Core;

namespace RAEngine.UI;

/// <summary>Bottom-of-screen block bar. Number keys 1-9 and the mouse wheel change
/// the selection; the selected block is what gets placed. Works in two modes:
/// a fixed palette (creative/editor, no counts) or bound to an
/// <see cref="Inventory"/> (survival sandbox), where slots track the blocks the
/// player has gathered and show stack counts. Laid out from the viewport size so
/// it stays centred at any resolution.</summary>
public partial class Hotbar : Control
{
    private const int Slots = 9;
    private const int SlotSize = 60;
    private const int Gap = 6;

    private readonly List<ushort> _blocks = new();
    private readonly Panel[] _panels = new Panel[Slots];
    private readonly TextureRect[] _icons = new TextureRect[Slots];
    private readonly Label[] _countLabels = new Label[Slots];
    private Label _nameLabel;
    private int _selected;
    private BlockTextures _tex;
    private Inventory _inventory;
    private bool _built;

    public ushort SelectedBlockId => _selected < _blocks.Count ? _blocks[_selected] : (ushort)0;

    /// <summary>Fixed-palette mode (creative / level editor): infinite blocks, no counts.</summary>
    public void Init(BlockTextures tex, IEnumerable<ushort> blocks)
    {
        _tex = tex;
        _inventory = null;
        _blocks.Clear();
        _blocks.AddRange(blocks);
        EnsureBuilt();
        RefreshSlots();
        Select(0);
    }

    /// <summary>Inventory mode (survival sandbox): slots mirror gathered blocks and
    /// show stack counts; the bar updates whenever the inventory changes.</summary>
    public void BindInventory(BlockTextures tex, Inventory inv)
    {
        _tex = tex;
        _inventory = inv;
        inv.Changed += RefreshFromInventory;
        EnsureBuilt();
        RefreshFromInventory();
        Select(0);
    }

    private bool _connected;

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

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

            var count = new Label
            {
                Visible = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                Size = new Vector2(SlotSize - 8, 18),
                Position = new Vector2(4, SlotSize - 22),
            };
            count.AddThemeFontSizeOverride("font_size", 16);
            count.AddThemeColorOverride("font_outline_color", Colors.Black);
            count.AddThemeConstantOverride("outline_size", 3);
            count.MouseFilter = MouseFilterEnum.Ignore;
            panel.AddChild(count);
            _countLabels[i] = count;
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

    private void RefreshFromInventory()
    {
        _blocks.Clear();
        for (int i = 0; i < _inventory.Order.Count && i < Slots; i++)
            _blocks.Add(_inventory.Order[i]);
        RefreshSlots();
        if (_selected >= _blocks.Count) _selected = Mathf.Max(0, _blocks.Count - 1);
        Select(_selected);
    }

    private void RefreshSlots()
    {
        for (int i = 0; i < Slots; i++)
        {
            bool has = i < _blocks.Count;
            _icons[i].Texture = has && _tex != null ? _tex.GetIcon(BlockRegistry.Get(_blocks[i])) : null;
            if (_inventory != null && has)
            {
                _countLabels[i].Text = _inventory.Count(_blocks[i]).ToString();
                _countLabels[i].Visible = true;
            }
            else _countLabels[i].Visible = false;
        }
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
        int count = Mathf.Min(Slots, _blocks.Count); // only cycle through filled slots
        if (count == 0)
        {
            if (_nameLabel != null) _nameLabel.Text = "";
            return;
        }
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
        if (_inventory != null) _inventory.Changed -= RefreshFromInventory;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(GameInput.Actions.HotbarNext)) { Next(); AudioManager.Play("select"); }
        else if (e.IsActionPressed(GameInput.Actions.HotbarPrev)) { Prev(); AudioManager.Play("select"); }
        else
        {
            for (int i = 0; i < Slots && i < _blocks.Count; i++)
                if (e.IsActionPressed($"hotbar_{i + 1}")) { Select(i); AudioManager.Play("select"); return; }
        }
    }
}
