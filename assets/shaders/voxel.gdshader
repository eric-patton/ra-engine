// PBR voxel surface shader.
// Per-vertex CUSTOM0 carries (x = texture-array layer, y = baked vertex AO).
// Albedo / normal / ORM (occlusion-roughness-metallic) / emission are sampled
// from Texture2DArrays so each block face can use its own texture without atlas
// bleeding and with proper mipmaps.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2DArray albedo_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2DArray orm_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2DArray emission_tex : filter_linear_mipmap, repeat_enable;
uniform float emission_energy = 3.0;
uniform float ao_min = 0.45; // how dark baked corner AO can get

varying flat float layer;
varying float ao_vert;

vec3 srgb_to_linear(vec3 c) {
    return mix(pow((c + vec3(0.055)) / vec3(1.055), vec3(2.4)), c / vec3(12.92), lessThan(c, vec3(0.04045)));
}

void vertex() {
    layer = CUSTOM0.x;
    ao_vert = CUSTOM0.y;
}

void fragment() {
    vec3 uvl = vec3(UV, layer);

    vec4 alb = texture(albedo_tex, uvl);
    vec3 baseCol = srgb_to_linear(alb.rgb);
    // Baked voxel corner AO darkens the albedo so it shows under direct light too.
    baseCol *= mix(ao_min, 1.0, clamp(ao_vert, 0.0, 1.0));
    ALBEDO = baseCol;

    NORMAL_MAP = texture(normal_tex, uvl).rgb;
    NORMAL_MAP_DEPTH = 1.0;

    vec3 orm = texture(orm_tex, uvl).rgb;
    AO = orm.r;
    AO_LIGHT_AFFECT = 0.4;
    ROUGHNESS = orm.g;
    METALLIC = orm.b;

    vec3 em = texture(emission_tex, uvl).rgb;
    EMISSION = srgb_to_linear(em) * emission_energy;
}
