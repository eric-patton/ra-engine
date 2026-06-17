// Volumetric "churning water" — thousands of small, translucent, hard-stepped voxel CUBES (one
// MultiMesh, instanced by ChurnWater.cs) that give a waterfall real 3D depth: layered cubes you can
// see through, chunky foam, raked streaks down the falls. The CPU buffer is STATIC (written once);
// every cube's chunky size + off-lattice jitter + presence is baked in C#, so nothing pops in place.
//
// All MOTION lives here on the GPU and is DIRECTIONAL — the previous "collapse below a scrolling
// threshold" trick made cubes blink in place with no sense of flow. Instead each cube streams along
// a per-instance flow vector (down + along the spill for the falls; radially outward from the impact
// for pool foam) over a looping life cycle, fading in as it spawns and out as it travels — so foam
// visibly dissipates as it flows away, and the recycle is invisible. Per-cube phase offsets keep it
// a continuous stream rather than a marching band.
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_prepass_alpha, diffuse_burley, specular_schlick_ggx;

uniform float fall_rate = 0.55;     // downward life-cycles / sec for falling cubes
uniform float fall_travel = 3.4;    // metres a falling cube streams before it recycles
uniform float fall_lateral = 0.35;  // how much falling cubes drift along the spill direction
uniform float pool_rate = 0.32;     // outward life-cycles / sec for pool foam
uniform float pool_travel = 2.6;    // metres foam drifts out from the impact before it fades
uniform vec3 deep_color  : source_color = vec3(0.10, 0.38, 0.74);
uniform vec3 mid_color   : source_color = vec3(0.36, 0.66, 0.93);
uniform vec3 light_color : source_color = vec3(0.74, 0.90, 1.00);
uniform vec3 foam_color  : source_color = vec3(1.00, 1.00, 1.00);

varying float v_tone;   // stable per-cube palette coordinate (no scrolling field -> no colour blink)
varying float v_fade;   // motion fade — doubles as the flow-out alpha
varying float v_turb;

void vertex() {
    float turb  = INSTANCE_CUSTOM.r;
    float state = INSTANCE_CUSTOM.g;   // 0=falling .25=edge .5=impact .75=spread
    float seed  = INSTANCE_CUSTOM.b;
    float aflow = INSTANCE_CUSTOM.a;   // horizontal flow angle, packed to 0..1
    bool falling = state < 0.12;

    float phase = fract(seed * 31.0);
    float rate  = falling ? fall_rate : pool_rate;
    float u = fract(TIME * rate + phase);                  // 0..1 per-cube life cycle

    float th = (aflow - 0.5) * 6.28318;
    vec3 dir; float travel;
    if (falling) { dir = normalize(vec3(cos(th) * fall_lateral, -1.0, sin(th) * fall_lateral)); travel = fall_travel; }
    else         { dir = vec3(cos(th), 0.0, sin(th)); travel = pool_travel; }

    // Stream the whole cube along its flow vector, in WORLD space. The instance basis is a pure
    // scale, so dividing the world offset by that scale lets MODEL_MATRIX put it back exactly.
    vec3 worldOff = dir * (u * travel);
    vec3 iscale = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
    VERTEX += worldOff / max(iscale, vec3(1e-4));

    v_fade = smoothstep(0.0, 0.12, u) * smoothstep(1.0, 0.78, u);   // fade in at spawn, out as it flows
    v_turb = turb;
    float white_near = falling ? 0.0 : (1.0 - u) * 0.25;            // foam is whitest near the impact
    v_tone = turb * 0.4 + seed * 0.35 + white_near;
}

void fragment() {
    // Strictly STEPPED palette — discrete blues + white foam, no smooth gradient (keeps it voxel).
    float t = v_tone;
    vec3 col;
    if      (t > 0.78) col = foam_color;
    else if (t > 0.58) col = light_color;
    else if (t > 0.38) col = mid_color;
    else               col = deep_color;

    ALBEDO = col;
    ALPHA = mix(0.50, 0.90, v_turb) * clamp(v_fade, 0.0, 1.0);   // translucent layering = volume
    ROUGHNESS = 0.12;
    METALLIC = 0.0;
    SPECULAR = 0.4;
}
