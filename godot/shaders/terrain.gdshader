shader_type spatial;
render_mode cull_disabled;
uniform bool contours = false;
uniform float contour_interval = 10.0;
varying float elevation;
float contour_line(float h, float width) {
    float pixel = max(fwidth(h), 0.00001);
    float distance_to_level = abs(fract(h + 0.5) - 0.5);
    return (1.0 - smoothstep(pixel * (width - 0.45), pixel * (width + 0.45), distance_to_level))
        * (1.0 - smoothstep(0.15, 0.45, pixel)) * smoothstep(0.00001, 0.0001, pixel);
}
void vertex() {
	elevation = VERTEX.y;
}
void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 0.95;
	if (contours) {
		float h = elevation / contour_interval;
		float minor_line = contour_line(h, 0.8);
		float major_line = contour_line(h / 5.0, 1.35);
		ALBEDO = mix(ALBEDO, vec3(0.075, 0.10, 0.065), max(minor_line * 0.65, major_line * 0.9));
	}
}
