// Cross-quad grass tufts with a gentle, position-phased wind sway. The top of
// each blade moves more than the base (sway scales with local height), and the
// alpha-cut texture gives the tuft its shape.
shader_type spatial;
render_mode cull_disabled, depth_prepass_alpha;

uniform sampler2D tuft : source_color, filter_linear_mipmap;
uniform float sway_amount = 0.10;

void vertex() {
    vec3 wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float ph = wpos.x * 0.6 + wpos.z * 0.6;
    float s = (sin(TIME * 1.6 + ph) + 0.4 * sin(TIME * 2.7 + ph * 1.7)) * sway_amount;
    VERTEX.x += s * VERTEX.y;
    VERTEX.z += s * 0.5 * VERTEX.y;
}

void fragment() {
    vec4 c = texture(tuft, UV);
    if (c.a < 0.4) discard;
    ALBEDO = c.rgb;
    ROUGHNESS = 0.9;
    SPECULAR = 0.1;
}
