"""Import OpenLoco vehicle audio parameters into SI profiles and reference fixtures.

Algorithm source: OpenLoco VehicleHead.cpp motor/friction sound updates (MIT).
See godot/third-party/OpenLoco-LICENSE.txt. All exported speeds are metres/second.
"""
import json
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / '.files/locomotion/extracted-audio'
manifest = json.loads((DATA / 'manifest.json').read_text())
UNIT_MPS = .44704
RAW_PER_MPS = 65536 / UNIT_MPS


def profile(vehicle):
    raw = bytes.fromhex(vehicle['sound_parameters_raw'])
    u16 = lambda at: struct.unpack_from('<H', raw, at)[0]
    mode = vehicle['driving_sound_type']
    result = {'mode': mode, 'sourceVehicle': vehicle['id'], 'sourceName': vehicle['name']}
    if mode == 'friction':
        result.update(minSpeedMps=struct.unpack_from('<i', raw, 1)[0] / RAW_PER_MPS,
                      speedFrequencyPerMps=RAW_PER_MPS / 2 ** raw[5], baseFrequencyHz=u16(6),
                      speedVolumePerMps=RAW_PER_MPS / 2 ** raw[8], baseVolume=raw[9], maxVolume=raw[10])
    else:
        result.update(idleFrequencyHz=u16(1), idleVolume=raw[3], coastThresholdMps=5.36448)
        if mode == 'simple_motor':
            result.update(coastingFrequencyHz=u16(4), coastingVolume=raw[6],
                          accelerationFrequencyHz=u16(7), accelerationVolume=raw[9])
            offset = 10
        else:
            result.update(firstGearFrequencyHz=u16(4), minimumGearSpeedMps=3.12928,
                          gearSpeedsMps=[u16(i) * UNIT_MPS for i in (6, 10, 14)],
                          gearFrequencyOffsetsHz=[u16(i) for i in (8, 12, 16)],
                          coastingVolume=raw[18], accelerationVolume=raw[19])
            offset = 20
        result.update(frequencyIncreasePerTick=u16(offset), frequencyDecreasePerTick=u16(offset + 2),
                      volumeIncreasePerTick=raw[offset + 4], volumeDecreasePerTick=raw[offset + 5],
                      speedFrequencyPerMps=RAW_PER_MPS / 2 ** raw[offset + 6])
    return result


profiles = {}
sources = {}
for name, sound, vehicle_id in [('f7', 'SNDFP7FA', 'USF7A25'), ('ge', 'SNDDSH9E', 'USATP383'), ('electric', 'SNDACLEN', 'USATACEL')]:
    candidates = [v for v in manifest['vehicles'] if any(r['id'] == sound and r['role'] == 'driving' for r in v['references'])]
    selected = next(v for v in candidates if v['id'] == vehicle_id)
    profiles[name] = profile(selected)
    sources[name] = selected
(ROOT / 'godot/assets/audio/trains/engine-profiles.json').write_text(json.dumps(profiles, indent=2) + '\n')

# Independent reference retains the original fixed-point arithmetic/byte fields.
# Fixtures cover transitions, threshold boundaries, startup and gearbox pitch drops.
sources['gearbox-test'] = next(v for v in manifest['vehicles'] if v['driving_sound_type'] == 'gearbox_motor')
fixtures = []
for name, vehicle in sources.items():
    b = bytes.fromhex(vehicle['sound_parameters_raw'])
    u16 = lambda i: struct.unpack_from('<H', b, i)[0]
    frequency = volume = 0
    initialized = False
    steps = []
    schedule = [(0, 'stopped', 10), (10, 'accelerating', 70), (30, 'coasting', 25),
                (20, 'braking', 25), (2, 'coasting', 25), (0, 'stopped', 50),
                (60, 'accelerating', 70), (5.36447, 'coasting', 30), (5.36448, 'coasting', 30)]
    for speed, state, ticks in schedule:
        for _ in range(ticks):
            fixed = int(speed * RAW_PER_MPS + 1e-7)
            if vehicle['driving_sound_type'] == 'friction':
                difference = fixed - struct.unpack_from('<i', b, 1)[0]
                if difference < 0:
                    volume = 0
                else:
                    frequency = ((difference >> b[5]) + u16(6)) & 65535
                    volume = min(255, b[10], ((difference >> b[8]) + b[9]) & 255)
            else:
                target = u16(1)
                level = b[3]
                if vehicle['driving_sound_type'] == 'simple_motor':
                    offset = 10
                    if state == 'accelerating':
                        target, level = (u16(7) + (fixed >> b[16])) & 65535, b[9]
                    elif state == 'coasting' and fixed >= 12 * 65536:
                        target, level = u16(4), b[6]
                else:
                    offset = 20
                    in_gear = state == 'accelerating'
                    if state in ('coasting', 'braking'):
                        level = b[18]
                        in_gear = fixed >= 12 * 65536
                    elif state == 'accelerating':
                        level = b[19]
                    if in_gear:
                        gear_speed = max(fixed, 7 * 65536)
                        base = u16(4)
                        for index, at in enumerate((6, 10, 14)):
                            if gear_speed < u16(at) * 65536:
                                break
                            base -= u16((8, 12, 16)[index])
                        target = (base + (gear_speed >> b[26])) & 65535
                if not initialized:
                    frequency, volume, initialized = u16(1) >> 2, b[3] >> 1, True
                else:
                    if frequency > target:
                        if vehicle['driving_sound_type'] == 'gearbox_motor':
                            level = b[18]
                        frequency = max(target, frequency - u16(offset + 2))
                    elif frequency < target:
                        frequency = min(target, frequency + u16(offset))
                    volume = max(level, volume - b[offset + 5]) if volume > level else min(level, volume + b[offset + 4])
            steps.append({'speedMps': speed, 'state': state, 'frequencyHz': frequency, 'volume': volume})
    fixtures.append({'name': name, 'profile': profile(vehicle), 'steps': steps})
(ROOT / 'godot/tests/engine-audio-fixtures.json').write_text(json.dumps(fixtures, separators=(',', ':')) + '\n')
print('Exported three SI engine profiles and', sum(len(f['steps']) for f in fixtures), 'OpenLoco reference steps.')
