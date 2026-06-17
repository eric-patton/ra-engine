// Translucent rippling water. The mesh is kept geometrically flat — no vertex
// displacement — so greedy-merged quads of different sizes never produce T-junction
// gaps or triangle-fold diagonal seams.
//
// The wave NORMAL is computed per-pixel in fragment() from a world-XZ wave function
// sampled at each fragment's position. This is continuous across all quad boundaries
// regardless of tessellation, and the nonlinear Fresnel shading stays seam-free.
shader_type spatial;
// No depth_draw_always: with cull_disabled (needed so a submerged player sees the
// surface from below) each quad's front+back faces are coplanar; if both wrote depth
// they z-fought, tearing the surface into view-dependent triangular facets where the
// water terraces. Letting transparency sort normally removes the facets.
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

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
    // No geometry displacement — the mesh stays flat so greedy-merged quads of
    // different sizes share edge vertices at the same height (no T-junction gaps)
    // and no triangle fold creates diagonal seams. Ripple appearance comes from
    // the per-pixel analytic normal in fragment().
    v_up = NORMAL.y > 0.5 ? 1.0 : 0.0;
    v_wpos = wpos;
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
