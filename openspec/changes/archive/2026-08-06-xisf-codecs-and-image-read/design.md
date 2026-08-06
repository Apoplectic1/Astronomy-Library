# Design: xisf-codecs-and-image-read

## Context

See `proposal.md` — Why. Current state: `XisfBlockCompression` does zlib±shuffle+SHA-1 only (`Compress` hard-codes zlib; `Decompress` handles `Zlib`/`ZlibSh`); `BlockCompressionInfo.Parse` maps unknown tokens to `BlockCodec.Other`; `XisfHeaderReader.ReadAsync` reads signature + XML and stops (never touches attachments). NINA's writer (`NINA.Image\FileFormat\XISF\XISFData.cs`, reference cheat-sheet only) confirms the on-disk vocabulary: tokens `lz4`/`lz4+sh`/`lz4hc`/`lz4hc+sh`/`zstd`/`zstd+sh`, raw LZ4 **block** encoding via `K4os.Compression.LZ4` (`LZ4Codec.Encode`), `ZstdSharp` compressor, checksums `sha-1`/`sha-256`/`sha-512`/`sha3-256`/`sha3-512` via BCL hash classes, shuffle applied before compression with item size = bytes-per-sample.

## Goals / Non-Goals

**Goals** (design-level; requirements live in the delta specs):
- One codec dispatch point; consumers never switch on codec themselves.
- The read slice reuses the existing header-parse machinery rather than duplicating signature/XML handling.
- **As efficient as reasonable** (the library's bar, stated 2026-08-06): block-at-a-time `byte[]` APIs sized-and-allocated once per stage, no gratuitous intermediate copies — but no speculative Span/pooling machinery; blocks are one-per-file and correctness/interop dominate.

**Non-Goals:**
- No header write-back, no file composition (Tiers 2/4 — future changes).
- No pixel-format conversion, normalization, or demosaic: the read slice returns the raw buffer + metadata; interpretation is the caller's.
- No sub-block `compression` forms: neither NINA nor XFM emits them; they are **explicitly rejected** with an informative error (revisit only if a real producer file surfaces).
- No streaming/chunked decode: blocks are materialized whole (largest real frame ≈ 120 MB uncompressed — fine in memory).

## Decisions

1. **Grow `BlockCodec`, keep `Other` as the fail-fast trigger.** Enum gains `Lz4`, `Lz4Sh`, `Lz4Hc`, `Lz4HcSh`, `Zstd`, `ZstdSh`. `Parse` keeps returning `Other` for unknown tokens — XFM's read-side "is this already compressed?" detector must keep working on files it merely inspects — but `Decompress` on `Other` now throws (naming the token) instead of being unreachable. Alternative rejected: throwing in `Parse` would abort consumers that only need `IsCompressed`.
2. **`Compress` gains a codec parameter; shuffle stays itemSize-driven.** `Compress(raw, itemSize, codec)` where `codec` is the base family (Zlib/Lz4/Lz4Hc/Zstd); the `+sh` variant is applied automatically when `itemSize > 1`, mirroring both the current API's behavior and NINA's writer. Existing single caller shape (`Compress(raw, itemSize)`) keeps zlib as the default so XFM's swap is a drop-in. Compression levels mirror the producers we must interoperate with: zlib `SmallestSize` (current), LZ4 fast, LZ4HC's HC level, zstd level 1 (NINA's choices).
3. **LZ4 is the raw block format, not LZ4 frame.** `K4os.Compression.LZ4`'s `LZ4Codec.Encode/Decode` — decode requires the exact uncompressed size, which the `compression` attribute supplies. A decode that fills a different byte count than declared is a hard error (this doubles as the declared-size-disagreement check in the spec).
4. **Checksum dispatch by declared token, BCL algorithms only.** `sha-1`→`SHA1`, `sha-256`→`SHA256`, `sha-512`→`SHA512`, `sha3-256`→`SHA3_256`, `sha3-512`→`SHA3_512`. Checksums cover the **stored** (compressed) bytes, matching NINA/PixInsight. SHA3 classes require OS support (present on this Win11 build; older platforms throw `PlatformNotSupportedException` — acceptable fail-fast, not guarded).
5. **Shared XML loader; header path byte-identical.** Extract `XisfHeaderReader`'s signature + XML-length + `XDocument` load into an internal helper used by both the header reader and the new read slice. `XisfHeaderReader`'s public behavior is unchanged (the delta spec pins header reads pixel-free).
6. **Read slice = new `XisfImageReader.ReadImageAsync(path, ct)` returning `XisfImageData`.** Carries the raw pixel buffer, width/height/channels (geometry's third field now parsed, not skipped), the declared `sampleFormat` + bytes-per-sample, and the block's `BlockCompressionInfo`. Pipeline: parse `<Image>` `location="attachment:offset:size"` → bounds-check against file length → read stored bytes → verify declared checksum → dispatch decompress → length-check against declared uncompressed size. Every step failure is an `InvalidDataException` naming file + attribute + expectation (matches the header reader's existing error style).
7. **New NuGet deps on `Astronomy.XISF` only**: `K4os.Compression.LZ4`, `ZstdSharp.Port` — both pure managed, no native payloads, preserving the library's no-native rule. No other project's dependency set changes.

## Risks / Trade-offs

- [ZstdSharp is a managed port — slower than native zstd] → Irrelevant at our scale (one block per file, read-side); interop correctness is what matters. Revisit only if a bulk consumer appears.
- [Interop tests can't literally run NINA] → Fixtures are encoded with **NINA's exact calls** (same package, same levels, same attribute strings), making decode tests faithful to NINA-written bytes; a genuine field file can be added later (see Open Questions).
- [`Compress` default stays zlib] → XFM's swap is drop-in, but callers wanting lz4/zstd must opt in explicitly — deliberate, since changing XFM's output codec is out of scope.

## Migration Plan

None needed: additive library change, no consumers change in this release (proposal — Impact). Ships in AL ahead of the two queued consumer changes (ASTAP pipeline read-side; XFM's `adopt-al-xisf-compression` encode-side swap).

## Open Questions

- Add a genuine NINA-written compressed field file as a test fixture? Deferrable: the NINA-call-identical fixtures cover the contract; a real file (user-supplied, small crop) would harden it further and can land any time.
