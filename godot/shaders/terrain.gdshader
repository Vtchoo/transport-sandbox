shader_type spatial;
render_mode ambient_light_disabled, specular_disabled, fog_disabled;
#include "res://shaders/world_surface.gdshaderinc"
uniform bool contours = false;
uniform float contour_interval = 10.0;
uniform vec2 terrain_origin = vec2(0.0);
varying float elevation;
varying vec2 ground_position;
varying vec3 terrain_color;
varying vec3 indirect_light;
varying vec3 direct_light;
varying float fog_depth;
float contour_line(float h, float width) {
    float pixel = max(fwidth(h), 0.00001);
    float distance_to_level = abs(fract(h + 0.5) - 0.5);
    return (1.0 - smoothstep(pixel * (width - 0.45), pixel * (width + 0.45), distance_to_level))
        * (1.0 - smoothstep(0.15, 0.45, pixel)) * smoothstep(0.00001, 0.0001, pixel);
}
void vertex() {
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    elevation = world.y;
    ground_position = world.xz;
    // Chunk colors are stored as sRGB. Decode before interpolation so blending
    // between meadow, rock, snow and cut/fill soil matches Three's linear colors.
    terrain_color = browser_linear(COLOR.rgb);
}
float ground_noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    vec3 q = fract(vec3(i.x, i.y, i.x) * .1031);
    q += dot(q, q.yzx + 33.33);
    float a = fract((q.x + q.y) * q.z);
    q = fract(vec3((i + vec2(1.0, 0.0)).xyx) * .1031);
    q += dot(q, q.yzx + 33.33); float b = fract((q.x + q.y) * q.z);
    q = fract(vec3((i + vec2(0.0, 1.0)).xyx) * .1031);
    q += dot(q, q.yzx + 33.33); float c = fract((q.x + q.y) * q.z);
    q = fract(vec3((i + vec2(1.0, 1.0)).xyx) * .1031);
    q += dot(q, q.yzx + 33.33); float d = fract((q.x + q.y) * q.z);
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}
void fragment() {
    vec2 p = ground_position + terrain_origin;
    float grain = ground_noise(p * 0.7) * 0.13 + ground_noise(p * 0.08) * 0.13;
    vec3 color = terrain_color * (0.87 + grain);
	if (contours) {
		float h = elevation / contour_interval;
		float minor_line = contour_line(h, 0.8);
		float major_line = contour_line(h / 5.0, 1.35);
		color = mix(color, vec3(0.075, 0.10, 0.065), max(minor_line * 0.65, major_line * 0.9));
	}
    vec3 n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec3 v = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz);
    vec3 derivatives = max(abs(dFdx(NORMAL)), abs(dFdy(NORMAL)));
    float roughness = min(0.96 + max(max(derivatives.x, derivatives.y), derivatives.z), 1.0);
    indirect_light = browser_world_ambient(color, 0.0, n);
    direct_light = browser_world_light(color, roughness, 0.0, n, v) - indirect_light;
    fog_depth = -VERTEX.z;
    ALBEDO = vec3(1.0);
    SPECULAR = 0.0;
}
void light() {
    if (LIGHT_IS_DIRECTIONAL) {
        // Shadow only sunlight, then apply the original exposure and distance fog.
        DIFFUSE_LIGHT += browser_world_lit_output(indirect_light + direct_light * ATTENUATION, fog_depth);
    }
}
