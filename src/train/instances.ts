import * as THREE from "three";
import { mergeGeometries } from "three/addons/utils/BufferGeometryUtils.js";
import type { VehicleDefinition } from "./types";
import type { VehicleVisual } from "./render";

const CELL_SIZE = 512;
interface Part {
  kind: "body" | "bogie" | "bogie-front" | "bogie-rear" | "moving";
  movingIndex?: number;
  geometry: THREE.BufferGeometry;
  material: THREE.Material;
}
interface Template {
  parts: Part[];
}
interface Batch {
  template: Template;
  meshes: THREE.InstancedMesh[];
  capacity: number;
}

/** Repeated models share merged parts; cells keep distant groups independently frustum-culled. */
export class VehicleInstances {
  readonly group = new THREE.Group();
  private templates = new Map<string, Template>();
  private batches = new Map<string, Batch>();
  private hidden = new Map<THREE.Group, boolean>();
  private definitionIds = new WeakMap<VehicleDefinition, number>();
  private nextDefinition = 0;
  private inverse = new THREE.Matrix4();
  private matrix = new THREE.Matrix4();

  constructor() {
    this.group.name = "vehicle-instances";
  }

  private modelKey(visual: VehicleVisual) {
    let id = this.definitionIds.get(visual.definition);
    if (id === undefined) {
      id = this.nextDefinition++;
      this.definitionIds.set(visual.definition, id);
    }
    return `${id}:${visual.gauge}`;
  }

  private makeTemplate(visual: VehicleVisual): Template {
    const parts: Part[] = [];
    const moving = new Set(visual.motion?.parts ?? []);
    const components: { kind: Part['kind']; component: THREE.Group; movingIndex?: number }[] = [
      { kind: 'body', component: visual.body },
      ...(visual.separateBogies ? [
        { kind: 'bogie-front' as const, component: visual.bogies[0] },
        { kind: 'bogie-rear' as const, component: visual.bogies[1] },
      ] : [{ kind: 'bogie' as const, component: visual.bogies[0] }]),
      ...(visual.motion?.parts.map((component, movingIndex) => ({ kind: 'moving' as const, component, movingIndex })) ?? []),
    ];
    for (const { kind, component, movingIndex } of components) {
      component.updateWorldMatrix(true, true);
      const inverse = component.matrixWorld.clone().invert();
      const groups = new Map<THREE.Material, THREE.BufferGeometry[]>();
      component.traverse((object) => {
        if (!(object instanceof THREE.Mesh)) return;
        // Moving geometry belongs to its own template, never baked into the static body.
        if (kind === 'body') for (let parent = object.parent; parent && parent !== component; parent = parent.parent) {
          if (moving.has(parent as THREE.Group)) return;
        }
        if (Array.isArray(object.material))
          throw new Error(
            "Vehicle batching requires one material per source mesh.",
          );
        const geometry: THREE.BufferGeometry = object.geometry.index
          ? object.geometry.toNonIndexed()
          : object.geometry.clone();
        const transform = new THREE.Matrix4().multiplyMatrices(
          inverse,
          object.matrixWorld,
        );
        geometry.applyMatrix4(transform);
        // Mirrored model details previously relied on the mesh's negative determinant for face culling.
        if (transform.determinant() < 0)
          for (const attribute of Object.values(geometry.attributes)) {
            for (let vertex = 0; vertex < attribute.count; vertex += 3)
              for (
                let component = 0;
                component < attribute.itemSize;
                component++
              ) {
                const value = attribute.getComponent(vertex + 1, component);
                attribute.setComponent(
                  vertex + 1,
                  component,
                  attribute.getComponent(vertex + 2, component),
                );
                attribute.setComponent(vertex + 2, component, value);
              }
          }
        // Primitive and custom shells have different optional attributes. Only mapped labels need UVs.
        const mapped = !!(object.material as THREE.MeshBasicMaterial).map;
        for (const attribute of Object.keys(geometry.attributes))
          if (
            !["position", "normal", ...(mapped ? ["uv"] : [])].includes(
              attribute,
            )
          )
            geometry.deleteAttribute(attribute);
        if (!geometry.getAttribute("normal")) geometry.computeVertexNormals();
        const geometries = groups.get(object.material) ?? [];
        geometries.push(geometry);
        groups.set(object.material, geometries);
      });
      for (const [source, geometries] of groups) {
        const geometry = mergeGeometries(geometries);
        geometries.forEach((item) => item.dispose());
        if (!geometry)
          throw new Error("Incompatible vehicle geometry attributes.");
        const material = source.clone(),
          mapped = material as THREE.MeshBasicMaterial;
        // Templates own their resources, so deleting the source car cannot invalidate surviving instances.
        if (mapped.map) mapped.map = mapped.map.clone();
        parts.push({ kind, movingIndex, geometry, material });
      }
    }
    return { parts };
  }

