# Tasks: xisf-codecs-and-image-read

## 1. Dependencies

- [x] 1.1 Add `K4os.Compression.LZ4` and `ZstdSharp.Port` PackageReferences to `Astronomy.XISF.csproj` (confirm both resolve as pure-managed; no native payloads appear in output)

## 2. Codec layer (`Astronomy.XISF.Compression`)

- [x] 2.1 Grow `BlockCodec` with `Lz4`, `Lz4Sh`, `Lz4Hc`, `Lz4HcSh`, `Zstd`, `ZstdSh`; keep `Other` (unknown-on-read); update `BlockCompressionInfo.Parse` token mapping (`lz4`, `lz4+sh`, `lz4hc`, `lz4hc+sh`, `zstd`, `zstd+sh`) and `ToCompressionAttribute` emission for the new codecs
- [x] 2.2 Reject sub-block / malformed `compression` attribute forms in `Parse` with an informative error (design — Non-Goals); malformed = wrong part count for the codec family, non-numeric sizes
- [x] 2.3 `Compress(raw, itemSize, codec)`: base-family parameter (Zlib/Lz4/Lz4Hc/Zstd) with zlib default preserving the existing call shape; `+sh` auto when `itemSize > 1`; levels per design (zlib SmallestSize, LZ4 fast, LZ4HC HC-level, zstd 1)
- [x] 2.4 `Decompress` dispatch for all codecs: LZ4 raw-block decode into the declared-size buffer (exact-fill check), zstd decompress, existing zlib path; `BlockCodec.Other` and decoded-size disagreement throw naming the token / both sizes
- [x] 2.5 Checksum dispatch by declared token: verify + compute for `sha-1`, `sha-256`, `sha-512`, `sha3-256`, `sha3-512` over stored bytes; unknown algorithm token throws naming it

## 3. Read slice (`Astronomy.XISF`)

- [x] 3.1 Extract signature + XML-length + `XDocument` load from `XisfHeaderReader` into a shared internal loader; header reader behavior byte-identical (its tests untouched and green)
- [x] 3.2 Parse the `<Image>` element's third geometry field (channels), `sampleFormat` (+ bytes-per-sample map), and `location="attachment:offset:size"` with fail-fast on absent/malformed values
- [x] 3.3 `XisfImageReader.ReadImageAsync(path, ct)` → `XisfImageData` (buffer, width/height/channels, sample format + bytes-per-sample, `BlockCompressionInfo`): bounds-check attachment vs file length → read stored bytes → verify declared checksum → decompress → declared-size check; `InvalidDataException` messages name file + attribute + expectation throughout

## 4. Tests (`Astronomy.XISF.Tests`)

- [x] 4.1 Round-trip matrix: every codec × {shuffled, unshuffled} reproduces original bytes; emitted `compression` attribute re-parses to the same codec/sizes
- [x] 4.2 Checksum coverage: verify success per algorithm; mismatch throws; unknown algorithm token throws
- [x] 4.3 Fail-fast: unknown codec token on decompress, decoded-size disagreement, malformed/sub-block attribute forms
- [x] 4.4 NINA-interop fixtures: blocks + attribute strings produced with NINA's exact encode calls (same packages/levels per design) decode correctly for lz4+sh, zstd+sh, and a sha-256 checksum case
- [x] 4.5 Image read: uncompressed and compressed monolithic fixtures round-trip to known pixel bytes with correct geometry/sample metadata; truncated attachment, malformed location, and corrupted-checksum fixtures each fail with the named error
- [x] 4.6 Header-only regression: header read of a fixture whose block uses an *unsupported* codec token still succeeds (pixel-free path pinned)

## 5. Docs + verify (same commit as code)

- [x] 5.1 ROADMAP: Tier 3 entry moves to shipped-slice status (codec coverage + read slice; Tier 2/4 remain open); `Astronomy.XISF.csproj` tier comment updated to match
- [x] 5.2 Full build + test run green (`dotnet build` / `dotnet test`, pure-managed graph)
