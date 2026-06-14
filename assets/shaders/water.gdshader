// Translucent rippling water. Waves are a function of world XZ + time, so quads
// that share an edge share the same displacement and stay welded (no cracks).
// Only up-facing surfaces ripple; vertical side faces just bob with the surface.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx, depth_draw_always;

uniform vec3 shallow_color : source_color = vec3(0.20, 0.52, 0.66);
uniform vec3 deep_color : source_color = vec3(0.04, 0.20, 0.34);
uniform float wave_height = 0.07;
uniform float wave_speed = 1.1;
uniform float water_alpha : hint_range(0.0, 1.0) = 0.74;

float wave(vec2 p, float t) {
    return sin(p.x * 0.6 + t) * 0.34
         + sin(p.y * 0.5 + t * 1.3) * 0.34
         + sin((p.x + p.y) * 0.3 + t * 0.7) * 0.32;
}

void vertex() {
    vec3 wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float t = TIME * wave_speed;
    float h = wave(wpos.xz, t);
    VERTEX.y += h * wave_height;

    if (NORMAL.y > 0.5) {
        float e = 0.35;
        float hx = wave(wpos.xz + vec2(e, 0.0), t);
        float hz = wave(wpos.xz + vec2(0.0, e), t);
        NORMAL = normalize(vec3(-(hx - h) * wave_height / e, 1.0, -(hz - h) * wave_height / e));
    }
}

void fragment() {
    float fres = pow(1.0 - clamp(dot(normalize(NORMAL), VIEW), 0.0, 1.0), 3.0);
    ALBEDO = mix(deep_color, shallow_color, fres);
    ROUGHNESS = 0.04;
    METALLIC = 0.0;
    SPECULAR = 0.6;
    ALPHA = clamp(water_alpha + fres * 0.2, 0.0, 1.0);
}
