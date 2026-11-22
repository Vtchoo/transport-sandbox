"""Find stereo silence gaps in float WAV captures and correlate them with a follow trace.

Usage: python scripts/analyze-audio-gaps.py capture.wav trace.json [loopback-metadata.json]
No third-party Python packages are required. Loopback alignment is approximate: callback
timestamps include output-device buffering. The master-bus capture is the primary clock.
"""
import array
from datetime import datetime
import json
from pathlib import Path
import struct
import sys


def read_wav(path):
    raw = Path(path).read_bytes()
    offset = 12
    fmt = pcm = None
    while offset + 8 <= len(raw):
        chunk, size = struct.unpack_from('<4sI', raw, offset)
        offset += 8
        if chunk == b'fmt ':
            fmt = struct.unpack_from('<HHIIHH', raw, offset)
            if fmt[0] == 65534:  # WAVE_FORMAT_EXTENSIBLE
                fmt = (struct.unpack_from('<H', raw, offset + 24)[0], *fmt[1:])
        if chunk == b'data':
            pcm = raw[offset:offset + size]
        offset += size + size % 2
    if fmt is None or pcm is None:
        raise ValueError('Missing WAV format/data chunk')
    code, channels, rate, _, _, bits = fmt
    if code == 3 and bits == 32:
        samples = array.array('f', pcm)
    elif code == 1 and bits == 16:
        samples = array.array('f', (n / 32768 for n in array.array('h', pcm)))
    else:
        raise ValueError(f'Expected IEEE float32 or PCM16 WAV, got {code}/{bits}')
    return samples, channels, rate


def analyze(wav, trace, metadata=None):
    data = json.loads(Path(trace).read_text())
    samples, channels, rate = read_wav(wav)
    origins = [s for s in data['samples'] if s['origin.events']]
    shift = 0
    if metadata:
        output = json.loads(Path(metadata).read_text())
        utc = lambda s: datetime.fromisoformat(s.replace('Z', '+00:00'))
        shift = (utc(data['audioStartedUtc']) - utc(output['utc'])).total_seconds()
    gaps = []
    run = 0
    for index in range(len(samples) // channels):
        silent = all(abs(samples[index * channels + c]) < 1e-9 for c in range(channels))
        if silent:
            run += 1
        elif run:
            t = (index - run) / rate - shift
            # Ignore startup and termination silence. Require a >=1 ms continuous gap.
            if run >= rate * .001 and 1 < t < data['seconds'] - 1:
                nearest = min(origins, key=lambda s: abs(s['time'] - t)) if origins else None
                gaps.append({'seconds': t, 'duration_ms': run / rate * 1000,
                             'nearest_origin_seconds': nearest['time'] if nearest else None,
                             'origin_frame_ms': nearest['elapsed'] if nearest else None,
                             'origin_frame_uploads': nearest['terrain.uploads'] if nearest else None})
            run = 0
    return {'wav': str(wav), 'rate': rate, 'duration_seconds': len(samples) / channels / rate,
            'gaps': gaps, 'measured_origin_events': len(origins),
            'max_capture_discarded_frames': max(s['audio.discarded'] for s in data['samples']),
            'terrain_uploads': sum(s['terrain.uploads'] for s in data['samples']),
            'frame_summary': data['summary']}


if __name__ == '__main__':
    print(json.dumps(analyze(*sys.argv[1:]), indent=2))
