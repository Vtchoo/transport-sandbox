import type { TrackNetwork, TrackSegment } from './model';

export interface StationEnd { edge: number; u: 0 | 1 }
export interface StationRun { station: number; length: number; ends: StationEnd[] }

/** Separate platform runs: parallel tracks and the two visual sides never add to usable length. */
export function stationRuns(network: TrackNetwork, station?: number): StationRun[] {
  const remaining = new Map([...network.segments].filter(([, edge]) => edge.station && (station === undefined || edge.station.id === station)));
  const result: StationRun[] = [];
  while (remaining.size) {
    const first = remaining.values().next().value!;
    const run: StationRun = { station: first.station!.id, length: 0, ends: [] };
    const pending: TrackSegment[] = [first]; remaining.delete(first.id);
    while (pending.length) {
      const edge = pending.pop()!, curve = network.curve(edge); curve.arcLengthDivisions = 512;
      run.length += curve.getLength();
      for (const u of [0, 1] as const) {
        const node = u === 0 ? edge.a : edge.b, side = u === 0 ? edge.aSide : edge.bSide;
        const neighbours = network.incident(node).filter(other => other.id !== edge.id && other.station?.id === run.station &&
          other.gauge === edge.gauge && (other.a === node ? other.aSide : other.bSide) !== side);
        if (!neighbours.length) run.ends.push({ edge: edge.id, u });
        for (const other of neighbours) if (remaining.delete(other.id)) pending.push(other);
      }
    }
    result.push(run);
  }
  return result;
}

export function stationLengthLabel(network: TrackNetwork, station: number) {
  const lengths = stationRuns(network, station).map(run => run.length);
  if (!lengths.length) return 'Station length: 0 m';
  const shortest = Math.min(...lengths), longest = Math.max(...lengths);
  return `Station length: ${Math.abs(longest - shortest) < 0.05 ? longest.toFixed(1) : `${shortest.toFixed(1)}–${longest.toFixed(1)}`} m`;
}
