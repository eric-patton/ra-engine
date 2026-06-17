using System.Collections.Generic;
using Godot;

namespace RAEngine.Combat;

/// <summary>A blocky character built from box primitives, with named limb pivots
/// so it can be animated procedurally — Minecraft-style sine-wave walk cycles,
/// attack swings, hit squash — without any external art, rig, or import. Origin
/// is at the feet. Construct via <see cref="MobModel"/>; drive via
/// <see cref="Animate"/> each physics frame plus <see cref="Attack"/>,
/// <see cref="Squash"/>, and <see cref="SetFlash"/> on events.</summary>
public partial class MobRig : Node3D, ICharacterModel
{
    public bool Beast;
    // Humanoid pivots (null on a beast). Each pivot sits at the joint; the box
    // mesh hangs below/forward so a local X rotation swings the limb naturally.
    public Node3D LeftLeg, RightLeg, LeftArm, RightArm, Head;
    // Beast pivots (null on a humanoid). Legs = [front-left, front-right, back-left, back-right]; front is -Z.
    public Node3D[] Legs;
    public Node3D Tail;

    // Squash/stretch happens on this inner node so it never fights the gameplay
    // scale (Type.Scale) or the defeat shrink, which both live on the rig root.
    private Node3D _visual;
    private readonly List<MeshInstance3D> _meshes = new();
    private Vector3 _headRest;
    private float _phase;   // walk-cycle accumulator — advances faster with speed, pauses when idle
    private float _idle;    // idle accumulator — always advances, so a standing mob still breathes
    private bool _attacking; // suppresses procedural drive of the swinging arm while an attack tween plays

    // ---- procedural animation -------------------------------------------

    /// <summary>Advance the walk/idle cycle. <paramref name="speed"/> is the mob's
    /// planar movement speed (m/s); 0 plays a gentle idle breath.</summary>
    public void Animate(float speed, float dt)
    {
        _idle += dt;
        float walk = Mathf.Clamp(speed / 3.5f, 0f, 1f);

        if (walk > 0.05f)
        {
            _phase += dt * (5f + walk * 5f);
            float s = Mathf.Sin(_phase);
            if (Beast)
            {
                float amp = walk * Mathf.DegToRad(28f);
                // diagonal gait: FL + BR together, FR + BL opposite
                SetPitch(Legs[0], s * amp); SetPitch(Legs[3], s * amp);
                SetPitch(Legs[1], -s * amp); SetPitch(Legs[2], -s * amp);
            }
            else
            {
                float leg = walk * Mathf.DegToRad(38f);
                float arm = walk * Mathf.DegToRad(30f);
                SetPitch(LeftLeg, s * leg); SetPitch(RightLeg, -s * leg);
                SetPitch(LeftArm, -s * arm);                 // arms counter-swing the legs
                if (!_attacking) SetPitch(RightArm, s * arm);
                if (Head != null) Head.Position = _headRest + new Vector3(0, Mathf.Abs(s) * walk * 0.03f, 0);
            }
        }
        else // idle: small breath so nobody is a frozen statue
        {
            float b = Mathf.Sin(_idle * 1.6f);
            if (Beast)
            {
                for (int i = 0; i < 4; i++) SetPitch(Legs[i], 0f);
            }
            else
            {
                float a = Mathf.DegToRad(4f) * b;
                SetPitch(LeftLeg, 0f); SetPitch(RightLeg, 0f);
                SetPitch(LeftArm, -a);
                if (!_attacking) SetPitch(RightArm, a);
                if (Head != null) Head.Position = _headRest + new Vector3(0, b * 0.01f, 0);
            }
        }

        if (Tail != null) Tail.Rotation = new Vector3(0, Mathf.Sin(_idle * 4f) * Mathf.DegToRad(12f), 0); // wag
    }

    /// <summary>Quick attack motion — the humanoid swings its right arm down; the
    /// beast snaps its head forward in a bite.</summary>
    public void Attack()
    {
        if (!IsInsideTree()) return;
        if (Beast)
        {
            if (Head == null) return;
            var t = CreateTween();
            t.TweenProperty(Head, "rotation", new Vector3(Mathf.DegToRad(35f), 0, 0), 0.08);
            t.TweenProperty(Head, "rotation", Vector3.Zero, 0.18);
            return;
        }
        if (RightArm == null) return;
        _attacking = true;
        var tw = CreateTween();
        tw.TweenProperty(RightArm, "rotation", new Vector3(Mathf.DegToRad(-95f), 0, 0), 0.08)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(RightArm, "rotation", Vector3.Zero, 0.22);
        tw.TweenCallback(Callable.From(() => _attacking = false));
    }

