"""Extract local Locomotion sound objects to WAV and a standalone listening gallery.

Format reference: OpenLoco SoundObject, VehicleObject and SawyerStream (MIT).
See godot/third-party/OpenLoco-LICENSE.txt. No installers are executed.
Outputs stay under .files/locomotion by default, outside game exports.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import wave

MAX_DECODED = 64 * 1024 * 1024


def require(ok, message):
    if not ok:
        raise ValueError(message)


def rle_single(data, limit, prefix=False):
    out = bytearray()
    i = 0
    while i < len(data):
        code = data[i]
        i += 1
        if code & 128:
            require(i < len(data), 'Truncated repeated run')
            block = bytes([data[i]]) * (257 - code)
            i += 1
        else:
            end = i + code + 1
            require(end <= len(data), 'Truncated literal run')
            block = data[i:end]
            i = end
        if prefix:
            out.extend(block[:limit - len(out)])
            if len(out) == limit:
                break
        else:
            require(len(out) + len(block) <= limit, 'Decoded object exceeds size limit')
            out.extend(block)
    return bytes(out)


def decode(raw, prefix_limit=None):
    require(len(raw) >= 21, 'Truncated object header')
    encoding, size = struct.unpack_from('<BI', raw, 16)
    require(size <= len(raw) - 21, 'Truncated Sawyer chunk')
    data = raw[21:21 + size]
    limit = prefix_limit or MAX_DECODED
    if encoding == 0:
        require(prefix_limit is not None or len(data) <= limit, 'Object too large')
        return data[:limit]
    if encoding == 3:
        return bytes(((b >> shift) | (b << (8 - shift))) & 255
                     for i, b in enumerate(data[:limit]) for shift in [(1 + i * 2) & 7])
    require(encoding in (1, 2), f'Unsupported Sawyer encoding {encoding}')
    data = rle_single(data, limit * 2 if encoding == 2 and prefix_limit else limit, prefix_limit is not None)
    if encoding == 1:
        return data
    out = bytearray()
    i = 0
    while i < len(data):
        code = data[i]
        i += 1
        if code == 255:
            require(i < len(data), 'Truncated repeat literal')
            block = data[i:i + 1]
            i += 1
        else:
            distance, count = 32 - (code >> 3), (code & 7) + 1
            require(distance <= len(out) and count <= distance, 'Invalid repeat reference')
            start = len(out) - distance
            block = out[start:start + count]
        require(prefix_limit is not None or len(out) + len(block) <= limit, 'Object too large')
        out.extend(block[:limit - len(out)])
        if len(out) == limit and prefix_limit:
            break
    return bytes(out)


def object_header(data, offset=0):
    require(offset + 16 <= len(data), 'Truncated dependency header')
    header = data[offset:offset + 16]
    return {'id': header[4:12].decode('ascii', errors='replace').strip(),
            'type': struct.unpack_from('<I', header)[0] & 63, 'header': header.hex()}


def string_table(data, offset):
    strings = {}
    while offset < len(data) and data[offset] != 255:
        language = data[offset]
        end = data.find(b'\0', offset + 1)
        require(end >= 0, 'Unterminated object string')
        text = data[offset + 1:end].decode('cp1252', errors='replace')
        strings[language] = ''.join(c for c in text if c.isprintable()).strip()
        offset = end + 1
    require(offset < len(data), 'Missing string-table terminator')
    return strings.get(0) or strings.get(1) or next(iter(strings.values()), ''), offset + 1


def sound(data):
    require(len(data) >= 12, 'Truncated sound object')
    name, offset = string_table(data, 12)
    require(offset + 8 <= len(data), 'Truncated sound prefix')
    count, declared_size = struct.unpack_from('<II', data, offset)
    offset += 8 + count * 16
    require(offset + 30 <= len(data), 'Truncated PCM structure')
    size = struct.unpack_from('<I', data, offset + 8)[0]
    tag, channels, rate, byte_rate, align, bits, extra = struct.unpack_from('<HHIIHHH', data, offset + 12)
    require(tag == 1 and channels in (1, 2) and bits in (8, 16, 24, 32), 'Unsupported PCM format')
    # OpenLoco ignores cbSize for PCM; old converters often left it uninitialized.
    require(0 < rate <= 384000 and align == channels * bits // 8 and byte_rate == rate * align,
            'Inconsistent PCM format')
    require(size > 0 and size % align == 0 and offset + 30 + size <= len(data), 'Invalid PCM data length')
    pcm = data[offset + 30:offset + 30 + size]
    wav = io.BytesIO()
    with wave.open(wav, 'wb') as output:
        output.setnchannels(channels)
        output.setsampwidth(bits // 8)
        output.setframerate(rate)
        output.writeframes(pcm)
    return {'name': name, 'loop': bool(data[6]), 'volume_raw': struct.unpack_from('<I', data, 8)[0],
            'channels': channels, 'sample_rate': rate, 'bits': bits, 'pcm_bytes': size,
            'duration_seconds': size / byte_rate, 'declared_size': declared_size,
            'wave_format_extra_bytes_raw': extra}, wav.getvalue()


def vehicle(data):
    require(len(data) >= 0x15E, 'Truncated vehicle')
    name, offset = string_table(data, 0x15E)
    flags = struct.unpack_from('<H', data, 0xE0)[0]
    if not flags & (1 << 9) and data[2] in (0, 1):
        offset += 16
    offset += data[6] * 16
    for _ in range(2):
        require(offset < len(data), 'Truncated cargo list')
        capacity = data[offset]
        offset += 1
        if capacity:
            while struct.unpack_from('<H', data, offset)[0] != 0xFFFF:
                offset += 3
            offset += 2
    for animation in range(2):
        if data[0x10D + animation * 3 + 2] != 0:
            offset += 16
    offset += data[0x0F] * 16
    if flags & (1 << 6):
        offset += 16
    kind = data[0x119]
    require(kind <= 3, 'Invalid driving sound type')
    references = []
    roles = (['driving'] if kind else []) + [f'start_{i + 1}' for i in range(min(3, data[0x15A] & 127))]
    for role in roles:
        reference = object_header(data, offset)
        require(reference['type'] == 1, f'Expected sound dependency for {role}, got type {reference["type"]}')
        references.append({**reference, 'role': role})
        offset += 16
    return {'name': name, 'driving_sound_type': ['none', 'friction', 'simple_motor', 'gearbox_motor'][kind],
            'sound_parameters_raw': data[0x11A:0x135].hex(), 'references': references}


def gallery(records, summary):
    embedded = json.dumps(records, ensure_ascii=True).replace('<', '\\u003c')
    return '''<!doctype html><meta charset="utf-8"><title>CLW sound testing gallery</title>
<style>body{font:16px system-ui;background:#182b25;color:#e5eddf;max-width:1100px;margin:35px auto;padding:0 20px}
input{font:inherit;padding:12px;width:95%;background:#edf0e5;color:#182b25;border:0;border-radius:5px}
article{background:#294238;padding:18px;margin:15px 0;border-radius:7px}h2{margin:0 0 8px;font-size:19px}
audio{width:min(100%,650px);display:block;margin:12px 0}small{color:#b9cbb4}summary{cursor:pointer}li{margin:4px 0}</style>
<h1>CLW train sounds</h1><p>Local testing samples. Engine, horn and other roles below come from vehicle dependencies.</p>
<p>''' + summary + '''</p><input id="search" placeholder="Filter by sound ID, name, vehicle or source path">
<div id="list"></div><script>
const sounds=''' + embedded + ''';
const list=document.querySelector('#list');
function render(){list.replaceChildren(); const term=document.querySelector('#search').value.toLowerCase();
for(const s of sounds){if(!JSON.stringify(s).toLowerCase().includes(term))continue;
const card=document.createElement('article');
const heading=document.createElement('h2');heading.textContent=s.id+' — '+s.name;card.append(heading);
const info=document.createElement('small');info.textContent=s.duration_seconds.toFixed(2)+' s · '+s.sample_rate+' Hz · '+s.bits+' bit · '+s.channels+' channel(s) · default loop: '+s.loop+' · raw volume: '+s.volume_raw;card.append(info);
const audio=document.createElement('audio');audio.controls=true;audio.volume=0.5;audio.preload='none';audio.src=s.wav;audio.loop=s.loop;
audio.addEventListener('play',()=>document.querySelectorAll('audio').forEach(a=>{if(a!==audio)a.pause()}));card.append(audio);
const label=document.createElement('label');const toggle=document.createElement('input');toggle.type='checkbox';toggle.style.width='auto';toggle.checked=audio.loop;toggle.onchange=()=>audio.loop=toggle.checked;label.append(toggle,' Loop playback');card.append(label);
const details=document.createElement('details');const summary=document.createElement('summary');summary.textContent=s.vehicles.length+' vehicle references; source files';details.append(summary);
const ul=document.createElement('ul');for(const v of s.vehicles){const li=document.createElement('li');li.textContent=v.name+' ('+v.id+') — '+v.role+' ['+v.match+']';ul.append(li)}
for(const path of s.sources){const li=document.createElement('li');li.textContent=path;ul.append(li)}details.append(ul);card.append(details);list.append(card)}}
document.querySelector('#search').addEventListener('input',render);render();</script>'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, default=Path('.files/locomotion/Chicago_Locomotive_Works'))
    parser.add_argument('--output', type=Path, default=Path('.files/locomotion/extracted-audio'))
    args = parser.parse_args()
    require(args.input.is_dir(), 'Input pack folder does not exist')
    require(args.input.resolve() != args.output.resolve(), 'Output must differ from source folder')
    records, vehicles, errors = {}, [], []
    wav_files = {}
    for path in sorted(args.input.rglob('*')):
        if path.suffix.lower() != '.dat':
            continue
        try:
            raw = path.read_bytes()
            header = object_header(raw)
            source = path.relative_to(args.input).as_posix()
            if header['type'] == 1:
                digest = hashlib.sha256(raw).hexdigest()
                if digest in records:
                    records[digest]['sources'].append(source)
                    continue
                metadata, wav = sound(decode(raw))
                audio_hash = hashlib.sha256(wav).hexdigest()
                filename = wav_files.get(audio_hash, f'wav/{header["id"].lower()}-{audio_hash[:12]}.wav')
                if audio_hash not in wav_files:
                    target = args.output / filename
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(wav)
                    wav_files[audio_hash] = filename
                records[digest] = {**header, **metadata, 'sha256': digest, 'wav': filename,
                                   'sources': [source], 'vehicles': []}
            elif header['type'] == 23:
                vehicles.append({**header, **vehicle(decode(raw, 65536)), 'source': source})
        except (ValueError, struct.error, IndexError) as error:
            errors.append({'source': str(path), 'error': str(error)})
    for item in vehicles:
        for reference in item['references']:
            exact = [s for s in records.values() if s['header'] == reference['header']]
            matches = exact or [s for s in records.values() if s['id'] == reference['id']]
            for match in matches:
                match['vehicles'].append({'id': item['id'], 'name': item['name'], 'role': reference['role'],
                                          'match': 'exact header' if exact else 'ID only; version unverified'})
    sounds = sorted(records.values(), key=lambda s: (s['id'].lower(), s['sha256']))
    for item in sounds:
        item['vehicles'] = list({(v['id'], v['name'], v['role'], v['match']): v for v in item['vehicles']}.values())
    manifest = {'source': str(args.input), 'sound_objects': len(sounds), 'wav_files': len(wav_files),
                'vehicle_objects': len(vehicles), 'errors': errors, 'sounds': sounds, 'vehicles': vehicles}
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / 'manifest.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=True), encoding='utf-8')
    summary = f'{len(sounds)} object versions, {len(wav_files)} distinct WAV files, {len(vehicles)} vehicle objects, {len(errors)} decoding errors.'
    (args.output / 'index.html').write_text(gallery(sounds, summary), encoding='utf-8')
    # Reopen every output with an independent WAV reader before reporting success.
    for filename in wav_files.values():
        with wave.open(str(args.output / filename), 'rb') as wav:
            require(len(wav.readframes(wav.getnframes())) == wav.getnframes() * wav.getnchannels() * wav.getsampwidth(), 'WAV validation failed')
    print(summary)
    print(f'Gallery: {args.output / "index.html"}')
    if errors:
        print(json.dumps(errors[:5], indent=2))
        raise SystemExit(1)


if __name__ == '__main__':
    main()
