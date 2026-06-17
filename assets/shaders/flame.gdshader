// Stylized billboard flame. A single QuadMesh per flame layer, cylindrically
// billboarded (faces the camera in yaw but stays upright so flames always stand up),
// shaped by rising fbm noise into a licking teardrop and coloured by the quantized
// blackbody ramp for a chunky, hand-painted look that matches the voxel world.
//
// Additive + emissive so the WorldEnvironment Glow blooms it. `flicker` is pushed
// every frame by FireController so the whole flame breathes in sync with its light;
// `col_*` set the palette (warm fire / holy white-gold / forge red); a per-instance
// seed derived from the world position decorrelates stacked layers and nearby fires.
shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, shadows_disabled,
            ambient_light_disabled, specular_disabled, fog_disabled;

#include "res://assets/shaders/fire_common.gdshaderinc"

uniform float emission_energy = 3.0;
uniform float bands = 5.0;          // colour quantization (lower = chunkier)
uniform float rise_speed = 1.7;
uniform float flicker = 1.0;        // breathing brightness, pushed each frame
uniform vec3 col_dark   : source_color = vec3(0.25, 0.01, 0.0);
uniform vec3 col_red    : source_color = vec3(0.85, 0.10, 0.01);
uniform vec3 col_orange : source_color = vec3(1.0, 0.38, 0.02);
uniform vec3 col_gold   : source_color = vec3(1.0, 0.75, 0.18);
uniform vec3 col_white  : source_color = vec3(1.0, 0.97, 0.82);

varying float v_seed;
varying float v_h;   // 0 at the base of the quad, 1 at the tip
varying float v_x;   // -0.5 .. 0.5 across the quad

void vertex() {
    // Per-instance seed from the world origin so stacked layers and separate fires
    // each burn with their own noise phase.
    vec3 o = MODEL_MATRIX[3].xyz;
    v_seed = fract(dot(o, vec3(0.137, 0.711, 0.289)) + length(o) * 0.013);

    v_h = VERTEX.y + 0.5;   // unit QuadMesh spans -0.5..0.5 in Y
    v_x = VERTEX.x;

    // Cylindrical billboard: X follows the camera's right vector, Y is world up, so
    // the flame yaws to face the viewer while staying vertical. Scale comes from the
    // instance (width on X, height on Y).
    vec3 right = normalize(INV_VIEW_MATRIX[0].xyz);
    vec3 up = vec3(0.0, 1.0, 0.0);
    vec3 fwd = normalize(cross(right, up));
    right = normalize(cross(up, fwd));
    float sx = length(MODEL_MATRIX[0].xyz);
    float sy = length(MODEL_MATRIX[1].xyz);
    mat4 bb = mat4(vec4(right * sx, 0.0), vec4(up * sy, 0.0), vec4(fwd, 0.0), MODEL_MATRIX[3]);
    MODELVIEW_MATRIX = VIEW_MATRIX * bb;
}

void fragment() {
    float h = clamp(v_h, 0.0, 1.0);
    float xc = v_x;

    // Rising noise: scrolls downward in V so the flame appears to climb.
    float n = fbm3(vec3(vec2(v_x + 0.5, h) * vec2(3.0, 4.5)
            + vec2(v_seed * 17.0, -TIME * rise_speed), v_seed * 9.0));

    // Silhouette: wide at the base, pinching to a tip, eroded ever more toward the
    // top so it licks into ragged tongues.
    float halfw = mix(0.46, 0.05, smoothstep(0.0, 1.0, h));
    float body = smoothstep(halfw, halfw * 0.25, abs(xc));
    body *= 1.0 - smoothstep(0.72, 1.02, h);
    float er = mix(0.16, 0.60, h);
    float a = body * smoothstep(er, er + 0.20, n);

    // Heat: hottest at the core/base, cooler at the edges and tips. Quantized so the
    // colour steps in chunky bands rather than a smooth gradient.
    float heat = (1.0 - h) * 1.15 - abs(xc) * 1.3 + (n - 0.5) * 0.6;
    heat = qstep(heat, bands);
    vec3 col = fire_ramp(heat, col_dark, col_red, col_orange, col_gold, col_white);

    ALBEDO = vec3(0.0);
    EMISSION = col * emission_energy * flicker;
    ALPHA = clamp(a, 0.0, 1.0);
}
