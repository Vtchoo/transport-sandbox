/** Replace/insert a catalog object without reformatting unrelated entries. */
export function updateArrayEntry(raw, id, value, afterId) {
  const entries = []; let depth = 0, start = 0, quoted = false, escaped = false;
  for (let i = 0; i < raw.length; i++) {
    const c = raw[i];
    if (quoted) { if (escaped) escaped = false; else if (c === '\\') escaped = true; else if (c === '"') quoted = false; continue; }
    if (c === '"') { quoted = true; continue; }
    if (c === '{') { if (depth++ === 0) start = i; }
    else if (c === '}' && --depth === 0) entries.push({ start, end: i + 1, value: JSON.parse(raw.slice(start, i + 1)) });
  }
  const json = JSON.stringify(value, null, 2).split('\n').map((line, i) => i ? '  ' + line : line).join('\n');
  const existing = entries.find(e => e.value.id === id);
  if (existing) return raw.slice(0, existing.start) + json + raw.slice(existing.end);
  const after = entries.find(e => e.value.id === afterId);
  if (!after) throw Error(`Cannot find insertion anchor ${afterId}`);
  return raw.slice(0, after.end) + ',\n  ' + json + raw.slice(after.end);
}
