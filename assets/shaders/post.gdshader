// Lightweight full-screen "production sheen" pass for the HUD: a subtle reactive
// vignette + filmic grain in a single screen read, so the rendered scene gets a touch
// of polish without stacking extra passes. Mounted as a GameHud ColorRect drawn over the
// 3D scene but UNDER the UI text, so it frames the world and leaves the bars crisp.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_linear;
uniform float vignette_strength : hint_range(0.0, 1.0) = 0.28;
uniform float grain_amount : hint_range(0.0, 0.2) = 0.028;

// Cheap per-pixel hash for the grain.
float hash(vec2 p) {
    p = fract(p * vec2(443.897, 441.423));
    p += dot(p, p.yx + 19.19);
    return fract((p.x + p.y) * p.x);
}

void fragment() {
    vec3 col = texture(screen_tex, SCREEN_UV).rgb;

    // Vignette: gently darken toward the corners to frame the image.
    float v = smoothstep(0.42, 0.92, distance(SCREEN_UV, vec2(0.5)));
    col *= 1.0 - v * vignette_strength;

    // Filmic grain: signed noise quantised to ~24 fps (stable per frame, not shimmering),
    // strongest in shadows and invisible in highlights — keeps the image from looking
    // digitally sterile without anyone consciously seeing "noise".
    float lum = dot(col, vec3(0.299, 0.587, 0.114));
    float frame = floor(TIME * 24.0);
    float n = hash(SCREEN_UV * vec2(1920.0, 1080.0) + frame * 7.13) - 0.5;
    col += n * grain_amount * (1.0 - lum);

    COLOR = vec4(col, 1.0);
}
