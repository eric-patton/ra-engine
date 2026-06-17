// Translucent rippling water. The mesh is kept geometrically flat — no vertex
// displacement — so greedy-merged quads of different sizes never produce T-junction
// gaps or triangle-fold diagonal seams. The current, foam and ripples all read through
// the NORMAL (animated highlights), not through bright additive patches, which keeps the
// surface believable and avoids "floating blobs" / "shockwave ring" artefacts.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

#include "res://assets/shaders/fire_common.gdshaderinc" // hash13 / vnoise3 / fbm3 / qstep

uniform vec3 shallow_color : source_color = vec3(0.20, 0.52, 0.66);
uniform vec3 deep_color : source_color = vec3(0.04, 0.20, 0.34);
uniform vec3 foam_color : source_color = vec3(0.86, 0.93, 1.0);
uniform float wave_height = 0.07;
uniform float wave_speed = 1.1;
uniform float water_alpha : hint_range(0.0, 1.0) = 0.74;
uniform float flow_speed = 0.9;   // how fast river current scrolls
uniform float fall_speed = 1.6;   // how fast a waterfall's streaks pour downward

// B13 ripple rings — a CPU-filled ring buffer of recent surface impacts (stone, footstep,
// raindrop). Each becomes an expanding, fading normal perturbation, so ripples only ever
// appear ON the water (non-water pixels never run this) and read as deformation, not a ring.
const int RIPPLES = 12;
uniform vec4 ripples[RIPPLES]; // xy = world XZ, z = start time (u_time clock), w = size (0 = empty)
uniform float u_time = 0.0;
uniform float ripple_speed = 2.4;
uniform float ripple_life = 1.0;
uniform float ripple_amp = 0.8;
uniform float ripple_freq = 22.0;

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

float fbm2(vec2 p) { return fbm3(vec3(p, 0.37)); } // 2D slice of the shared fbm

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
    vec3 wn = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
    v_across = abs(wn.x) > 0.5 ? wpos.z : wpos.x;
}

void fragment() {
    bool top = v_up > 0.5;
    vec3 nworld = vec3(0.0, 1.0, 0.0);

    if (top) {
        // Base analytic wave normal at THIS pixel's world XZ (continuous across all quads).
        float t = TIME * wave_speed;
        float e = 0.35;
        float h  = wave(v_wpos.xz, t);
        float hx = wave(v_wpos.xz + vec2(e, 0.0), t);
        float hz = wave(v_wpos.xz + vec2(0.0, e), t);
        nworld = normalize(vec3(-(hx - h) * wave_height / e, 1.0, -(hz - h) * wave_height / e));

        // B1 — directional current: advect a noise field along the per-cell flow vector and
        // tilt the normal downstream, so the highlights stream (no bright foam patches).
        float spd = length(v_flow);
        if (spd > 0.001) {
            vec2 dir = v_flow / spd;
            float ph = TIME * flow_speed * (0.6 + spd);
            vec2 suv = v_wpos.xz - dir * ph;
            float rip = (fbm2(suv * 1.2) * 0.6 + fbm2((suv - dir * 0.7) * 2.6 + 7.0) * 0.4) - 0.5;
            nworld = normalize(nworld + vec3(dir.x, 0.0, dir.y) * rip * (0.5 + spd));
        }

        // B13 — accumulate ripple-ring normal perturbations from recent impacts.
        for (int i = 0; i < RIPPLES; i++) {
            vec4 r = ripples[i];
            if (r.w <= 0.0) continue;
            float life = ripple_life * (0.5 + r.w);
            float age = u_time - r.z;
            if (age < 0.0 || age > life) continue;
            vec2 toP = v_wpos.xz - r.xy;
            float d = length(toP);
            float rad = age * ripple_speed;
            if (d > rad + 0.5) continue;                              // outside the wavefront
            float band = pow(clamp(1.0 - abs(d - rad) * 2.0, 0.0, 1.0), 6.0); // soft moving band
            float w = sin((d - rad) * ripple_freq) * band;           // crests inside the band
            float env = 1.0 - age / life;                            // fade over its life
            nworld.xz += (toP / max(d, 1e-4)) * w * env * ripple_amp * (0.4 + r.w);
        }
        nworld = normalize(nworld);
    }

    vec3 nv = top ? normalize((VIEW_MATRIX * vec4(nworld, 0.0)).xyz) : normalize(NORMAL);
    NORMAL = nv;

    float fres = pow(1.0 - clamp(dot(nv, VIEW), 0.0, 1.0), 3.0);
    ALBEDO = mix(deep_color, shallow_color, fres);
    ROUGHNESS = 0.04;
    METALLIC = 0.0;
    SPECULAR = 0.6;
    ALPHA = clamp(water_alpha + fres * 0.2, 0.0, 1.0);

    // B1 — sparse, thin foam only where the current is fast, advected downstream so it
    // streaks rather than sitting as blobs.
    float spd2 = length(v_flow);
    if (top && spd2 > 0.001) {
        vec2 dir = v_flow / spd2;
        float fn = fbm2((v_wpos.xz - dir * (TIME * flow_speed)) * 2.2);
        float foam = smoothstep(0.62, 0.88, fn) * smoothstep(0.25, 0.8, spd2);
        foam += smoothstep(0.55, 1.0, spd2) * 0.6;   // bold whitewater band at the brink/lip
        ALBEDO = mix(ALBEDO, foam_color, clamp(foam, 0.0, 0.85));
    }

    // B2 — waterfall curtain: aperiodic vertical foam streaks. Multiple fbm layers scrolling
    // down at different speeds + a per-lane random phase + a domain warp, quantised into a few
    // bands. This replaces the old periodic sin-grid that read as a tiled "U-cup" texture.
    if (!top && v_fall > 0.4) {
        float lane = floor(v_across * 2.2);
        float lh = hash13(vec3(lane, 1.7, 2.3));                     // per-lane random
        // + TIME on the vertical term so the streaks scroll DOWNWARD (the water falls).
        float warp = fbm2(vec2(v_across * 0.7, v_wpos.y * 0.2 + TIME * 0.3)) - 0.5;
        float ax = v_across * 1.3 + warp * 0.7;                      // domain-warped horizontal
        float a = fbm2(vec2(ax * 1.2, v_wpos.y * 1.0 + TIME * (fall_speed * 0.7 + lh * 0.5) + lh * 9.0));
        float b = fbm2(vec2(ax * 2.6, v_wpos.y * 1.9 + TIME * fall_speed * 1.2));
        float streaks = a * 0.6 + b * 0.4;
        // Bold whitewater: foam-dominant, blue only in the troughs — reads as churning falling
        // water, not translucent glass. Matte (high roughness) so it doesn't mirror the sky.
        vec3 col = mix(shallow_color, foam_color, smoothstep(0.30, 0.78, streaks));
        col = mix(col, deep_color, smoothstep(0.35, 0.0, streaks) * 0.35); // depth in the troughs
        // churning foam at the lip (top of the curtain) where it pours over the brink.
        float churn = fbm2(vec2(ax * 2.0, v_wpos.y * 3.0 + TIME * fall_speed * 2.2));
        if (v_fall < 0.75) col = mix(col, foam_color, smoothstep(0.28, 0.6, churn));
        ALBEDO = col;
        ROUGHNESS = 0.55;
        SPECULAR = 0.2;
        METALLIC = 0.0;
        ALPHA = clamp(0.82 + smoothstep(0.5, 0.85, streaks) * 0.18, 0.0, 1.0);
    }
}
