// Soft, small smoke puffs. A billboarded particle quad with a round, fbm-broken alpha
// (no hard square edges) that fades over the particle's life via the process material's
// colour ramp, read here as COLOR.a. blend_mix + unshaded so it reads as flat dark smoke
// against the sky — wisps, not billowing slabs.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled,
            ambient_light_disabled, specular_disabled, fog_disabled, unshaded;

#include "res://assets/shaders/fire_common.gdshaderinc"

uniform vec3 smoke_color : source_color = vec3(0.14, 0.13, 0.12);
uniform float opacity : hint_range(0.0, 1.0) = 0.5;

varying float v_seed;

void vertex() {
    vec3 o = MODEL_MATRIX[3].xyz;
    v_seed = fract(dot(o, vec3(0.27, 0.11, 0.59)) * 13.0);
    // Spherical billboard (smoke is roundish), preserving the particle's animated scale.
    float sx = length(MODEL_MATRIX[0].xyz);
    float sy = length(MODEL_MATRIX[1].xyz);
    mat4 bb = mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
    bb = bb * mat4(vec4(sx, 0, 0, 0), vec4(0, sy, 0, 0), vec4(0, 0, 1, 0), vec4(0, 0, 0, 1));
    MODELVIEW_MATRIX = VIEW_MATRIX * bb;
}

void fragment() {
    vec2 c = UV - 0.5;
    float d = length(c) * 2.0;                    // 0 centre .. 1 edge
    float soft = 1.0 - smoothstep(0.15, 1.0, d);  // round, soft-edged
    float n = fbm3(vec3(UV * 3.2 + v_seed * 23.0, TIME * 0.2 + v_seed * 7.0));
    ALBEDO = smoke_color * (0.7 + 0.5 * n);
    ALPHA = clamp(soft * (0.45 + 0.7 * n), 0.0, 1.0) * COLOR.a * opacity;
}
