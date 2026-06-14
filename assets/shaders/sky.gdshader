// Day/night sky: gradient + sun disk & glow + stars + drifting fbm clouds.
// `day` (0 = night, 1 = full day) cross-fades the palette, fades stars in at
// night and lights the clouds; `sun_dir` is the direction toward the sun.
shader_type sky;

uniform vec3 sun_dir = vec3(0.0, 0.6, -0.8);
uniform float day : hint_range(0.0, 1.0) = 1.0;
uniform vec3 zenith_day : source_color = vec3(0.26, 0.46, 0.86);
uniform vec3 horizon_day : source_color = vec3(0.74, 0.82, 0.90);
uniform vec3 zenith_night : source_color = vec3(0.01, 0.02, 0.06);
uniform vec3 horizon_night : source_color = vec3(0.05, 0.06, 0.13);
uniform vec3 sun_color : source_color = vec3(1.0, 0.96, 0.85);
uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.5;
uniform float cloud_speed = 0.004;
uniform float star_density : hint_range(0.0, 1.0) = 0.02;

float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i), b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0)), d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) { s += a * vnoise(p); p *= 2.0; a *= 0.5; }
    return s;
}

void sky() {
    vec3 dir = normalize(EYEDIR);

    // Base vertical gradient.
    vec3 zen = mix(zenith_night, zenith_day, day);
    vec3 hor = mix(horizon_night, horizon_day, day);
    vec3 col = mix(hor, zen, smoothstep(0.0, 0.55, dir.y));

    // Warm golden-hour glow at the horizon toward the sun when the sun is low.
    vec3 sdir = normalize(sun_dir);
    float horizonBand = 1.0 - smoothstep(0.0, 0.32, abs(dir.y));
    float lowSun = clamp(1.0 - abs(sdir.y) * 3.0, 0.0, 1.0) * day;
    float towardSun = max(dot(normalize(dir.xz + vec2(1e-4)), normalize(sdir.xz + vec2(1e-4))), 0.0);
    col = mix(col, vec3(1.0, 0.5, 0.22), horizonBand * lowSun * towardSun * 0.7);

    // Sun disk + glow (only contributes during the day).
    float sd = max(dot(dir, sdir), 0.0);
    float disk = smoothstep(0.9977, 0.9990, sd);
    float glow = pow(sd, 90.0) * 0.6 + pow(sd, 8.0) * 0.12;
    col += sun_color * (disk + glow) * clamp(day + 0.15, 0.0, 1.0);

    // Stars: fade in as it gets dark, only above the horizon.
    if (dir.y > 0.04) {
        vec2 sc = floor(dir.xz / (dir.y + 0.25) * 60.0);
        float s = hash(sc);
        float twinkle = 0.7 + 0.3 * sin(TIME * 2.0 + s * 30.0);
        float star = smoothstep(1.0 - star_density, 1.0, s) * twinkle;
        col += vec3(star) * pow(1.0 - day, 1.5) * smoothstep(0.04, 0.2, dir.y);
    }

    // Drifting clouds.
    if (dir.y > 0.02) {
        vec2 cp = dir.xz / (dir.y + 0.12);
        float c = fbm(cp * 1.1 + vec2(TIME * cloud_speed, TIME * cloud_speed * 0.6));
        float cover = mix(0.75, 0.35, cloud_coverage);
        float cloud = smoothstep(cover, cover + 0.22, c) * smoothstep(0.02, 0.22, dir.y);
        vec3 cloudCol = mix(vec3(0.45, 0.47, 0.55), vec3(1.0), day);
        col = mix(col, cloudCol, cloud * 0.75);
    }

    COLOR = col;
}
