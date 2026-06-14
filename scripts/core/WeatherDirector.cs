using Godot;

namespace RAEngine.Core;

/// <summary>Chooses weather for the streamed sandbox from the biome under the
/// player: snow in cold/high country, clear in the desert, and the odd rain
/// shower elsewhere (on a slow deterministic schedule). Lessons leave the weather
/// clear unless they set it themselves.</summary>
public sealed partial class WeatherDirector : Node
{
    public TerrainGenerator Generator;
    public Node3D Player;
    public EnvironmentController Env;

    private float _clock;
    private readonly ValueNoise2D _schedule = new(4242);

    public override void _Process(double delta)
    {
        if (Generator == null || Player == null || Env == null) return;
        _clock += (float)delta;
        if (_clock < 1.5f) return;
        _clock = 0f;

        Vector3 p = Player.GlobalPosition;
        int x = Mathf.FloorToInt(p.X), z = Mathf.FloorToInt(p.Z);
        Biome biome = Generator.BiomeAt(x, z, Generator.SurfaceHeight(x, z));

        // A slow 0..1 wave that turns showers on and off over a few minutes.
        float wet = _schedule.Noise((float)(Time.GetTicksMsec() / 1000.0) * 0.012f, 0f);

        Weather w = biome switch
        {
            Biome.Snow or Biome.Mountains => Weather.Snow,
            Biome.Desert => Weather.Clear,
            _ => wet > 0.62f ? Weather.Rain : Weather.Clear,
        };
        Env.SetWeather(w);
    }
}
