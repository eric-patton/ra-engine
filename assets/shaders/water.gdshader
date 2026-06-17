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
uniform vec3 foam_color : source_color = vec3(0.86, 0.93, 1.0);
uniform float wave_height = 0.07;
uniform float wave_speed = 1.1;
uniform float water_alpha : hint_range(0.0, 1.0) = 0.74;
uniform float flow_speed = 1.4;   // how fast river foam crests travel along the current
uniform float fall_speed = 2.2;   // how fast a waterfall's streaks pour downward

varying vec3 v_wpos;  // world-space position of this surface point
varying float v_up;   // 1 on up-facing surfaces, 0 on the vertical side faces
// Per-cell water-FX channels baked by ChunkMesher into CUSTOM0.ba:
//   top faces  → v_flow = current direction * strength (0 = still water).
//   side faces → v_fall = 1 on the vertical sheet of a waterfall.
varying vec2 v_flow;
varying float v_fall;
varying float v_across; // world horizontal coord across a side face (for vertical fall streaks)

float wave(vec2 p, float t) {
    return sin(p.x * 0.6 + t) * 0.34
         + sin(p.y * 0.5 + t * 1.3) * 0.34
         + sin((p.x + p.y) * 0.3 + t * 0.7) * 0.32;
}

// Quantise a 0..1 value into a few flat bands so foam reads stylised/blocky, not photoreal.
float band(float x, float n) { return floor(clamp(x, 0.0, 1.0) * n) / (n - 1.0); }

void vertex() {
    vec3 wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    // No geometry displacement — the mesh stays flat so greedy-merged quads of
    // different sizes share edge vertices at the same height (no T-junction gaps)
    // and no triangle fold creates diagonal seams. Ripple appearance comes from
    // the per-pixel analytic normal in fragment().
    v_up = NORMAL.y > 0.5 ? 1.0 : 0.0;
    v_wpos = wpos;
    v_flow = CUSTOM0.ba; // (flow.x, flow.z) on top faces
    v_fall = CUSTOM0.a;  // fall flag on side faces (same channel, read only when !v_up)
    // Which horizontal world axis runs across this side face (perpendicular to its normal),
    // so a waterfall's foam streaks line up vertically instead of shearing diagonally.
    vec3 wn = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
    v_across = abs(wn.x) > 0.5 ? wpos.z : wpos.x;
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

    // B1 — flowing rivers: stylised foam crests stream along the baked current direction.
    float spd = length(v_flow);
    if (v_up > 0.5 && spd > 0.001) {
        vec2 dir = v_flow / spd;
        float along  = dot(v_wpos.xz, dir);
        float across = dot(v_wpos.xz, vec2(-dir.y, dir.x));
        float phase  = along * 2.2 - TIME * flow_speed * (0.5 + spd);
        float crest  = (sin(phase) * 0.5 + 0.5) * (0.6 + 0.4 * sin(across * 1.7 + phase * 0.5));
        float foam   = band(smoothstep(0.55, 0.95, crest) * spd, 3.0);
        ALBEDO = mix(ALBEDO, foam_color, clamp(foam, 0.0, 0.7));
        ALPHA  = clamp(ALPHA + foam * 0.25, 0.0, 1.0);
    }

    // B2 — waterfalls: vertical foam streaks pour straight down. Columns are set by the
    // horizontal position across the sheet; their brightness scrolls downward over time.
    if (v_up < 0.5 && v_fall > 0.5) {
        float columns = sin(v_across * 5.5) * 0.5 + 0.5;          // standing foam columns
        float down    = fract(v_wpos.y * 0.9 + TIME * fall_speed); // sawtooth travelling down
        float foam    = band(columns * (0.35 + 0.65 * down), 3.0);
        ALBEDO = mix(shallow_color, foam_color, foam * 0.85);
        ROUGHNESS = 0.06;
        ALPHA = clamp(water_alpha + 0.18 + foam * 0.18, 0.0, 1.0);
    }
}
