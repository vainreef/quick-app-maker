import zlib from 'node:zlib';

export function solidPng(width, height, [r, g, b, a = 255] = [31, 41, 55, 255]) {
  const rows = [];
  const row = Buffer.alloc(width * 4 + 1);
  row[0] = 0;
  for (let x = 0; x < width; x += 1) { row[1 + x * 4] = r; row[2 + x * 4] = g; row[3 + x * 4] = b; row[4 + x * 4] = a; }
  for (let y = 0; y < height; y += 1) rows.push(row);
  const raw = Buffer.concat(rows);
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4); ihdr[8] = 8; ihdr[9] = 6;
  return Buffer.concat([Buffer.from('89504e470d0a1a0a', 'hex'), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
function chunk(type, data) { const header = Buffer.from(type); const size = Buffer.alloc(4); size.writeUInt32BE(data.length); const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(Buffer.concat([header, data]))); return Buffer.concat([size, header, data, crc]); }
function crc32(buffer) { let c = 0xffffffff; for (const byte of buffer) { c ^= byte; for (let bit = 0; bit < 8; bit += 1) c = (c >>> 1) ^ (0xedb88320 & -(c & 1)); } return (c ^ 0xffffffff) >>> 0; }
