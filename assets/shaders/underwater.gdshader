// Full-screen underwater post-process for the HUD overlay. Samples the rendered
// scene (screen texture) and adds a gentle refraction wobble, a depth-murk blue
// tint with red absorption, and an edge vignette. `strength` (0..1) fades the whole
// effect — driven light at the waterline and strong when fully submerged. Because
// it's the first HUD child it samples only the 3D scene, leaving the HUD crisp.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform float strength : hint_range(0.0, 1.0) = 0.0;
uniform vec3 water_tint : source_color = vec3(0.10, 0.34, 0.52);
uniform float low_air : hint_range(0.0, 1.0) = 0.0; // 0 = fresh, 1 = out of breath

void fragment() {
    vec2 uv = SCREEN_UV;

    // Gentle refraction wobble — a couple of crossing sine waves over time.
    float t = TIME;
    vec2 wob = vec2(
        sin(uv.y * 28.0 + t * 1.7) + sin(uv.y * 13.0 - t * 1.1),
        cos(uv.x * 24.0 + t * 1.5) + cos(uv.x * 11.0 - t * 0.9)
    );
    vec2 off = wob * 0.004 * strength;
    vec3 col = texture(screen_tex, clamp(uv + off, vec2(0.0), vec2(1.0))).rgb;

    // Murk: blend toward the water colour, and absorb red (deep water goes blue-green).
    col = mix(col, water_tint, strength * 0.45);
    col.r *= mix(1.0, 0.72, strength);

    // Vignette: darken the edges for an enclosed, underwater feel.
    float vig = distance(uv, vec2(0.5));
    col *= mix(1.0, 1.0 - vig * 0.8, strength);

    // Out of breath: deepen the vignette and dim overall — a gentle "surface!" cue.
    col *= mix(1.0, 1.0 - vig * 1.2, low_air);
    col *= mix(1.0, 0.80, low_air);

    COLOR = vec4(col, 1.0);
}
