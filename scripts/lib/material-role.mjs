/** Strip only the trailing authoring UV group; keep the material role intact. */
export function materialRole(name) {
  // SketchUp COLLADA sanitizes brackets to underscores: paint-[front] -> paint-_front_.
  return name.replace(/(?:-\[[^\[\]]+\]|-_[^\[\]]+_)\s*$/, '').trim();
}
