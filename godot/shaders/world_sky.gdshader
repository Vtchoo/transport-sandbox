shader_type sky;
#include "res://shaders/browser_color.gdshaderinc"
uniform bool compatibility_output = true;
void sky() {
    vec3 d = normalize(EYEDIR);
    vec3 sun = normalize(vec3(-0.7, 0.8, -0.4));
    float h = pow(max(d.y, 0.0), 0.45);
    vec3 color = mix(vec3(0.68, 0.79, 0.86), vec3(0.18, 0.39, 0.64), h);
    float glow = pow(max(dot(d, sun), 0.0), 28.0);
    color += vec3(0.3, 0.22, 0.1) * glow;
    color += vec3(3.0, 2.5, 1.7) * smoothstep(0.99975, 0.9999, dot(d, sun));
    // GLES3's sky pass accepts sRGB before its internal linear conversion.
    // https://github.com/godotengine/godot/blob/master/drivers/gles3/shaders/sky.glsl
    vec3 mapped = browser_aces(color);
    COLOR = compatibility_output ? browser_srgb(mapped) : mapped;
}
