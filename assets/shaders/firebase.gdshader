// Flat "coal-bed" glow disc that lies on the ground at a fire's base. A hot radial pool
// with fbm coal breakup, additive + emissive so it blooms. It reads from straight above —
// where the vertical flame billboard goes edge-on and nearly vanishes — and adds a warm
// ground pool from the side. Palette-coloured + flicker-driven to match its fire.
shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, shadows_disabled,
            ambient_light_disabled, specular_disabled, fog_disabled;

#include "res://assets/shaders/fire_common.gdshaderinc"

uniform float emission_energy = 2.0;
uniform float flicker = 1.0;
uniform vec3 col_pool : source_color = vec3(1.0, 0.38, 0.05);
uniform vec3 col_coals : source_color = vec3(1.0, 0.72, 0.2);

void fragment() {
    vec2 c = UV - 0.5;
    float d = clamp(length(c) * 2.0, 0.0, 1.0);
    float fall = pow(1.0 - d, 1.8);                            // soft radial pool
    float n = fbm3(vec3(UV * 7.0, TIME * 0.8));
    float coals = smoothstep(0.5, 0.85, n) * smoothstep(0.65, 0.1, d); // hot chunks inside
    vec3 col = col_pool * fall + col_coals * coals * 1.6;
    ALBEDO = vec3(0.0);
    EMISSION = col * emission_energy * flicker;
    ALPHA = clamp(fall + coals, 0.0, 1.0);
}
