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
	vec4 texel = has_texture ? texture(color_texture, UV) : vec4(1.0);
	vec3 color = base_color * COLOR.rgb * to_linear(texel.rgb);
	vec3 outgoing = color;
	if (!unlit) {
		vec3 n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
		vec3 v = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz);
		vec3 l = normalize(vec3(-60.0, 90.0, 45.0));
		vec3 h = normalize(l + v);
		float nl = clamp(dot(n, l), 0.0, 1.0);
		float nv = clamp(dot(n, v), 0.0, 1.0);
		float nh = clamp(dot(n, h), 0.0, 1.0);
		float vh = clamp(dot(v, h), 0.0, 1.0);
		vec3 derivatives = max(abs(dFdx(NORMAL)), abs(dFdy(NORMAL)));
		float roughness = min(max(roughness_value, 0.0525) + max(max(derivatives.x, derivatives.y), derivatives.z), 1.0);
		float a2 = pow(roughness, 4.0);
		float gv = nl * sqrt(a2 + (1.0 - a2) * nv * nv);
		float gl = nv * sqrt(a2 + (1.0 - a2) * nl * nl);
		float visibility = 0.5 / max(gv + gl, 0.000001);
		float denominator = nh * nh * (a2 - 1.0) + 1.0;
		float distribution = 0.31830988618 * a2 / (denominator * denominator);
		vec3 f0 = mix(vec3(0.04), color, metalness_value);
		float fresnel_weight = exp2((-5.55473 * vh - 6.98316) * vh);
		vec3 fresnel = f0 * (1.0 - fresnel_weight) + vec3(fresnel_weight);
		vec3 diffuse = color * (1.0 - metalness_value) * 0.31830988618;
		vec3 hemisphere = mix(to_linear(vec3(108.0,113.0,77.0)/255.0), to_linear(vec3(230.0,242.0,255.0)/255.0), n.y * 0.5 + 0.5) * 2.6;
		vec3 sunlight = to_linear(vec3(255.0,221.0,169.0)/255.0) * 3.2;
		outgoing = diffuse * hemisphere + nl * sunlight * (diffuse + fresnel * visibility * distribution) + emission_color;
	}
	vec3 mapped = to_srgb(aces(outgoing));
	// Three uploads fog in the renderer's output color space.
	mapped = mix(mapped, vec3(179.0,201.0,208.0)/255.0, smoothstep(170.0, 580.0, -VERTEX.z));
	ALBEDO = OUTPUT_IS_SRGB ? mapped : to_linear(mapped);
	/*MENU_ALPHA*/
}
