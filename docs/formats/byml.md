# BYML

Implemented in `src/MKWSatellite.Formats/Byml/BymlReader.cs`. Banc scenes use little-endian (`YB`) version 7.

## Header (16 bytes)
| Off | Type | Field |
|---|---|---|
| 0x0 | char[2] | `YB` (LE) or `BY` (BE) |
| 0x2 | u16 | version |
| 0x4 | u32 | key (hash-key) string table offset, 0 = none |
| 0x8 | u32 | value string table offset, 0 = none |
| 0xC | u32 | root node offset, 0 = empty |

## Node types
| Type | Meaning | Value slot (4 bytes) |
|---|---|---|
| 0xA0 | string | index into value string table |
| 0xA1 | binary | offset → u32 size + data |
| 0xC0 | array | offset → node |
| 0xC1 | dictionary | offset → node |
| 0xC2 | string table | (container only) |
| 0xD0 | bool | inline |
| 0xD1 | s32 | inline |
| 0xD2 | f32 | inline |
| 0xD3 | u32 | inline |
| 0xD4 / 0xD5 / 0xD6 | s64 / u64 / f64 | offset → 8 bytes |
| 0xFF | null | — |

Containers start with `u8 type, u24 count`.
- Array: `count` type bytes, padded to 4, then `count` value slots.
- Dictionary: `count` entries of `u24 keyIndex, u8 type, u32 value`. Keys are stored sorted.
- String table: `count + 1` u32 offsets relative to the table start (last one = end), then NUL-terminated UTF-8 strings.

## Not yet supported
Hash-map node types (`0x20`/`0x21`) and other newer types; the reader throws `NotSupportedException` naming the type and offset. `mkwsat scan` will show whether any game file needs them.
