// Translucent rippling water. Waves are a function of world XZ + time, so quads
// that share an edge share the same displacement and stay welded (no cracks).
//
// The wave NORMAL is recomputed per-pixel in fragment() from the same world-XZ
// wave function, instead of being interpolated from the four corner normals of a
// (possibly 16-wide, greedy-merged) quad. Linear interpolation of a corner normal
// across a quad spanning many wavelengths produced a facet whose slope broke at
// every quad/chunk edge, and the nonlinear Fresnel turned that C1 break into the
// faint grid of seams on the top surface. A per-pixel normal is continuous
// everywhere regardless of tessellation, so the seams are gone.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx, depth_draw_always;

uniform vec3 shallow_color : source_color = vec3(0.20, 0.52, 0.66);
uniform vec3 deep_color : source_color = vec3(0.04, 0.20, 0.34);
uniform float wave_height = 0.07;
uniform float wave_speed = 1.1;
uniform float water_alpha : hint_range(0.0, 1.0) = 0.74;

varying vec3 v_wpos;  // world-space position of this surface point
varying float v_up;   // 1 on up-facing surfaces, 0 on the vertical side faces

float wave(vec2 p, float t) {
    return sin(p.x * 0.6 + t) * 0.34
         + sin(p.y * 0.5 + t * 1.3) * 0.34
         + sin((p.x + p.y) * 0.3 + t * 0.7) * 0.32;
}

void vertex() {
    vec3 wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float t = TIME * wave_speed;
    float h = wave(wpos.xz, t);
    VERTEX.y += h * wave_height;          // geometry still ripples
    v_up = NORMAL.y > 0.5 ? 1.0 : 0.0;
    v_wpos = wpos;                         // xz is what the wave samples; unchanged by the y bob
}

void fragment() {
    vec3 nv = normalize(NORMAL); // view-space interpolated normal — kept for the side faces
    if (v_up > 0.5) {
        // Analytic wave normal at THIS pixel's world XZ (continuous across all quads).
        float t = TIME * wave_speed;
        float e = 0.35;
        float h  = wave(v_wpos.xz, t);
        float hx = wave(v_wpos.xz + vec2(e, 0.0), t);
        float hz = wave(v_wpos.xz + vec2(0.0, e), t);
        vec3 nworld = normalize(vec3(-(hx - h) * wave_height / e, 1.0, -(hz - h) * wave_height / e));
        nv = normalize((VIEW_MATRIX * vec4(nworld, 0.0)).xyz); // to view space for the Fresnel dot
    }
    NORMAL = nv;

    float fres = pow(1.0 - clamp(dot(nv, VIEW), 0.0, 1.0), 3.0);
    ALBEDO = mix(deep_color, shallow_color, fres);
    ROUGHNESS = 0.04;
    METALLIC = 0.0;
    SPECULAR = 0.6;
    ALPHA = clamp(water_alpha + fres * 0.2, 0.0, 1.0);
}
