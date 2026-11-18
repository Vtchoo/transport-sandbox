// Lighting and tone mapping ported from Three.js r180 (MIT; see assets/menu/README.md).
// Reproduce the browser menu's hemisphere + directional light, without Godot's default IBL.
shader_type spatial;
render_mode unshaded, fog_disabled;
uniform vec3 base_color = vec3(1.0);
uniform vec3 emission_color = vec3(0.0);
uniform float metalness_value = 0.0;
uniform float roughness_value = 1.0;
uniform float opacity_value = 1.0;
uniform bool unlit = false;
uniform bool has_texture = false;
uniform sampler2D color_texture : filter_linear_mipmap, repeat_disable;

vec3 to_linear(vec3 c) {
	return mix(pow(c * 0.9478672986 + vec3(0.0521327014), vec3(2.4)), c * 0.0773993808, lessThanEqual(c, vec3(0.04045)));
}
vec3 to_srgb(vec3 c) {
	return mix(pow(max(c, vec3(0.0)), vec3(0.41666)) * 1.055 - vec3(0.055), c * 12.92, lessThanEqual(c, vec3(0.0031308)));
}
vec3 aces(vec3 c) {
	const mat3 input_mat = mat3(vec3(0.59719, 0.07600, 0.02840), vec3(0.35458, 0.90834, 0.13383), vec3(0.04823, 0.01566, 0.83777));
	const mat3 output_mat = mat3(vec3(1.60475, -0.10208, -0.00327), vec3(-0.53108, 1.10813, -0.07276), vec3(-0.07367, -0.00605, 1.07602));
	vec3 v = input_mat * (c * (1.05 / 0.6));
	vec3 a = v * (v + 0.0245786) - 0.000090537;
	vec3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
	return clamp(output_mat * (a / b), vec3(0.0), vec3(1.0));
}

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float angle = atan(p.y, p.x);
	float edge = length(p) * (1.0 + 0.10 * sin(angle * 5.0 + UV2.x) + 0.07 * sin(angle * 3.0 - UV2.x));
	float alpha = (1.0 - smoothstep(0.12, 1.0, edge)) * UV2.y;
	if (alpha < 0.003) discard;
	vec3 mapped = to_srgb(aces(COLOR.rgb));
	mapped = mix(mapped, vec3(179.0,201.0,208.0)/255.0, smoothstep(170.0,580.0,-VERTEX.z));
	ALBEDO = OUTPUT_IS_SRGB ? mapped : to_linear(mapped);
	ALPHA = alpha;
}

