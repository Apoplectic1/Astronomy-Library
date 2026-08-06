# xisf-image-read — delta

## Purpose

Reading the primary image's pixel data out of a monolithic XISF file: locate the image's attached data block, decompress it through the block-codec layer, verify its declared checksum, and hand the caller a pixel buffer with the geometry and sample-format metadata needed to interpret it.

## ADDED Requirements

### Requirement: Read the primary image's pixel data
Given a monolithic XISF file, the library SHALL locate the primary image's attached data block from the image element's location metadata, decode it (decompressing when a compression attribute is declared), and return the raw pixel buffer together with the image's geometry (width, height, channel count) and sample format, sufficient for the caller to interpret every sample.

#### Scenario: Uncompressed attachment
- **WHEN** an XISF file's primary image block is stored uncompressed
- **THEN** the returned buffer contains exactly the attachment's bytes, with matching geometry and sample-format metadata

#### Scenario: Compressed attachment
- **WHEN** the primary image block is compressed with any supported codec (with or without byte-shuffling)
- **THEN** the returned buffer equals the decompressed pixel data, byte-for-byte

#### Scenario: NINA-produced compressed file
- **WHEN** the file was written by NINA with compression enabled
- **THEN** the image reads successfully and the buffer length equals width × height × channels × bytes-per-sample

### Requirement: Integrity verification on read
When the image's data block declares a checksum, the library SHALL verify it before returning pixel data. A mismatch SHALL fail the read; pixel data SHALL never be returned from a block that failed verification.

#### Scenario: Corrupted block detected
- **WHEN** the attachment's bytes do not match the declared checksum
- **THEN** the read fails with an error naming the file and the checksum expectation, and no buffer is returned

### Requirement: Fail fast on malformed image structure
When the file violates the XISF structural contract — missing or malformed location metadata, an attachment range that exceeds the file's length, or geometry/sample-format metadata that cannot be resolved — the read SHALL fail with an informative error naming the file and the violated expectation. It SHALL NOT return a partial or zero-filled buffer.

#### Scenario: Truncated file
- **WHEN** the declared attachment offset + size extends past the end of the file
- **THEN** the read fails with an error naming the file and the out-of-range attachment

#### Scenario: Malformed location metadata
- **WHEN** the image element's location metadata is absent or not a well-formed attachment declaration
- **THEN** the read fails with an error naming the file and the malformed attribute

### Requirement: Header reads stay pixel-free
Reading pixel data SHALL be a separate, explicit operation. Existing header-only reads SHALL continue to complete without touching the image's data block, preserving their metadata-only I/O profile.

#### Scenario: Header-only read of a compressed file
- **WHEN** a caller performs a header-only read of a file whose image block is compressed with a codec, supported or not
- **THEN** the header read succeeds without decoding, verifying, or reading the image's data block
