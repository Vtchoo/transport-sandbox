import { DOMParser } from 'linkedom';
import * as THREE from 'three';
import { ColladaLoader } from 'three/addons/loaders/ColladaLoader.js';
import { createHash } from 'node:crypto';

const round = n => Math.abs(n) < 1e-9 ? 0 : Number(n.toFixed(9));
const vector = v => v.toArray().map(round);
const semantic = name => name?.toLowerCase().replaceAll('_', '-');

/** Convert static, solid-color SketchUp COLLADA into synchronous, DOM-free vehicle data. */
export function convertColladaVehicle(text) {
  const document = new DOMParser().parseFromString(text, 'text/xml');
  if (!document.querySelector('COLLADA')) throw Error('Expected a COLLADA document.');
  const unit = Number(document.querySelector('asset unit')?.getAttribute('meter') ?? 1);
  if (!Number.isFinite(unit) || unit <= 0) throw Error('Invalid COLLADA length unit.');
  if (document.querySelector('asset up_axis')?.textContent.trim() !== 'Z_UP')
    throw Error('This SketchUp adapter expects Z_UP. Keep the source blue axis upward.');
  if (document.querySelector('library_images image'))
    throw Error('This first importer supports solid-color materials. Textured COLLADA needs a texture adapter.');
  if (document.querySelector('library_animations animation, library_controllers controller'))
    throw Error('Export static components; animation/controller imports are not supported yet.');

  // SketchUp stores component names on referenced library nodes. ColladaLoader flattens
  // those references, so transfer the semantic names onto their scene instances first.
  for (const node of document.querySelectorAll('library_visual_scenes node')) {
    const reference = Array.from(node.children).find(child => child.tagName === 'instance_node');
    if (!reference) continue;
    const target = document.getElementById(reference.getAttribute('url')?.slice(1));
    const name = semantic(target?.getAttribute('name'));
    if (['body', 'bogie-front', 'bogie-rear'].includes(name)) node.setAttribute('name', name);
  }
  const previousParser = globalThis.DOMParser;
  let scene;
  try {
    globalThis.DOMParser = DOMParser;
    scene = new ColladaLoader().parse(document.toString(), '').scene;
  } finally {
    if (previousParser === undefined) delete globalThis.DOMParser;
    else globalThis.DOMParser = previousParser;
  }
  const sourceParts = {};
  for (const name of ['body', 'bogie-front', 'bogie-rear']) {
    const matches = [];
    scene.traverse(object => { if (semantic(object.name) === name) matches.push(object); });
    if (matches.length !== 1) throw Error(`Expected exactly one ${name} component; found ${matches.length}.`);
    sourceParts[name] = matches[0];
  }
  const root = new THREE.Group(); root.add(scene); root.updateMatrixWorld(true);
  // ColladaLoader already applies the declared unit and Z-up rotation. Only yaw
  // when needed so the explicitly named front bogie is toward game +Z.
  let front = sourceParts['bogie-front'].getWorldPosition(new THREE.Vector3());
  let rear = sourceParts['bogie-rear'].getWorldPosition(new THREE.Vector3());
  if (front.z < rear.z) { root.rotation.y = Math.PI; root.updateMatrixWorld(true); }
  front = sourceParts['bogie-front'].getWorldPosition(new THREE.Vector3());
  rear = sourceParts['bogie-rear'].getWorldPosition(new THREE.Vector3());
  if (Math.abs(front.x) > .001 || Math.abs(rear.x) > .001 || Math.abs(front.y) > .001 || Math.abs(rear.y) > .001)
    throw Error('Bogie origins must be on the centerline at railhead level. Use Change Axes in SketchUp.');
  if (front.z <= rear.z || Math.abs(front.z + rear.z) > .001)
    throw Error('The current placement adapter requires symmetric front/rear bogie origins around world zero.');
  const bodyOrigin = sourceParts.body.getWorldPosition(new THREE.Vector3());
  if (bodyOrigin.length() > .001) throw Error('Body origin must be at the vehicle centerline / railhead datum (0,0,0).');

  const materials = [], materialIds = new Map();
  function materialIndex(source) {
    if (materialIds.has(source)) return materialIds.get(source);
    if (!source.color) throw Error('Expected a solid-color mesh material.');
    const id = materials.length;
    materials.push({ name: source.name || 'material', color: source.color.toArray().map(round),
      opacity: source.opacity, side: source.side });
    materialIds.set(source, id); return id;
  }
  const bounds = new THREE.Box3();
  function bake(component, origin) {
    const meshes = [];
    component.traverse(object => {
      if (!object.isMesh) return; // Omit SketchUp edges, cameras and helpers.
      const geometry = object.geometry.index ? object.geometry.toNonIndexed() : object.geometry.clone();
      const transform = new THREE.Matrix4().makeTranslation(-origin.x, -origin.y, -origin.z).multiply(object.matrixWorld);
      geometry.applyMatrix4(transform);
      if (!geometry.attributes.normal) geometry.computeVertexNormals();
      const available = Array.isArray(object.material) ? object.material : [object.material];
      const groups = geometry.groups.length ? geometry.groups : [{ start: 0, count: geometry.attributes.position.count, materialIndex: 0 }];
      for (const group of groups) {
        const material = available[group.materialIndex ?? 0];
        if (!material) throw Error('Mesh references a missing material.');
        const positions = [], normals = [];
        for (let start = group.start; start < group.start + group.count; start += 3) {
          // Baking mirrored component instances needs a winding correction.
          const order = transform.determinant() < 0 ? [0, 2, 1] : [0, 1, 2];
          const vertices = order.map(offset => new THREE.Vector3().fromBufferAttribute(geometry.attributes.position, start + offset));
          const normal = new THREE.Vector3().fromBufferAttribute(geometry.attributes.normal, start + order[0]);
          // SketchUp may export a mirrored definition with reversed triangles but
          // unchanged normals as well. Keep the baked front face aligned with normals.
          if (vertices[1].clone().sub(vertices[0]).cross(vertices[2].clone().sub(vertices[0])).dot(normal) < 0)
            [order[1], order[2]] = [order[2], order[1]];
          for (const offset of order) {
            const index = start + offset;
            const point = new THREE.Vector3().fromBufferAttribute(geometry.attributes.position, index);
            positions.push(...vector(point));
            normals.push(...vector(new THREE.Vector3().fromBufferAttribute(geometry.attributes.normal, index)));
            bounds.expandByPoint(point.add(origin));
          }
        }
        meshes.push({ name: object.name || component.name, material: materialIndex(material), positions, normals });
      }
      geometry.dispose();
    });
    if (!meshes.length) throw Error(`Component ${component.name} contains no triangle meshes.`);
    return meshes;
  }
  const parts = {
    body: bake(sourceParts.body, new THREE.Vector3()),
    front: bake(sourceParts['bogie-front'], front),
    rear: bake(sourceParts['bogie-rear'], rear),
  };
  for (const mesh of Object.values(parts).flat())
    if (![...mesh.positions, ...mesh.normals].every(Number.isFinite)) throw Error('Non-finite imported geometry.');
  return { version: 1, sourceSha256: createHash('sha256').update(text).digest('hex'), sourceUnitMeters: unit,
    sketchupToGame: root.rotation.y ? '(-X, Z, Y)' : '(X, Z, -Y)',
    bogieCenters: round(front.z - rear.z),
    bounds: { min: vector(bounds.min), max: vector(bounds.max), size: vector(bounds.getSize(new THREE.Vector3())) },
    materials, parts };
}
