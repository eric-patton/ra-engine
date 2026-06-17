// Volumetric "churning water" — renders thousands of small, translucent, hard-stepped voxel CUBES
// (one MultiMesh, instanced by ChurnWater.cs) so a waterfall reads with real 3D depth: layered
// cubes you can see THROUGH, chunky sub-block foam, raked vertical streaks down the falls. All the
// per-frame motion lives here on the GPU — the CPU instance buffer is static (written once on
// world build). Per instance we sample a downward-scrolling 3D density field; sub-cubes below a
// turbulence-scaled threshold collapse to zero scale, survivors jitter + resize, and a strictly
// stepped blue→white palette at translucent alpha makes the overlapping layers read as VOLUME.
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_prepass_alpha, diffuse_burley, specular_schlick_ggx;

#include "res://assets/shaders/fire_common.gdshaderinc" // hash13 / fbm3 (≈0..1) — no sampler3D needed

uniform float fall_speed = 1.4;     // how fast the churn density scrolls downward
uniform float noise_scale = 0.55;   // spatial frequency of the churn field
uniform float jitter_amt = 0.16;    // how far survivors wander off the sub-grid lattice
uniform vec3 deep_color  : source_color = vec3(0.10, 0.38, 0.74);
uniform vec3 mid_color   : source_color = vec3(0.36, 0.66, 0.93);
uniform vec3 light_color : source_color = vec3(0.74, 0.90, 1.00);
uniform vec3 foam_color  : source_color = vec3(1.00, 1.00, 1.00);

varying float v_dens;
varying float v_turb;
varying float v_state;

void vertex() {
    float turb  = INSTANCE_CUSTOM.r;   // 0..1 turbulence
    float state = INSTANCE_CUSTOM.g;   // 0=falling .25=edge .5=impact .75=spread
    float seed  = INSTANCE_CUSTOM.b;   // de-correlates the lattice
    float phase = INSTANCE_CUSTOM.a;   // de-syncs the animation

    // Sample the density field at this sub-cube's world origin, scrolling downward over time.
    vec3 origin = MODEL_MATRIX[3].xyz;
    vec3 uvw = origin * noise_scale + vec3(seed * 7.0, -TIME * fall_speed + phase, seed * 3.0);
    float dens = fbm3(uvw);

    // More turbulent -> lower threshold -> more sub-cubes survive -> denser churn.
    float thresh = mix(0.60, 0.04, turb);
    float alive = step(thresh, dens);

    // Size varies with density (the chunky size-RANGE from the references); dead cubes collapse.
    float s = alive * mix(0.45, 1.0, dens);
    VERTEX *= s;

    // Break the rigid sub-grid so it doesn't read as a lattice.
    vec3 j = vec3(hash13(vec3(seed, 1.7, 0.0)), dens, hash13(vec3(seed, 4.3, 0.0))) - 0.5;
    VERTEX += j * jitter_amt * alive;

    v_dens = dens;
    v_turb = turb;
    v_state = state;
}

void fragment() {
    // Strictly STEPPED palette — discrete blues + white foam, no smooth gradient (keeps it voxel).
    float foam = v_dens + v_turb * 0.35;
    foam += (v_state > 0.1 && v_state < 0.65) ? 0.12 : 0.0;   // edge / impact read whiter
    vec3 col;
    if      (foam > 0.82) col = foam_color;
    else if (foam > 0.64) col = light_color;
    else if (foam > 0.46) col = mid_color;
    else                  col = deep_color;

    ALBEDO = col;
    ALPHA = mix(0.50, 0.90, v_turb);   // translucent layering = the volumetric read
    ROUGHNESS = 0.12;
    METALLIC = 0.0;
    SPECULAR = 0.4;
}