    /// <summary>A short squash-and-stretch pop on the visual, for a punchy hit.</summary>
    public void Squash()
    {
        if (_visual == null || !IsInsideTree()) return;
        _visual.Scale = Vector3.One;
        var t = CreateTween();
        t.TweenProperty(_visual, "scale", new Vector3(1.18f, 0.82f, 1.18f), 0.05);
        t.TweenProperty(_visual, "scale", Vector3.One, 0.12).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Toggle the red hit-flash emission across every box of the model.</summary>
    public void SetFlash(bool on)
    {
        Color col = on ? new Color(0.9f, 0.15f, 0.12f) : Colors.Black;
        foreach (MeshInstance3D mi in _meshes)
            if (mi.MaterialOverride is StandardMaterial3D m)
            {
                m.EmissionEnabled = on;
                m.Emission = col;
            }
    }

    private static void SetPitch(Node3D n, float radians)
    {
        if (n != null) n.Rotation = new Vector3(radians, 0, 0);
    }

    // ---- building -------------------------------------------------------

    internal void InitHumanoid(Color skin, Color cloth, Color accent)
    {
        Beast = false;
        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);

        LeftLeg = Pivot("LeftLeg", new Vector3(-0.16f, 0.8f, 0));
        LeftLeg.AddChild(Box(new Vector3(0.26f, 0.8f, 0.26f), new Vector3(0, -0.4f, 0), cloth));
        RightLeg = Pivot("RightLeg", new Vector3(0.16f, 0.8f, 0));
        RightLeg.AddChild(Box(new Vector3(0.26f, 0.8f, 0.26f), new Vector3(0, -0.4f, 0), cloth));

        _visual.AddChild(Box(new Vector3(0.62f, 0.72f, 0.36f), new Vector3(0, 1.16f, 0), accent)); // torso

        LeftArm = Pivot("LeftArm", new Vector3(-0.42f, 1.52f, 0));
        LeftArm.AddChild(Box(new Vector3(0.18f, 0.72f, 0.2f), new Vector3(0, -0.36f, 0), skin));
        RightArm = Pivot("RightArm", new Vector3(0.42f, 1.52f, 0));
        RightArm.AddChild(Box(new Vector3(0.18f, 0.72f, 0.2f), new Vector3(0, -0.36f, 0), skin));

        Head = Pivot("Head", new Vector3(0, 1.52f, 0));
        Head.AddChild(Box(new Vector3(0.46f, 0.46f, 0.46f), new Vector3(0, 0.23f, 0), skin));
        _headRest = Head.Position;
    }

    internal void InitBeast(Color fur, Color belly)
    {
        Beast = true;
        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);

        _visual.AddChild(Box(new Vector3(1.1f, 0.5f, 0.5f), new Vector3(0, 0.55f, 0), fur));     // body
        _visual.AddChild(Box(new Vector3(0.6f, 0.3f, 0.45f), new Vector3(0, 0.35f, 0), belly));  // belly

        Head = Pivot("Head", new Vector3(0, 0.65f, -0.4f));
        Head.AddChild(Box(new Vector3(0.45f, 0.4f, 0.4f), new Vector3(0, 0, -0.2f), fur));        // head (front = -Z)
        Head.AddChild(Box(new Vector3(0.18f, 0.18f, 0.2f), new Vector3(0, -0.15f, -0.45f), belly)); // snout

        var corners = new[] { (-0.4f, -0.4f), (0.4f, -0.4f), (-0.4f, 0.4f), (0.4f, 0.4f) }; // FL, FR, BL, BR
        Legs = new Node3D[4];
        for (int i = 0; i < 4; i++)
        {
            var (lx, lz) = corners[i];
            Legs[i] = Pivot($"Leg{i}", new Vector3(lx, 0.5f, lz));
            Legs[i].AddChild(Box(new Vector3(0.18f, 0.5f, 0.18f), new Vector3(0, -0.25f, 0), fur));
        }

        Tail = Pivot("Tail", new Vector3(0, 0.6f, 0.45f));
        Tail.AddChild(Box(new Vector3(0.14f, 0.14f, 0.5f), new Vector3(0, 0, 0.25f), fur));
        _headRest = Head.Position;
    }

    private Node3D Pivot(string name, Vector3 pos)
    {
        var n = new Node3D { Name = name, Position = pos };
        _visual.AddChild(n);
        return n;
    }

    private MeshInstance3D Box(Vector3 size, Vector3 pos, Color color)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            Position = pos,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f },
        };
        _meshes.Add(mi);
        return mi;
    }
}

/// <summary>Builds simple blocky <see cref="MobRig"/> characters so enemies and
/// NPCs are recognizable and animated without external art. Origin is at the feet.</summary>
public static class MobModel
{
    public static MobRig BuildHumanoid(Color skin, Color cloth, Color accent)
    {
        var rig = new MobRig { Name = "Model" };
        rig.InitHumanoid(skin, cloth, accent);
        return rig;
    }

    public static MobRig BuildBeast(Color fur, Color belly)
    {
        var rig = new MobRig { Name = "Model" };
        rig.InitBeast(fur, belly);
        return rig;
    }
}
