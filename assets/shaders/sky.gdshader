// Day/night sky: gradient + a blocky sun & moon that track the real light
// directions, + stars + drifting fbm clouds. `day` (0 = night, 1 = full day)
// cross-fades the palette, fades stars in at night and lights the clouds.
// `sun_dir` / `moon_dir` are the directions toward each body (set every frame by
// EnvironmentController), so the discs move across the sky as time passes.
shader_type sky;

uniform vec3 sun_dir = vec3(0.0, 0.6, -0.8);
uniform vec3 moon_dir = vec3(0.0, -0.6, 0.8);
uniform float day : hint_range(0.0, 1.0) = 1.0;
uniform vec3 zenith_day : source_color = vec3(0.26, 0.46, 0.86);
uniform vec3 horizon_day : source_color = vec3(0.74, 0.82, 0.90);
uniform vec3 zenith_night : source_color = vec3(0.01, 0.02, 0.06);
uniform vec3 horizon_night : source_color = vec3(0.05, 0.06, 0.13);
uniform vec3 sun_color : source_color = vec3(1.0, 0.96, 0.85);
uniform vec3 moon_color : source_color = vec3(0.86, 0.90, 1.0);
uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.5;
uniform float cloud_speed = 0.004;
uniform float star_density : hint_range(0.0, 1.0) = 0.02;

float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

// A higher-quality, isotropic hash for the cloud noise. The star hash above has
// slight diagonal correlation that the cloud-plane projection stretched into
// criss-cross facets; this Hoskins-style hash is far more uniform.
float chash(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = chash(i), b = chash(i + vec2(1.0, 0.0));
    float c = chash(i + vec2(0.0, 1.0)), d = chash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// Small rotation applied between fbm octaves to break axis-aligned streaks.
const mat2 FBM_ROT = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8));

float fbm(vec2 p) {
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 6; i++) { s += a * vnoise(p); p = FBM_ROT * p * 2.0; a *= 0.5; }
    return s;
}

// A chunky, pixelated disc around `body` (a normalized direction): 1 inside, 0
// outside. `ang_radius` sets the size, `pixels` the blockiness (lower = chunkier).
float blocky_disc(vec3 dir, vec3 body, float ang_radius, float pixels) {
    if (dot(dir, body) < 0.0) return 0.0; // never mirror a disc at the antipode
    vec3 up = abs(body.y) > 0.99 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
    vec3 rt = normalize(cross(up, body));
    up = normalize(cross(body, rt));
    vec2 uv = vec2(dot(dir, rt), dot(dir, up)) / ang_radius; // ~[-1,1] inside the disc
    if (dot(uv, uv) > 1.0) return 0.0;
    uv = (floor(uv * pixels) + 0.5) / pixels;                 // snap to a coarse grid
    return dot(uv, uv) <= 1.0 ? 1.0 : 0.0;
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

    // Stars: a soft, varied point field. Each projection cell holds at most one star at a
    // random sub-position; brightness follows a power law (many faint, a few brilliant),
    // with slight size + colour variation and a gentle per-star twinkle. Round
    // (distance-based) instead of square, so they read as crisp pinpoints. A very faint
    // fbm band adds a hint of Milky Way. Only above the horizon, fading in as night falls.
    if (dir.y > 0.02) {
        float night = pow(1.0 - day, 1.5);
        // Project onto the sky; higher scale -> more, smaller stars. (dir.y + 0.15) keeps
        // the density from blowing up toward the horizon.
        vec2 suv = dir.xz / (dir.y + 0.15) * 90.0;
        vec2 cell = floor(suv);
        vec2 fp = fract(suv) - 0.5;
        float present = step(1.0 - star_density * 9.0, hash(cell + 0.5));
        vec2 jitter = (vec2(hash(cell + 3.1), hash(cell + 7.7)) - 0.5) * 0.7;
        float hb = hash(cell + 1.7);
        float mag = pow(hb, 3.5);                            // power law: mostly faint
        float radius = mix(0.07, 0.18, hash(cell + 5.3));   // slightly larger so they survive small displays
        float d = length(fp - jitter);
        float core = smoothstep(radius, 0.0, d);            // round soft dot
        float twinkle = 0.70 + 0.30 * sin(TIME * 1.6 + hb * 40.0);
        vec3 tint = mix(vec3(0.72, 0.82, 1.0), vec3(1.0, 0.92, 0.80), hash(cell + 9.2));
        float starLit = present * core * (0.50 + 3.0 * mag) * twinkle;
        // Faint Milky Way: a soft fbm brightening in a broad diagonal band.
        float band = smoothstep(0.6, 0.0, abs(dir.x * 0.7 + dir.y * 0.2 - dir.z * 0.45));
        float milky = fbm(suv * 0.02) * band * 0.05;
        col += (tint * starLit + vec3(0.6, 0.7, 1.0) * milky) * night * smoothstep(0.02, 0.16, dir.y);
    }

    // Drifting clouds: a single fbm of isotropic value noise, NO domain warp, a soft
    // coverage threshold for fluffy edges. KEY: clouds fade to ZERO at/below the projection
    // clamp (dir.y ~0.22). Below the clamp the denominator is constant, so a single cloud
    // value would otherwise smear straight down into the vertical "falling" streaks at the
    // horizon; zeroing cloud there removes them. Above the clamp the denominator varies
    // normally, so the projection is smooth.
    if (dir.y > 0.02) {
        vec2 cp = dir.xz / max(dir.y, 0.22);
        vec2 drift = vec2(TIME * cloud_speed, TIME * cloud_speed * 0.5);
        float n = fbm(cp * 1.7 + drift);
        float cover = mix(0.58, 0.32, cloud_coverage);   // higher coverage -> lower threshold
        float cloud = smoothstep(cover, cover + 0.20, n) * smoothstep(0.22, 0.48, dir.y);
        vec3 cloudCol = mix(vec3(0.26, 0.28, 0.36), vec3(1.0), day); // darker/moonlit at night
        col = mix(col, cloudCol, cloud * 0.9);
    }

    // Blocky sun, drawn on top of the clouds so it always reads, and emitted
    // HDR-bright so the Glow post-process blooms it into a clear, shining sun.
    float sunVis = smoothstep(-0.08, 0.06, sdir.y);
    float sunDisc = blocky_disc(dir, sdir, 0.09, 5.0);
    float sunGlow = pow(max(dot(dir, sdir), 0.0), 60.0) * 0.8;
    col = mix(col, sun_color * 4.0, sunDisc * sunVis);
    col += sun_color * sunGlow * sunVis;

    // Blocky moon (opposite phase), gently bright so it stands out at night.
    vec3 mdir = normalize(moon_dir);
    float moonVis = smoothstep(-0.08, 0.06, mdir.y);
    float moonDisc = blocky_disc(dir, mdir, 0.07, 5.0);
    float moonGlow = pow(max(dot(dir, mdir), 0.0), 120.0) * 0.4;
    col = mix(col, moon_color * 2.2, moonDisc * moonVis);
    col += moon_color * moonGlow * moonVis;

    COLOR = col;
}
