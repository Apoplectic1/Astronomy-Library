# xisf-block-compression — delta

## Purpose

Symmetric XISF block-codec contract: compress and decompress raw data blocks with every codec the XISF specification defines, parse and emit the spec's `compression`/`checksum` attribute metadata, and verify block integrity — so any consumer (file readers, XFM's writer, future image-write) shares one conformant codec layer.

## ADDED Requirements

### Requirement: Symmetric codec round-trip
The library SHALL compress and decompress data blocks with every XISF block codec — `zlib`, `lz4`, `lz4hc`, `zstd` — each with and without byte-shuffling, plus uncompressed passthrough. For every supported codec×shuffle combination, decompressing a compressed block SHALL reproduce the original bytes exactly.

#### Scenario: Round-trip preserves bytes for every codec
- **WHEN** a data block is compressed with any supported codec, with or without byte-shuffling, and the result is decompressed
- **THEN** the output is byte-for-byte identical to the original block

#### Scenario: Shuffled variant applies item-size correctly
- **WHEN** a block of multi-byte samples is compressed with a `+sh` codec variant using the sample's item size
- **THEN** decompression with the same declared item size reproduces the original block exactly

### Requirement: Compression attribute conformance
The library SHALL parse XISF `compression` attribute values (codec identifier, uncompressed size, and shuffle item size when present) for all supported codecs, and SHALL emit spec-conformant `compression` attribute values for every block it compresses, such that conformant third-party XISF software can decode the block.

#### Scenario: Parsing a producer's attribute
- **WHEN** a `compression` attribute written by conformant XISF software (e.g. NINA or PixInsight) declares a supported codec
- **THEN** the library extracts codec, uncompressed size, and item size (when present) and can decompress the associated block

#### Scenario: Emitted attribute is readable by the parser
- **WHEN** the library compresses a block and emits its `compression` attribute
- **THEN** parsing that attribute back yields the same codec, uncompressed size, and item size used for compression

### Requirement: Checksum algorithm coverage
The library SHALL verify block `checksum` attributes for every hash algorithm the XISF specification lists (SHA-1, SHA-256, SHA-512, SHA3-256, SHA3-512), using the algorithm the attribute declares, and SHALL be able to compute a spec-conformant checksum for any block it produces.

#### Scenario: Verification honors the declared algorithm
- **WHEN** a block carries a `checksum` attribute in any spec-listed algorithm and the block data matches
- **THEN** verification succeeds

#### Scenario: Checksum mismatch is a hard error
- **WHEN** a block's computed checksum differs from the declared value
- **THEN** the operation fails with an error naming the expected and actual values; the block is never returned as valid data

### Requirement: Fail fast on malformed or unsupported codec metadata
When a `compression` or `checksum` attribute is malformed, declares an unknown codec or hash algorithm, or contradicts the block (e.g. declared uncompressed size disagrees with the decoded output), the library SHALL fail with an informative error naming the attribute and the expectation. It SHALL NOT skip verification, guess a codec, or return partially decoded data.

#### Scenario: Unknown codec identifier
- **WHEN** a `compression` attribute declares a codec the XISF specification does not define
- **THEN** the operation fails with an error naming the unrecognized identifier

#### Scenario: Decoded size disagrees with declaration
- **WHEN** decompression yields a byte count different from the attribute's declared uncompressed size
- **THEN** the operation fails with an error naming both sizes
