using Godot;

namespace RAEngine.Combat;

/// <summary>A small billboarded health bar that floats above a mob. Hidden while
/// at full health; turns from green to red as health drops.</summary>
public partial class HealthBar3D : Node3D
{
    private MeshInstance3D _fill;
    private StandardMaterial3D _fillMat;
    private const float Width = 1.0f;

    public override void _Ready()
    {
        var bg = MakeQuad(Width + 0.06f, 0.18f, new Color(0, 0, 0, 0.7f), 0);
        AddChild(bg);
        _fill = MakeQuad(Width, 0.12f, new Color(0.2f, 0.85f, 0.25f), 1);
        _fillMat = (StandardMaterial3D)_fill.MaterialOverride;
        AddChild(_fill);
        Visible = false;
    }

    private static MeshInstance3D MakeQuad(float w, float h, Color color, int priority)
    {
        var mi = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(w, h) } };
        mi.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = color,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            Transparency = color.A < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            NoDepthTest = true,
            RenderPriority = priority,
            DisableReceiveShadows = true,
        };
        return mi;
    }

    public void SetFraction(float f)
    {
        f = Mathf.Clamp(f, 0f, 1f);
        Visible = f < 0.999f && f > 0f;
        _fill.Scale = new Vector3(f, 1, 1);
        _fill.Position = new Vector3(-(1 - f) * Width * 0.5f, 0, 0.001f);
        _fillMat.AlbedoColor = f > 0.5f
            ? new Color(0.2f, 0.85f, 0.25f).Lerp(new Color(0.95f, 0.85f, 0.2f), (1 - f) * 2f)
            : new Color(0.95f, 0.85f, 0.2f).Lerp(new Color(0.9f, 0.2f, 0.15f), (0.5f - f) * 2f);
    }
}
