shader_type spatial;
render_mode ambient_light_disabled, specular_disabled, fog_disabled, depth_draw_always;
#include "res://shaders/world_surface.gdshaderinc"
varying vec3 indirect_light;
varying vec3 direct_light;
varying float fog_depth;
void fragment() {
    vec3 n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec3 v = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz);
    vec3 derivatives = max(abs(dFdx(NORMAL)), abs(dFdy(NORMAL)));
    float roughness = min(0.28 + max(max(derivatives.x, derivatives.y), derivatives.z), 1.0);
    vec3 color = browser_linear(vec3(67.0, 124.0, 133.0) / 255.0);
    indirect_light = browser_world_ambient(color, 0.25, n);
    direct_light = browser_world_light(color, roughness, 0.25, n, v) - indirect_light;
    fog_depth = -VERTEX.z;
    ALBEDO = vec3(1.0);
    SPECULAR = 0.0;
    ALPHA = 0.88;
}
void light() {
    if (LIGHT_IS_DIRECTIONAL) {
        DIFFUSE_LIGHT += browser_world_lit_output(indirect_light + direct_light * ATTENUATION, fog_depth);
    }
}
