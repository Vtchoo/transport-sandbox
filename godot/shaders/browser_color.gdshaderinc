// Three.js r180 color transfer and ACES filmic mapping (MIT; assets/menu/README.md).
vec3 browser_linear(vec3 c) {
    return mix(pow(c * 0.9478672986 + vec3(0.0521327014), vec3(2.4)), c * 0.0773993808, lessThanEqual(c, vec3(0.04045)));
}
vec3 browser_srgb(vec3 c) {
    return mix(pow(max(c, vec3(0.0)), vec3(0.41666)) * 1.055 - vec3(0.055), c * 12.92, lessThanEqual(c, vec3(0.0031308)));
}
vec3 browser_aces(vec3 c) {
    const mat3 input_mat = mat3(vec3(0.59719, 0.07600, 0.02840), vec3(0.35458, 0.90834, 0.13383), vec3(0.04823, 0.01566, 0.83777));
    const mat3 output_mat = mat3(vec3(1.60475, -0.10208, -0.00327), vec3(-0.53108, 1.10813, -0.07276), vec3(-0.07367, -0.00605, 1.07602));
    vec3 v = input_mat * (c * (1.05 / 0.6));
    vec3 a = v * (v + 0.0245786) - 0.000090537;
    vec3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return clamp(output_mat * (a / b), vec3(0.0), vec3(1.0));
}
