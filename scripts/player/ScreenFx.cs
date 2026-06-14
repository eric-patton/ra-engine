using Godot;
using RAEngine.Core;

namespace RAEngine.PlayerSys;

/// <summary>Trauma-based camera shake. Each frame it rewrites the camera's *local*
/// offset and roll from smooth noise scaled by trauma², then decays the trauma.
/// Because it only touches the Camera's own Position/Rotation (mouse-look drives
/// the Head, not the Camera), shaking never fights the player's aim. Trauma is
/// added in one-line bursts via <see cref="Fx.Shake"/> (mining, landings, hits).</summary>
public partial class ScreenFx : Node
{
    public Camera3D Camera;

    private const float MaxOffset = 0.16f; // metres of camera translation at full trauma
    private const float MaxRoll = 0.045f;  // radians of camera roll at full trauma
    private const float Decay = 1.8f;      // trauma units shed per second

    private float _trauma;
    private float _t;
    private readonly ValueNoise2D _noise = new(1337);

    /// <summary>Add shake intensity (0..1, accumulates and clamps).</summary>
    public void AddTrauma(float amount) => _trauma = Mathf.Clamp(_trauma + amount, 0f, 1f);

    public override void _Process(double delta)
    {
        if (Camera == null) return;
        if (_trauma <= 0f)
        {
            // settle exactly to neutral so a finished shake leaves no residual offset
            if (Camera.Position != Vector3.Zero) Camera.Position = Vector3.Zero;
            if (Camera.Rotation != Vector3.Zero) Camera.Rotation = Vector3.Zero;
            return;
        }

        float dt = (float)delta;
        _t += dt;
        float shake = _trauma * _trauma; // quadratic feels punchier than linear

        // Smooth, decorrelated noise per axis in [-1,1].
        float nx = _noise.Noise(_t * 30f, 0f) * 2f - 1f;
        float ny = _noise.Noise(0f, _t * 30f + 17f) * 2f - 1f;
        float nr = _noise.Noise(_t * 27f + 50f, _t * 27f) * 2f - 1f;

        Camera.Position = new Vector3(nx * MaxOffset * shake, ny * MaxOffset * shake, 0f);
        Camera.Rotation = new Vector3(0f, 0f, nr * MaxRoll * shake);

        _trauma = Mathf.MoveToward(_trauma, 0f, Decay * dt);
    }
}
