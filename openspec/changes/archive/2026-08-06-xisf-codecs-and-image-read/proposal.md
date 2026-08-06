# Proposal: xisf-codecs-and-image-read

## Why

`Astronomy.XISF` is header-only today; the seeded `Astronomy.XISF.Compression` codec (zlib+shuffle+SHA-1, shipped 2026-06) still has no caller outside its own tests, and it covers only the codecs this library *produces* — not the codecs real libraries on disk actually carry (NINA writes LZ4 by default; zstd is spec-legal). ROADMAP Tier 3 ("full image read") gates on a real consumer: that consumer has now arrived — an ASTAP plate-solve pipeline explored on the TSM side (2026-08-06) needs decompressed pixel data for any image in the user's library, regardless of who compressed it. This change is deliberately **standalone**: full XISF-conformant block compression + the minimal image-read entry point, independent of any ASTAP work, so any future consumer (ASTAP pipeline, XFM migration, ISM) gets the same contract. A second consumer is already known: XFM compresses image blocks today via a vendored byte-for-byte duplicate of `Astronomy.XISF.Compression` (`XisfFileManager\Files\Compression\`) — the symmetric encode side of this change is what lets XFM retire that duplicate and consume AL's codec layer directly (while keeping its own binary writer; Tier 2/4 stay out of scope).

## What Changes

- **Symmetric codec coverage** in `Astronomy.XISF.Compression`: encode *and* decode for every XISF 1.0 block codec — `zlib`, `lz4`, `lz4hc`, `zstd`, each with and without byte-shuffling (`+sh`), plus uncompressed passthrough. Symmetric by decision (2026-08-06): the encode side is nearly free from the same packages and makes round-trip tests hermetic (no binary fixtures required).
- **XISF 1.0 conformance** of the block-level metadata surface, targeting compatibility with NINA- and PixInsight-produced files: full `compression` attribute syntax (codec, uncompressed-size, shuffle item-size; sub-block form parsed or explicitly rejected with a clear error after checking what producers emit), and `checksum` verification for every spec-listed algorithm (SHA-1, SHA-256, SHA-512, SHA3 variants as the spec requires — not SHA-1-only).
- **Minimal image-read entry point** (the Tier 3 slice): given an XISF file, locate the primary `<Image>` attachment from its `location="attachment:offset:size"` attribute, decompress via the codec layer, verify the declared checksum, and return the pixel buffer plus geometry/sample-format metadata. This is the piece that gives the codec surface a real caller.
- **Fail fast on contract violations**: unknown codec, checksum mismatch, malformed compression/location attributes, or truncated attachment → informative exception naming file + attribute + expectation. No fallback paths, no warn-and-continue.
- New NuGet dependencies: `K4os.Compression.LZ4` and `ZstdSharp.Port` — both pure-managed, preserving the library's no-native rule (zlib stays BCL `ZLibStream`). NINA's `XISFData` is the reference cheat-sheet for algorithm strategy only; no NINA code copied, no NINA assembly referenced (decoupling policy, ROADMAP Tier 3 note).

Out of scope: header write-back (Tier 2), image write/composition (Tier 4), any WCS/plate-solve awareness, any change to `XisfHeaderReader`'s header-only fast path.

## Capabilities

### New Capabilities

- `xisf-block-compression`: the symmetric block codec contract — supported codecs and shuffle variants, compression/checksum attribute parsing and emission, round-trip guarantees, checksum algorithm coverage, fail-fast error behavior.
- `xisf-image-read`: the attachment read contract — locating the primary image block, decompression via the codec layer, checksum verification, returned pixel buffer + geometry/sample-format metadata, fail-fast on malformed or truncated files.

### Modified Capabilities

_None — existing specs (`contract-assumption-pinning`, `github-distribution`, `moon-brightness-gate`) are untouched._

## Impact

- **Code**: `Astronomy.XISF\Compression\` (extend `XisfBlockCompression`, `BlockCompressionInfo`), new read-slice entry in `Astronomy.XISF`; tests in `Astronomy.XISF.Tests` (round-trips per codec×shuffle, checksum algorithms, malformed-attribute rejection, and at least one real-file read against a NINA-produced compressed XISF).
- **Dependencies**: + `K4os.Compression.LZ4`, + `ZstdSharp.Port` (both managed-only).
- **Consumers**: none change in this release. `Astronomy.Catalog`'s scanner keeps the header-only path; TSM/TP/XFM unaffected. Two follow-on consumers are queued: the future ASTAP pipeline change (read side) and an XFM-side change swapping its vendored `Files\Compression\` duplicate for AL's codec layer (encode side — codec classes only, XFM's binary writer untouched).
- **Docs**: ROADMAP Tier 3 moves from "partially seeded, no caller" to shipped-slice status; `Astronomy.XISF.csproj` tier comment updated alongside.
- **Release ordering**: lands and releases in AL ahead of any consumer work, per the cross-repo ordering rule.
