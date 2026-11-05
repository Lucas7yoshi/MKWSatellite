# NSP / NCA notes (getting a dump)

Not part of the app (yet); recorded from extracting a 1.8.0 dump.

- **NSP** = PFS0: `"PFS0"`, u32 file count, u32 string table size, u32 reserved, then entries `{u64 offset, u64 size, u32 nameOffset, u32 reserved}`, string table, data. The file list is never encrypted, so contents can be inspected without keys.
- The 1.8 dump was a single full package: one Program NCA (~22 GB), one Control NCA, one `.cnmt.nca`, no ticket. Title ID `0400C3F00006E000`.
- Its NCAs were **stored decrypted**: plaintext `NCA3` magic at 0x200, zeroed signatures, section encryption type 1 (none). No keys or hactoolnet were needed.
- Program NCA sections: 0 = exefs (PFS0 behind HierarchicalSha256; last hash region = the PFS0), 1 = romfs (behind IVFC/HierarchicalIntegrity; last level = the RomFS image).
- RomFS (64-bit): header of u64 fields (header size, dir hash/meta, file hash/meta, data offset); directory entries `{parent, sibling, childDir, childFile, hashNext, nameLen, name}`, file entries `{parent, sibling, u64 dataOff, u64 size, hashNext, nameLen, name}`, 0xFFFFFFFF = none.
- Extract in data-offset order: much faster on hard drives.
- A normal (encrypted) dump needs keys from the user's own console (`prod.keys`, `title.keys`); we never ship or fetch keys.
- Idea for later: read decrypted NSPs directly as a romfs source instead of extracting.
