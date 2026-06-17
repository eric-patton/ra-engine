// Expanding ripple ring for B13 — a flat decal that lies on the water surface and
// blooms outward as a thinning, fading annulus. Driven by a single `prog` uniform
// (0 = just spawned, 1 = gone) that Fx advances over the ring's life, so no per-frame
// geometry work. Shared with C1 (rain impact rings) later. Stylised: one clean ring,
// quantised edge, no soft photoreal feathering.
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never, shadows_disabled,
            ambient_light_disabled, specular_disabled, fog_disabled, unshaded;

uniform vec4 ring_color : source_color = vec4(0.86, 0.93, 1.0, 0.9);
uniform float prog : hint_range(0.0, 1.0) = 0.0;

void fragment() {
    float d = length(UV - 0.5) * 2.0;          // 0 centre .. 1 plane edge
    float r = mix(0.12, 0.96, prog);           // ring radius grows outward over its life
    float band = 0.16 * (1.0 - prog * 0.55);   // ring thins as it expands
    float ring = smoothstep(band, 0.0, abs(d - r));
    float fade = 1.0 - prog;                    // and dims to nothing
    ALBEDO = ring_color.rgb;
    ALPHA = ring * ring_color.a * fade;
}
