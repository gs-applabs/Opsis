"""Fill an unsigned Inno 7.1.1 layout that was compiled with test-only files."""
import hashlib
import lzma
import re
import struct
import sys
import zlib
from pathlib import Path

OFFSET_ID = b'rDlPtS\xcd\xe6\xd7\x7b\x0b\x2a'
LOCATION = struct.Struct('<ii4q32sQ2IB')
TABLE = struct.Struct('<12sIqqIiqqII')
LZMA1 = dict(id=lzma.FILTER_LZMA1, dict_size=1 << 23, lc=3, lp=0, pb=2)
LZMA2 = dict(id=lzma.FILTER_LZMA2, dict_size=1 << 24)

def get32(data, pos):
    return struct.unpack_from('<I', data, pos)[0]

def read_block(data, pos):
    crc, length, compressed = struct.unpack_from('<IqB', data, pos)
    assert zlib.crc32(data[pos+4:pos+13]) == crc
    pos += 13
    end = pos + length
    chunks = []
    while pos < end:
        chunk = data[pos+4:min(end,pos+4100)]
        assert chunk and zlib.crc32(chunk) == get32(data, pos)
        chunks.append(chunk)
        pos += 4 + len(chunk)
    payload = b''.join(chunks)
    if compressed:
        prop = payload[0]
        props = dict(id=lzma.FILTER_LZMA1, lc=prop%9, lp=prop//9%5, pb=prop//45, dict_size=get32(payload,1))
        payload = lzma.LZMADecompressor(format=lzma.FORMAT_RAW, filters=[props]).decompress(payload[5:], max_length=64<<20)
    return payload, pos

def write_block(data):
    packed = b'\x5d' + struct.pack('<I', LZMA1['dict_size']) + lzma.compress(data, format=lzma.FORMAT_RAW, filters=[LZMA1])
    framed = bytearray()
    for pos in range(0, len(packed), 4096):
        chunk = packed[pos:pos+4096]
        framed.extend(struct.pack('<I', zlib.crc32(chunk)))
        framed.extend(chunk)
    header = struct.pack('<qB', len(framed), 1)
    return struct.pack('<I', zlib.crc32(header)) + header + framed

def read_layout(data):
    pos = data.index(OFFSET_ID)
    table = TABLE.unpack_from(data, pos)
    assert table[1] == 2 and table[2] == len(data) and zlib.crc32(data[pos:pos+60]) == table[-1]
    offset0, offset1, offset_exe = table[6], table[7], table[3]
    assert 0 < pos < offset1 < offset0 < offset_exe < len(data)
    assert data[offset0:offset0+64].startswith(b'Inno Setup Setup Data (7.1.1)')
    encryption_at = offset0 + 68
    assert data[encryption_at] == 0 and zlib.crc32(data[encryption_at:encryption_at+49]) == get32(data, encryption_at-4)
    metadata, next_block = read_block(data, encryption_at+49)
    locations, end = read_block(data, next_block)
    assert end == offset_exe and len(locations) % LOCATION.size == 0
    return pos, table, metadata, locations

def map_files(metadata, folders, count):
    result = {}
    for root in folders:
        prefix = root.encode('utf-16-le')
        for hit in re.finditer(re.escape(prefix), metadata):
            try:
                pos = hit.start()-8
                source_len = get32(metadata, pos)
                if pos<0 or source_len != 0:
                    continue
                pos += 4
                fields = []
                for _ in range(14):
                    length = get32(metadata, pos)
                    assert length < 16384 and pos+4+length<=len(metadata) and length%2==0
                    fields.append(metadata[pos+4:pos+4+length].decode('utf-16-le'))
                    pos += 4+length
                if not fields[0].startswith(root) or len(fields[0]) <= len(root):
                    continue
                size = get32(metadata, pos)
                assert size < 16384
                index = struct.unpack_from('<i', metadata, pos+4+size+53)[0]
                if not (0 <= index < count):
                    continue
                name = fields[0]
                if index in result:
                    assert result[index].casefold() == name.casefold(), (name, result[index])
                result[index] = name
            except (AssertionError, struct.error, UnicodeDecodeError):
                continue
    assert len(result) == count, sorted(set(range(count))-result.keys())[:50]
    return result

def pe_unsigned(data):
    off = get32(data, 0x3c) + 24
    assert data[off-24:off-20] == b'PE\0\0'
    magic = struct.unpack_from('<H', data, off)[0]
    assert magic in (0x10b,0x20b)
    security = off + (96 if magic==0x10b else 112) + 4*8
    assert struct.unpack_from('<II', data, security) == (0,0), 'The layout must be an unsigned installer'
    return off+64

def repack(template, publish, crawler, output):
    data = template.read_bytes()
    checksum_offset = pe_unsigned(data)
    location_position, table, metadata, old_locations = read_layout(data)
    targets = map_files(metadata, ('{app}\\', '{tmp}\\'), len(old_locations)//LOCATION.size)
    inputs = {'{app}\\'+str(path.relative_to(publish)).replace('/','\\'):path
              for path in publish.rglob('*') if path.is_file()}
    if crawler != Path('-'):
        inputs['{tmp}\\OpsisNewsCrawler-Setup.exe'] = crawler
    source = {name.casefold(): file for name,file in inputs.items()}
    assert {name.casefold() for name in targets.values()} == source.keys(), (sorted(set(source)-{x.casefold() for x in targets.values()}), sorted({x.casefold() for x in targets.values()}-set(source)))
    content = bytearray()
    records = []
    for i,name in sorted(targets.items()):
        body = source[name.casefold()].read_bytes()
        old = LOCATION.unpack_from(old_locations, i*LOCATION.size)
        records.append((len(content), len(body), hashlib.sha256(body).digest(), old[7], old[-1] & 2))
        content.extend(body)
    packed = b'\x18'+lzma.compress(content, format=lzma.FORMAT_RAW, filters=[LZMA2])
    locations = b''.join(LOCATION.pack(0,0,0,start,size,len(packed),checksum,stamp,0,0,flags|16)
                         for start,size,checksum,stamp,flags in records)
    # All metadata except payload locations is the original compiler output.
    offset1 = table[7]
    header = bytearray(data[:offset1])
    block1 = b'zlb\x1a'+packed
    offset0 = offset1+len(block1)
    setup_data = data[table[6]:table[6]+117]+write_block(metadata)+write_block(locations)
    offset_exe = offset0+len(setup_data)
    engine = data[table[3]:]
    fields = list(table)
    fields[2],fields[3],fields[6] = offset_exe+len(engine),offset_exe,offset0
    leading = TABLE.pack(*fields)[:60]
    header[location_position:location_position+64]=leading+struct.pack('<I',zlib.crc32(leading))
    struct.pack_into('<I',header,checksum_offset,0)
    with output.open('wb') as destination:
        for block in (header,block1,setup_data,engine): destination.write(block)
    verify(output, source)
    print('Verified',len(records),'embedded files and SHA-256 digests:',output)

def verify(output, source):
    final = output.read_bytes()
    _, tab, metadata, loc = read_layout(final)
    targets = map_files(metadata, ('{app}\\','{tmp}\\'),len(loc)//LOCATION.size)
    offset1 = tab[7]
    assert final[offset1:offset1+5] == b'zlb\x1a\x18'
    full = lzma.decompress(final[offset1+5:tab[6]],format=lzma.FORMAT_RAW,filters=[LZMA2])
    for i,name in targets.items():
        record = LOCATION.unpack_from(loc,i*LOCATION.size)
        start,size,hash_value = record[3],record[4],record[6]
        assert hashlib.sha256(full[start:start+size]).digest() == hash_value
        assert hashlib.sha256(source[name.casefold()].read_bytes()).digest() == hash_value

if __name__ == '__main__':
    repack(*map(Path,sys.argv[1:]))
