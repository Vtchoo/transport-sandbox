import type * as THREE from 'three';
import { Trees, type TreeChunk } from './trees';
import type { PreparedTrees } from './tree-data';

interface Entry { visual: TreeChunk; active: boolean; used: number }
/** Coordinate cache independent of terrain tessellation. All mutation stays on the render thread. */
export class TreeStreaming {
  private entries = new Map<string, Entry>();
  private retired: TreeChunk[] = [];
  private clock = 0;
  budgetBytes = 32 * 1024 * 1024;
  uploads = 0;
  reuses = 0;
  constructor(private renderer: Trees) {}
  get cachedBytes() {
    let bytes = 0;
    for (const entry of this.entries.values()) if (!entry.active) bytes += entry.visual.prepared.bytes * 3;
    return bytes;
  }
  get pendingDisposals() { return this.retired.length; }
  activate(key: string, holder: THREE.Object3D, prepared: PreparedTrees, hidden: boolean) {
    let entry = this.entries.get(key);
    if (entry) {
      const previous = entry.visual.prepared;
      const compatible = previous.batches.length === prepared.batches.length && previous.batches.every((batch, i) =>
        batch.species === prepared.batches[i].species && batch.trunks.length === prepared.batches[i].trunks.length);
      if (compatible) {
        // Cached returns need no buffer copies or uploads. Other LODs may change root elevations.
        if (previous !== prepared) { this.renderer.replace(entry.visual, prepared); this.uploads++; }
        this.reuses++;
      } else {
        const replacement = this.renderer.createPrepared(prepared);
        entry.visual.group.removeFromParent();
        this.retired.push(entry.visual);
        entry.visual = replacement;
        this.uploads++;
      }
    } else {
      entry = { visual: this.renderer.createPrepared(prepared), active: true, used: 0 };
      this.entries.set(key, entry); this.uploads++;
    }
    entry.active = true; entry.used = ++this.clock;
    holder.add(entry.visual.group);
    entry.visual.group.visible = !hidden;
    return entry.visual;
  }
  deactivate(key: string) {
    const entry = this.entries.get(key);
    if (!entry) return;
    entry.active = false; entry.used = ++this.clock;
    entry.visual.group.removeFromParent();
    this.trim();
  }
  private trim() {
    let bytes = this.cachedBytes;
    const inactive = [...this.entries].filter(([, entry]) => !entry.active).sort((a, b) => a[1].used - b[1].used);
    let count = inactive.length;
    for (const [key, entry] of inactive) {
      if (bytes <= this.budgetBytes && count <= 128) break;
      this.entries.delete(key); bytes -= entry.visual.prepared.bytes * 3; count--;
      this.retired.push(entry.visual);
    }
  }
  releaseOne() {
    const chunk = this.retired.shift();
    if (!chunk) return false;
    this.renderer.release(chunk); return true;
  }
  dispose() {
    for (const entry of this.entries.values()) this.renderer.release(entry.visual);
    this.entries.clear();
    while (this.releaseOne()) { /* Shutdown must release all instance buffers. */ }
  }
}
