using System.Collections.Generic;
using Godot;

namespace RAEngine.Combat;

/// <summary>Wraps an imported rigged glTF character so it can be driven by the same
/// gameplay code as the procedural <see cref="MobRig"/>. It auto-detects idle/walk/attack
/// clips by name, forces idle/walk to loop, flashes via per-instance material overrides
/// (so one mob flashing doesn't tint them all), and pops a quick squash on hit.</summary>
public partial class RiggedModel : Node3D, ICharacterModel
{
    private Node3D _inner;                 // the instantiated glTF scene root
    private AnimationPlayer _anim;
    private readonly List<StandardMaterial3D> _flashMats = new();
    private string _idle, _walk, _attack;
    private string _current;               // locomotion clip currently playing
    private bool _attacking;

    /// <summary>Extra yaw (degrees) applied to the imported model so its front matches the
    /// box model's (-Z). glTF characters often import facing the opposite way; pass 180 to
    /// flip. Tune once per asset and pass it via the archetype's ModelYawDeg.</summary>
    public float FacingOffsetDeg
    {
        set { if (_inner != null) _inner.Rotation = new Vector3(0, Mathf.DegToRad(value), 0); }
    }

    /// <summary>Load + instantiate a rigged model scene. Returns null (so callers fall back
    /// to the procedural model) when the path is empty, missing, unloadable, or the scene
    /// has no AnimationPlayer.</summary>
    public static RiggedModel TryLoad(string scenePath, float facingDeg = 0f)
    {
        if (string.IsNullOrEmpty(scenePath)) return null;
        if (!ResourceLoader.Exists(scenePath))
        {
            GD.PushWarning($"[RA] model scene not found, using box model instead: {scenePath}");
            return null;
        }
        var root = ResourceLoader.Load<PackedScene>(scenePath)?.Instantiate<Node3D>();
        if (root == null) return null;
        var m = new RiggedModel { Name = "Model" };
        if (!m.Init(root)) { root.QueueFree(); m.QueueFree(); return null; }
        m.FacingOffsetDeg = facingDeg;
        return m;
    }

    /// <summary>Adopt an already-instantiated character root (used by <see cref="TryLoad"/>
    /// and by tests). Returns false if it has no AnimationPlayer, so the caller falls back.</summary>
    public bool Init(Node3D root)
    {
        _inner = root;
        AddChild(_inner);
        _anim = FindAnim(_inner);
        if (_anim == null) return false; // not a rigged character — caller uses the box model

        CollectFlashMaterials(_inner);
        var clips = new List<string>(_anim.GetAnimationList());
        _idle = Pick(clips, "idle") ?? (clips.Count > 0 ? clips[0] : null);
        _walk = Pick(clips, "walk", "run", "move") ?? _idle;
        _attack = Pick(clips, "attack", "melee", "swing", "punch", "chop", "slash") ?? _idle;
        ForceLoop(_idle);
        ForceLoop(_walk);
        if (_idle != null) { _anim.Play(_idle); _current = _idle; }
        return true;
    }

    public void Animate(float speed, float dt)
    {
        if (_anim == null || _attacking) return;
        string want = speed > 0.6f ? _walk : _idle;
        if (want != null && want != _current) { _anim.Play(want); _current = want; }
    }

    public void Attack()
    {
        if (_anim == null || _attack == null || _attacking) return;
        _attacking = true;
        _anim.AnimationFinished += OnAttackFinished;
        _anim.Play(_attack);
    }

    private void OnAttackFinished(StringName which)
    {
        if ((string)which != _attack) return;
        _anim.AnimationFinished -= OnAttackFinished;
        _attacking = false;
        _current = null; // force Animate to re-issue idle/walk next frame
    }

    public void Squash()
    {
        if (_inner == null || !IsInsideTree()) return;
        _inner.Scale = Vector3.One;
        var t = CreateTween();
        t.TweenProperty(_inner, "scale", new Vector3(1.12f, 0.88f, 1.12f), 0.05);
        t.TweenProperty(_inner, "scale", Vector3.One, 0.12).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    public void SetFlash(bool on)
    {
        Color c = on ? new Color(0.9f, 0.15f, 0.12f) : Colors.Black;
        foreach (StandardMaterial3D m in _flashMats)
        {
            m.EmissionEnabled = on;
            m.Emission = c;
        }
    }

    // ---- helpers ----------------------------------------------------------

    private static AnimationPlayer FindAnim(Node n)
    {
        if (n is AnimationPlayer ap) return ap;
        foreach (Node c in n.GetChildren())
        {
            var f = FindAnim(c);
            if (f != null) return f;
        }
        return null;
    }

    /// <summary>Give each mesh surface its own duplicated StandardMaterial3D override so
    /// the hit-flash is per-instance (surfaces whose material isn't a StandardMaterial3D
    /// keep their original material and simply don't flash).</summary>
    private void CollectFlashMaterials(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                if (mi.GetActiveMaterial(s) is StandardMaterial3D src)
                {
                    var mat = (StandardMaterial3D)src.Duplicate();
                    mi.SetSurfaceOverrideMaterial(s, mat);
                    _flashMats.Add(mat);
                }
        }
        foreach (Node c in n.GetChildren()) CollectFlashMaterials(c);
    }

    private static string Pick(List<string> clips, params string[] keys)
    {
        foreach (string k in keys)
            foreach (string c in clips)
                if (c.ToLower().Contains(k)) return c;
        return null;
    }

    private void ForceLoop(string clip)
    {
        if (clip == null) return;
        var a = _anim.GetAnimation(clip);
        if (a != null) a.LoopMode = Animation.LoopModeEnum.Linear;
    }
}
