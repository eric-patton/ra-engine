using System.Collections.Generic;
using Godot;
using RAEngine.Core;

namespace RAEngine.UI;

/// <summary>A simple fixed-recipe crafting screen (opened with Tab). Each row shows
/// the output, the ingredients with how many you have, and a Craft button that is
/// enabled only when the recipe is affordable. Crafting flows straight into the
/// inventory, so the hotbar updates immediately.</summary>
public partial class CraftingMenu : CanvasLayer
{
    public System.Action OnClose;

    private Inventory _inv;
    private BlockTextures _tex;
    private Control _root;
    private readonly List<RowUi> _rows = new();

    private struct RowUi
    {
        public Recipe Recipe;
        public Button Craft;
        public Label Needs;
    }

    public void Setup(Inventory inv, BlockTextures tex)
    {
        _inv = inv;
        _tex = tex;
        _inv.Changed += Refresh;
    }

    public override void _Ready()
    {
        Layer = 18;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;

        _root = UiKit.Dim(new Color(0.04f, 0.05f, 0.08f, 0.92f));
        AddChild(_root);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        center.AddChild(box);

        box.AddChild(UiKit.Title("Crafting", 40, UiKit.Gold));
        var hint = new Label { Text = "Break blocks to gather materials, then craft.", HorizontalAlignment = HorizontalAlignment.Center };
        hint.AddThemeFontSizeOverride("font_size", 16);
        hint.Modulate = new Color(1, 1, 1, 0.7f);
        box.AddChild(hint);

        foreach (var r in CraftBook.All)
            box.AddChild(BuildRow(r));

        var back = UiKit.Button("Close  (Tab)");
        back.Pressed += Close;
        box.AddChild(back);
    }

    private Control BuildRow(Recipe r)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(560, 56) };
        row.AddThemeConstantOverride("separation", 12);

        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(48, 48),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Texture = _tex?.GetIcon(BlockRegistry.Get(r.Output.block)),
        };
        row.AddChild(icon);

        var info = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        var title = new Label { Text = $"{r.Output.count}× {BlockRegistry.Get(r.Output.block).DisplayName}" };
        title.AddThemeFontSizeOverride("font_size", 20);
        info.AddChild(title);
        var needs = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        needs.AddThemeFontSizeOverride("font_size", 15);
        info.AddChild(needs);
        row.AddChild(info);

        var craft = UiKit.Button("Craft", 18);
        craft.CustomMinimumSize = new Vector2(120, 46);
        craft.Pressed += () =>
        {
            if (_inv.Craft(r)) AudioManager.Play("place");
            else AudioManager.Play("talk");
        };
        row.AddChild(craft);

        _rows.Add(new RowUi { Recipe = r, Craft = craft, Needs = needs });
        return row;
    }

    public void Open()
    {
        Visible = true;
        Refresh();
    }

    public void Close()
    {
        Visible = false;
        OnClose?.Invoke();
    }

    public override void _ExitTree()
    {
        if (_inv != null) _inv.Changed -= Refresh;
    }

    private void Refresh()
    {
        if (!Visible || _inv == null) return;
        foreach (var row in _rows)
        {
            var sb = new System.Text.StringBuilder("Needs: ");
            for (int i = 0; i < row.Recipe.Inputs.Length; i++)
            {
                var (block, count) = row.Recipe.Inputs[i];
                int have = _inv.Count(BlockRegistry.IdOf(block));
                if (i > 0) sb.Append(",  ");
                sb.Append($"{count} {BlockRegistry.Get(block).DisplayName} ({have})");
            }
            row.Needs.Text = sb.ToString();
            bool can = _inv.CanAfford(row.Recipe);
            row.Craft.Disabled = !can;
            row.Needs.Modulate = can ? new Color(0.7f, 1f, 0.7f) : new Color(1f, 0.7f, 0.7f);
        }
    }
}
