import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const directory = path.dirname(fileURLToPath(import.meta.url));
const mean = values => values.length ? values.reduce((a, b) => a + b, 0) / values.length : null;
const output = [];
const baselineRows = new Map();
let baselineFrames = 0;
for (const file of (await fs.readdir(directory)).filter(name => name.endsWith('.jsonl')).sort()) {
  const records = (await fs.readFile(path.join(directory, file), 'utf8')).trim().split('\n').filter(Boolean).map(JSON.parse);
  // SetBeltTickCounts counts only render frames in which a simulation step ran.
  // PipeWorld has exactly one Render Submit scope per LateUpdate, including pause.
  const snapshots = records.filter(r => r.perf).map(r => r.perf).filter(p =>
    (p.rows.find(r => r.itemName === 'Pipe Render Submit')?.samples ?? 0) >= 15);
  const frames = snapshots.reduce((sum, p) => sum + p.rows.find(r => r.itemName === 'Pipe Render Submit').samples, 0);
  const rows = new Map();
  const counters = {};
  for (const snapshot of snapshots) {
    for (const row of snapshot.rows) {
      const key = `${row.kind}/${row.itemName}`;
      const entry = rows.get(key) ?? { key, totalUs: 0, samples: 0, maxUs: 0 };
      entry.totalUs += row.totalUs;
      entry.samples += row.samples;
      entry.maxUs = Math.max(entry.maxUs, row.maxUs);
      rows.set(key, entry);
    }
    for (const counter of snapshot.runtimeCounters) {
      (counters[`${counter.group}/${counter.name}`] ??= []).push(counter.value);
    }
  }
  if (file.startsWith('baseline-')) {
    baselineFrames += frames;
    for (const [key, row] of rows) {
      const entry = baselineRows.get(key) ?? { key, totalUs: 0, samples: 0, maxUs: 0 };
      entry.totalUs += row.totalUs;
      entry.samples += row.samples;
      entry.maxUs = Math.max(entry.maxUs, row.maxUs);
      baselineRows.set(key, entry);
    }
  }
  output.push({
    phase: file.replace('.jsonl', ''), samples: records.length,
    meanFps: mean(records.map(r => Number(r.status.fps))),
    meanFrameMs: mean(records.map(r => Number(r.status.frameMs))),
    meanUps: mean(records.map(r => Number(r.status.ups))),
    maxSampleFrameMs: Math.max(...records.map(r => Number(r.status.frameMs))),
    // These are half-second averages, not individual-frame percentiles.
    positions: [...new Set(records.map(r => `${r.status.playerX},${r.status.playerZ}`))],
    minBeltItems: Math.min(...records.map(r => Number(r.status.beltItems))),
    maxBeltItems: Math.max(...records.map(r => Number(r.status.beltItems))),
    validProfileWindows: snapshots.length, profiledRenderFrames: frames,
    rows: [...rows.values()].sort((a, b) => b.totalUs - a.totalUs).map(r => ({ ...r, msPerRenderFrame: r.totalUs / 1000 / frames })),
    counters,
  });
}
const pooledBaseline = {
  profiledRenderFrames: baselineFrames,
  rows: [...baselineRows.values()].sort((a, b) => b.totalUs - a.totalUs).map(r => ({ ...r, msPerRenderFrame: r.totalUs / 1000 / baselineFrames })),
};
await fs.writeFile(path.join(directory, 'summary.json'), JSON.stringify({ phases: output, pooledBaseline }, null, 2));
console.log(JSON.stringify({ phases: output.map(({ rows, counters, ...rest }) => rest), pooledBaseline }, null, 2));
