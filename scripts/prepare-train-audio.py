"""Prepare replaceable test audio: mono, peak-normalized CLW loops and procedural placeholders."""
import json
import math
from pathlib import Path
import struct
import wave

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / '.files/locomotion/extracted-audio'
OBJ_SOURCE = ROOT / '.files/locomotion/objdata-audio'
TARGET = ROOT / 'godot/assets/audio/trains/testing'
TARGET.mkdir(parents=True, exist_ok=True)


def write(name, samples, rate):
    peak = max(max(map(abs, samples)), .001)
    # Leave 6 dB of asset headroom before the runtime mixer gains.
    pcm = [round(v / peak * 16383) for v in samples]
    with wave.open(str(TARGET / (name + '.wav')), 'wb') as out:
        out.setparams((1, 2, rate, 0, 'NONE', 'not compressed'))
        out.writeframes(struct.pack('<' + 'h' * len(pcm), *pcm))


manifest = json.loads((SOURCE / 'manifest.json').read_text())
obj_manifest = json.loads((OBJ_SOURCE / 'manifest.json').read_text())
provenance = {}
for name, identifier in {'f7': 'SNDFP7FA', 'dash9': 'SNDDSH9E',
                         'electric': 'SNDACLEN',
                         'wheels-freight-1': 'SNDFRGH2', 'wheels-freight-2': 'SNDFRGHT',
                         'steam-idle': 'SNDS1',
                         'chuff-a': 'SNDCH1A', 'chuff-b': 'SNDCH1B',
                         'chuff-c': 'SNDCH1C', 'chuff-d': 'SNDCH1D',
                         'horn-f7': 'SNDFP7HO', 'horn-ge': 'SND1CHIM',
                         'horn-electric': 'SNDACLHO', 'whistle-steam': 'SND412W1'}.items():
    source = OBJ_SOURCE if identifier == 'SNDS1' or identifier.startswith('SNDCH') else SOURCE
    data = obj_manifest if source == OBJ_SOURCE else manifest
    entry = next(s for s in data['sounds'] if s['id'] == identifier)
    with wave.open(str(source / entry['wav']), 'rb') as audio:
        channels, width, rate, count, _, _ = audio.getparams()
        raw = audio.readframes(count)
        values = []
        for i in range(0, len(raw), width * channels):
            total = 0
            for c in range(channels):
                chunk = raw[i + c * width:i + (c + 1) * width]
                total += chunk[0] - 128 if width == 1 else int.from_bytes(chunk, 'little', signed=True)
            values.append(total / channels)
        if name.startswith(('horn-', 'chuff-')) or name == 'whistle-steam':
            # Horns are one-shots, even when the source object marks them as loops.
            fade = min(int(rate * .01), len(values) // 8)
            for i in range(fade):
                values[i] *= i / fade
                values[-1 - i] *= i / fade
        else:
            # Crossfade the loop boundary, retaining most of the original recording.
            fade = min(int(rate * .04), len(values) // 8)
            tail = values[-fade:]
            values = values[:-fade]
            for i in range(fade):
                weight = i / fade
                values[i] = tail[i] * (1 - weight) + values[i] * weight
        write(name, values, rate)
    provenance[name] = {'object': identifier, 'source': entry['wav'], 'name': entry['name'], 'pack': source.name}

rate = 22050
samples = []
for i in range(rate * 4):
    t = i / rate
    # Stylized flange whine, explicitly a placeholder rather than a recording.
    value = (math.sin(2 * math.pi * 920 * t + .8 * math.sin(2 * math.pi * 3 * t))
             + .3 * math.sin(2 * math.pi * 1840 * t)) * (.8 + .2 * math.sin(2 * math.pi * 7 * t))
    samples.append(value * min(1, t / .025, (4 - t) / .025))
write('curve', samples, rate)
(TARGET / 'sources.json').write_text(json.dumps(provenance, indent=2) + '\n')
print('Prepared mono train test loops and horn/whistle one-shots.')
