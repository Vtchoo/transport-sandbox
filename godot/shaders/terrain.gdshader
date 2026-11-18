shader_type spatial;
render_mode cull_disabled;
uniform bool contours = false;
uniform float contour_interval = 10.0;
varying float elevation;
void vertex() {
	elevation = VERTEX.y;
}
void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 0.95;
	if (contours) {
		float h = elevation / contour_interval;
		float width = max(fwidth(h), 0.00001);
		float line = 1.0 - smoothstep(width * 0.5, width * 1.4, abs(fract(h + 0.5) - 0.5));
		ALBEDO = mix(ALBEDO, vec3(0.04, 0.06, 0.03), line * 0.85);
	}
}