  sync(vehicles: readonly VehicleVisual[]) {
    const leaves = (visual: VehicleVisual): VehicleVisual[] => visual.sections ? visual.sections.flatMap(leaves) : [visual];
    const visuals = vehicles.flatMap(leaves);
    this.group.updateWorldMatrix(true, false);
    const toLocal = this.group.matrixWorld.clone().invert();
    const position = new THREE.Vector3();
    const retained = new Set(visuals.map((visual) => visual.group));
    for (const [group, visible] of this.hidden)
      if (!retained.has(group)) {
        group.visible = visible;
        this.hidden.delete(group);
      }
    const cells = new Map<
      string,
      { template: Template; visuals: VehicleVisual[]; x: number; z: number }
    >();
    const usedModels = new Set<string>();
    for (const visual of visuals) {
      if (!this.hidden.has(visual.group))
        this.hidden.set(visual.group, visual.group.visible);
      const visible = this.hidden.get(visual.group);
      visual.group.visible = false;
      if (!visible) continue;
      const model = this.modelKey(visual);
      usedModels.add(model);
      let template = this.templates.get(model);
      if (!template) {
        template = this.makeTemplate(visual);
        this.templates.set(model, template);
      }
      visual.group.updateWorldMatrix(true, true);
      position.setFromMatrixPosition(visual.group.matrixWorld).applyMatrix4(toLocal);
      const x = Math.floor(position.x / CELL_SIZE),
        z = Math.floor(position.z / CELL_SIZE);
      const key = `${model}:${x}:${z}`;
      let cell = cells.get(key);
      if (!cell) {
        cell = { template, visuals: [], x, z };
        cells.set(key, cell);
      }
      cell.visuals.push(visual);
      visual.group.updateWorldMatrix(true, true);
    }
    for (const [key, batch] of this.batches)
      if (!cells.has(key)) {
        this.disposeBatch(batch);
        this.batches.delete(key);
      }
    for (const [key, cell] of cells) {
      let batch = this.batches.get(key);
      if (!batch || batch.capacity < cell.visuals.length) {
        if (batch) this.disposeBatch(batch);
        const capacity =
          2 ** Math.ceil(Math.log2(Math.max(4, cell.visuals.length)));
        const meshes = cell.template.parts.map((part) => {
          const mesh = new THREE.InstancedMesh(
            part.geometry,
            part.material,
            capacity * (part.kind === "bogie" ? 2 : 1),
          );
          // Instance translations stay small even millions of metres from world zero.
          mesh.position.set(cell.x * CELL_SIZE, 0, cell.z * CELL_SIZE);
          mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
          mesh.name = `${key}:${part.kind}${part.kind === 'moving' ? ':' + part.movingIndex : ''}`;
          this.group.add(mesh);
          return mesh;
        });
        batch = { template: cell.template, meshes, capacity };
        this.batches.set(key, batch);
      }
      batch.template.parts.forEach((part, index) => {
        const mesh = batch!.meshes[index];
        let count = 0;
        mesh.updateWorldMatrix(true, false);
        this.inverse.copy(mesh.matrixWorld).invert();
        for (const visual of cell.visuals)
          for (const component of part.kind === "body"
            ? [visual.body]
            : part.kind === 'moving' ? [visual.motion!.parts[part.movingIndex!]]
            : part.kind === 'bogie-front' ? [visual.bogies[0]]
            : part.kind === 'bogie-rear' ? [visual.bogies[1]] : visual.bogies) {
            this.matrix.multiplyMatrices(this.inverse, component.matrixWorld);
            mesh.setMatrixAt(count++, this.matrix);
          }
        mesh.count = count;
        mesh.instanceMatrix.needsUpdate = true;
        mesh.computeBoundingSphere();
      });
    }
    for (const [key, template] of this.templates)
      if (!usedModels.has(key)) {
        this.disposeTemplate(template);
        this.templates.delete(key);
      }
  }

  private disposeBatch(batch: Batch) {
    for (const mesh of batch.meshes) {
      this.group.remove(mesh);
      mesh.dispose();
    }
  }
  private disposeTemplate(template: Template) {
    for (const part of template.parts) {
      part.geometry.dispose();
      (part.material as THREE.MeshBasicMaterial).map?.dispose();
      part.material.dispose();
    }
  }
  dispose() {
    for (const [group, visible] of this.hidden) group.visible = visible;
    this.hidden.clear();
    this.batches.forEach((batch) => this.disposeBatch(batch));
    this.batches.clear();
    this.templates.forEach((template) => this.disposeTemplate(template));
    this.templates.clear();
    this.group.removeFromParent();
  }
}
