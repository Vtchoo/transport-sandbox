shader_type spatial;
render_mode cull_disabled;
uniform vec4 base_color : source_color;
uniform vec4 profile;
uniform float cargo_length;
uniform float tile_metres = 1.0;
uniform float texture_distance = 250.0;
uniform float detail_distance = 70.0;
uniform float displacement_metres = 0.0;
uniform float normal_strength = 1.0;
uniform bool has_albedo_map = false;
uniform bool has_height_map = false;
uniform bool has_normal_map = false;
uniform sampler2D albedo_map : source_color, repeat_enable, filter_linear_mipmap;
uniform sampler2D height_map : repeat_enable, filter_linear_mipmap;
uniform sampler2D normal_map : hint_normal, repeat_enable, filter_linear_mipmap;
varying float camera_distance;
varying float lid;
void vertex() {
    float height = clamp(INSTANCE_CUSTOM.r, 0.0, 1.0);
    float level = VERTEX.y * height;
    VERTEX.x *= mix(profile.z, profile.w, level);
    VERTEX.y = mix(profile.x, profile.y, level);
    VERTEX.z *= cargo_length;
    camera_distance = length((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz);
    lid = step(0.9, NORMAL.y);
    UV = vec2(VERTEX.x, VERTEX.z) / max(tile_metres, 0.01);
    if (has_height_map && lid > 0.5 && camera_distance < detail_distance) {
        // Keep the lid attached to its walls; fade detail rather than popping at the LOD boundary.
        float width = mix(profile.z, profile.w, height);
        float edge = smoothstep(0.0, 0.15, min(width * 0.5 - abs(VERTEX.x), cargo_length * 0.5 - abs(VERTEX.z)));
        VERTEX.y += (textureLod(height_map, UV, 0.0).r - 0.5) * displacement_metres * edge * height * (1.0 - smoothstep(detail_distance * 0.7, detail_distance, camera_distance));
    }
    TANGENT = vec3(1.0, 0.0, 0.0);
    BINORMAL = vec3(0.0, 0.0, 1.0);
}
void fragment() {
    vec3 color = base_color.rgb;
    if (has_albedo_map && camera_distance < texture_distance) {
        float amount = 1.0 - smoothstep(texture_distance * 0.7, texture_distance, camera_distance);
        color = mix(color, texture(albedo_map, UV).rgb, amount);
    }
    ALBEDO = color;
    ROUGHNESS = 0.95;
    if (has_normal_map && lid > 0.5 && camera_distance < detail_distance) {
        NORMAL_MAP = texture(normal_map, UV).rgb;
        NORMAL_MAP_DEPTH = normal_strength * (1.0 - smoothstep(detail_distance * 0.7, detail_distance, camera_distance));
    }
    // PREVIEW
}
