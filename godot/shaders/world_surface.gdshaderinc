#include "res://shaders/browser_color.gdshaderinc"

// The original gameplay HemisphereLight + DirectionalLight, without an environment map.
vec3 browser_world_ambient(vec3 color, float metalness, vec3 n) {
    vec3 diffuse = color * (1.0 - metalness) * 0.31830988618;
    vec3 hemisphere = mix(browser_linear(vec3(123.0, 113.0, 87.0) / 255.0), browser_linear(vec3(214.0, 234.0, 255.0) / 255.0), n.y * 0.5 + 0.5) * 2.4;
    return diffuse * hemisphere;
}
vec3 browser_world_light(vec3 color, float roughness, float metalness, vec3 n, vec3 v) {
    vec3 l = normalize(vec3(-0.7, 0.8, -0.4));
    vec3 h = normalize(l + v);
    float nl = clamp(dot(n, l), 0.0, 1.0);
    float nv = clamp(dot(n, v), 0.0, 1.0);
    float nh = clamp(dot(n, h), 0.0, 1.0);
    float vh = clamp(dot(v, h), 0.0, 1.0);
    float a2 = pow(roughness, 4.0);
    float gv = nl * sqrt(a2 + (1.0 - a2) * nv * nv);
    float gl = nv * sqrt(a2 + (1.0 - a2) * nl * nl);
    float visibility = 0.5 / max(gv + gl, 0.000001);
    float denominator = nh * nh * (a2 - 1.0) + 1.0;
    float distribution = 0.31830988618 * a2 / (denominator * denominator);
    vec3 f0 = mix(vec3(0.04), color, metalness);
    float fresnel_weight = exp2((-5.55473 * vh - 6.98316) * vh);
    vec3 fresnel = f0 * (1.0 - fresnel_weight) + vec3(fresnel_weight);
    vec3 diffuse = color * (1.0 - metalness) * 0.31830988618;
    vec3 sunlight = browser_linear(vec3(255.0, 239.0, 211.0) / 255.0) * 2.7;
    return browser_world_ambient(color, metalness, n) + nl * sunlight * (diffuse + fresnel * visibility * distribution);
}
vec3 browser_world_output(vec3 outgoing, float depth) {
    vec3 mapped = browser_srgb(browser_aces(outgoing));
    float fog = 1.0 - exp(-0.00034 * 0.00034 * depth * depth);
    mapped = mix(mapped, vec3(181.0, 206.0, 222.0) / 255.0, fog);
    return OUTPUT_IS_SRGB ? mapped : browser_linear(mapped);
}
// Custom light() results accumulate in linear RGB in every Godot renderer.
vec3 browser_world_lit_output(vec3 outgoing, float depth) {
    vec3 mapped = browser_world_output(outgoing, depth);
    return OUTPUT_IS_SRGB ? browser_linear(mapped) : mapped;
}
