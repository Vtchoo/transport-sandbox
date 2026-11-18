export interface TerrainCacheEntry { x: number; z: number; segments: number; revision: number; bytes: number }

/** Inactive LODs only; taking an entry transfers ownership to the visible world. */
export class TerrainLodCache<T extends TerrainCacheEntry> {
  private entries = new Map<string, T>();
  private budget = 128 * 1024 * 1024;
  bytes = 0;
  hits = 0;
  evictions = 0;
  constructor(private release: (entry: T) => void) {}
  get count() { return this.entries.size; }
  get budgetBytes() { return this.budget; }
  set budgetBytes(value: number) { this.budget = Math.max(0, value); this.trim(); }
  private key(x: number, z: number, segments: number, revision: number) { return `${x},${z}/${segments}/${revision}`; }
  store(entry: T) {
    const key = this.key(entry.x, entry.z, entry.segments, entry.revision);
    const previous = this.entries.get(key);
    if (previous === entry) return;
    if (previous) this.remove(key, previous);
    if (!this.budget || entry.bytes > this.budget) { this.release(entry); return; }
    this.entries.set(key, entry); this.bytes += entry.bytes; this.trim();
  }
  take(x: number, z: number, segments: number, revision: number) {
    const key = this.key(x, z, segments, revision), entry = this.entries.get(key);
    if (!entry) return;
    this.entries.delete(key); this.bytes -= entry.bytes; this.hits++; return entry;
  }
  advanceRevision(revision: number, affected: (x: number, z: number) => boolean) {
    const previous = this.entries; this.entries = new Map(); this.bytes = 0;
    for (const entry of previous.values()) {
      if (affected(entry.x, entry.z)) this.release(entry);
      else { entry.revision = revision; this.entries.set(this.key(entry.x, entry.z, entry.segments, revision), entry); this.bytes += entry.bytes; }
    }
  }
  private remove(key: string, entry: T) { this.entries.delete(key); this.bytes -= entry.bytes; this.release(entry); }
  private trim() {
    while (this.entries.size > 256 || this.bytes > this.budget) {
      const first = this.entries.entries().next().value;
      if (!first) break;
      this.remove(...first); this.evictions++;
    }
  }
  clear() { for (const entry of this.entries.values()) this.release(entry); this.entries.clear(); this.bytes = 0; }
}
