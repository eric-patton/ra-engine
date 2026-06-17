// Cross-quad grass tufts with a gentle, position-phased wind sway. The top of
// each blade moves more than the base (sway scales with local height), and the
// alpha-cut texture gives the tuft its shape. `sway_amount` (driven by the live
// wind strength) sets the amplitude and `wind_dir` the bend direction, so a calm
// day barely trembles while a storm lays the blades over downwind in unison.
shader_type spatial;
render_mode cull_disabled, depth_prepass_alpha;

uniform sampler2D tuft : source_color, filter_linear_mipmap;
uniform float sway_amount = 0.10;       // overall amplitude (0 calm .. ~0.3 storm)
uniform vec2 wind_dir = vec2(1.0, 0.0); // normalized horizontal wind direction (x, z)

void vertex() {
    vec3 wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float ph = wpos.x * 0.6 + wpos.z * 0.6;
    // A fast flutter rides a slow gust swell, biased positive so a steady wind bends
    // the blade DOWNWIND (along wind_dir) rather than waving symmetrically about upright.
    float flutter = sin(TIME * 1.7 + ph) + 0.4 * sin(TIME * 2.9 + ph * 1.7);
    float gust = 0.6 + 0.4 * sin(TIME * 0.6 + ph * 0.5);
    float lean = sway_amount * gust * (0.7 + 0.3 * flutter);
    vec2 bend = wind_dir * lean * VERTEX.y; // tip (high y) moves most; base stays planted
    VERTEX.x += bend.x;
    VERTEX.z += bend.y;
}

void fragment() {
    vec4 c = texture(tuft, UV);
    if (c.a < 0.4) discard;
    ALBEDO = c.rgb;
    ROUGHNESS = 0.9;
    SPECULAR = 0.1;
}
