# Receiver backup and conversion method

## What was observed

On November 3, 2026, a standard G7 Pro receiver running `JS_3103_DG` 1.46 was read twice in full. Each SPI NOR dump was 524,288 bytes and matched byte for byte. Its header and application parsed successfully. The official Wuchang dongle 5.12 updater detected the standard receiver but refused the differing product identifier. A locally prepared updater changed `PidMatch/enable` from true to false while preserving both UFW payloads byte for byte. Installation completed with green `Upgrade SUCCESS`, product `JS_3103_U_DG`, version 5.12, and CRC `4938-FD1F1A2B`.

The existing standard controller reconnected wirelessly after a receiver cold boot without a controller firmware update. The owner then confirmed wireless dashboard/controller operation on Xbox Series S with no controller USB cable. This is evidence for one physical hardware combination. Xbox authentication internals, other receiver revisions, audio, and long-term behavior were not established.

Independent post-flash raw readback was attempted using the vendor's resident loader. It returned a different reply format from the public backup loader, so the readback stopped without producing a post-flash dump. Do not interpret vendor CRC success as independent verification that every reserved byte was preserved. No restoration experiment has been performed.

## Receiver selection

The app queries `Win32_DiskDrive` for the exact BR23 UBOOT model and checks the PnP parent chain for USB `VID_4C4A&PID_2342`. It accepts one matching target only. It never scans internal disks with vendor commands. An explicit matching disk handle is opened; standard SCSI INQUIRY must return vendor `BR23`, product `UBOOT1.00` before vendor commands are allowed.

The saved disk PnP ID and parent USB path must match after the app observes a disconnect/reconnect. These paths can identify a connection rather than a unique receiver, so the operator must connect only the intended receiver and keep the same port. This is not hardware attestation.

## Temporary RAM reader and flash reads

The pinned `br23loader.bin` is fetched from [kagaimiq/jl-uboot-tool at adb3f18889e88ac512ce0a3c4d8cc3d3cb30696a](https://github.com/kagaimiq/jl-uboot-tool/tree/adb3f18889e88ac512ce0a3c4d8cc3d3cb30696a). The 11,264-byte file is verified by SHA-256 before use. BR23 ROM accepts the existing MengLi-scrambled bytes directly.

All vendor CDBs are 16 bytes, padded with `FF`. Integers below are big-endian unless stated otherwise. Control acknowledgments must echo the two-byte opcode.

| Opcode | Operation | Bounds |
|---|---|---|
| `FB06` | RAM write: address u32, length u16, zero, CRC16 little-endian | Pinned 512-byte loader blocks only in `0x12000..0x14BFF` |
| `FB08` | Jump to RAM address u32 with argument u16 | Address `0x12000`, argument `1` only |
| `FC0A` | Online device / flash identification | SPI NOR and JEDEC `856013` only |
| `FD05` | Flash read: address u32, length u16 | Exactly bounded chunks up to 256 bytes within `0x00000..0x7FFFF` |

The C# transport has no flash erase, programming, key-write, or arbitrary memory execution API. Its allowlist rejects the vendor command opcodes for those operations. The public loader blob itself is a general loader, not claimed to be stripped of write functionality; the app exposes and sends only the listed operations. Actual firmware writing is delegated to the separately prepared vendor program after a cold replug.

The app saves and flushes both complete reads, compares them byte for byte, validates the firmware header, records SHA-256, and reopens the saved files for verification. It includes the final reserved range `0x7FF00..0x7FFFF`. It does not read OTP/eFuses or other chips. A saved image does not prove that recovery will work after a failure.

## Firmware identity

The JieLi ENC scrambling routine XORs each header byte with the low byte of a 16-bit key, then shifts the key left and conditionally XORs `0x1021`; initial key `FFFF`. The 32-byte flash header must pass CRC16 before its plaintext version/product fields are used. This release requires a header at `0x1000`, flash limit `0x7FF00`, filesystem version 2, block alignment 1, product `JS_3103_DG`, version `1.46`.

## Updater preparation

The original EXE is pinned by full-file SHA-256 before parsing. No future package is accepted automatically. Its compressed container has five entries, 528 bytes each, after a 32-byte header. All file ranges, duplicates, overlap, CRC16 values, container length, and zlib Adler32 are checked. The two embedded firmware SHA-256 values are pinned independently:

```text
JS_3103_U_DG_V512_Key.ufw
8d455017df79ea81b5373013602427ca75faf709c7f766bab1ddc761eb7752b7

JS_3103_U_DG_V512_No_Key.ufw
26acd29c8c0190889e847ae5ac7d01409ab6c77874d2072d1c388cb7b2dd6d43
```

Only `PidMatch/enable` is changed, preserving configuration length. `EraseAll/checked`, `RewriteAuthCode/checked`, and `AutoUpgrade/checked` must all be false. The configuration entry CRC, file-table CRC, and header CRC are regenerated. Firmware, image, and tail-file bytes are unchanged.

The outer 32-byte integrity records cover the compressed Qt-style zlib payload and executable body. A record contains its header CRC16, type, data CRC16, reflected CRC32 initialized to `26536734` with no final XOR, data length, PRNG seed, padding count, and other unchanged metadata. Records are XOR-obfuscated with the low output byte of the MSVC generator (`state = state * 214013 + 2531011`, output `state >> 16`), seeded from a clear 16-bit value at offset 16. Both original records are fully validated before rebuilding. CRC validation is retained, not suppressed. The prepared copy's firmware bytes are exactly those in the verified official package.

## Separation and recovery limits

This software does not alter the separate pairing application or publish vendor binaries. Backup logs, prepared vendor executables, and dumps are local only. There is no automatic controller update, forced erase, raw flash writer, or advertised one-click recovery. A failed flash requires further investigation; do not assume a backup guarantees unbricking.

References: [GameSir firmware links](https://gamesir.com/support/manuals/gamesir-g7prozzz), [jl-uboot-tool](https://github.com/kagaimiq/jl-uboot-tool), [jl-misctools](https://github.com/kagaimiq/jl-misctools).
