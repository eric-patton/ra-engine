// Full-screen damage / low-health overlay for the HUD. Draws an edge vignette in a
// flash colour: `flash_amount` spikes on a hit and fades out, while `low_health`
// drives a slow red pulse when the player is badly hurt. It writes only a tinted
// alpha (no screen-texture read), so it composes cheaply over the 3D scene and the
// HUD bars layered after it stay readable.
shader_type canvas_item;

uniform float flash_amount : hint_range(0.0, 1.0) = 0.0;
uniform vec4 flash_color : source_color = vec4(0.85, 0.06, 0.05, 1.0);
uniform float low_health : hint_range(0.0, 1.0) = 0.0;

void fragment() {
    // Vignette: 0 in the centre, ramping to 1 toward the corners.
    float vig = smoothstep(0.30, 0.85, distance(SCREEN_UV, vec2(0.5)));

    // Hit flash: strong at the edges with a faint full-screen wash.
    float flash = flash_amount * (0.20 + 0.80 * vig);

    // Low health: a calm heartbeat pulse confined to the edges.
    float pulse = 0.5 + 0.5 * sin(TIME * 4.0);
    float low = low_health * vig * (0.40 + 0.60 * pulse);

    float a = clamp(max(flash, low), 0.0, 1.0);
    COLOR = vec4(flash_color.rgb, a);
}
