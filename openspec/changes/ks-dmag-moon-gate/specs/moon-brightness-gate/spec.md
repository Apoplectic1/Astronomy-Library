# moon-brightness-gate

## Purpose

The K-S Δmag moon gate: placement primitives accept an observing minute if and only if the
Krisciunas–Schaefer-predicted sky brightness at the target exceeds the moonless baseline by no more
than a caller-chosen tolerance. Replaces the ACP/TS Lorentzian moon-avoidance model entirely.

## ADDED Requirements

### Requirement: Δmag acceptance semantics
The gate SHALL accept a sampled minute iff `Δmag ≤ ToleranceMag`, where
`Δmag = ln(1 + bMoon / (bDark + bTwilight)) / 0.92104` — the K-S-predicted brightening of the sky
at the target over the moonless (dark + twilight) baseline, in mag/arcsec². The Δ SHALL be computed
from one decomposed K-S evaluation (the three nanolambert components evaluated once), not by
subtracting two full `KsAt` calls. `SkyBrightness.KsAt` itself SHALL be unchanged (its 10-parameter
golden contract, CONSUMERS #4, is preserved).

#### Scenario: Full moon near the target rejects
- **WHEN** the moon is full, above the horizon, and near the target at any sane tolerance (≤ 3 mag)
- **THEN** the minute is rejected (Δmag is far above tolerance)

#### Scenario: New moon accepts
- **WHEN** the moon phase is new (illumination ≈ 0) or the apparent moon altitude is ≤ 0
- **THEN** `Δmag = 0` and the minute is accepted for any non-negative tolerance

#### Scenario: Tolerance is monotone
- **WHEN** the same instant is evaluated under tolerances t1 < t2
- **THEN** acceptance under t1 implies acceptance under t2

### Requirement: Bandwidth independence
`Δmag` SHALL be independent of filter bandwidth (the bandwidth scale is a common factor of all
three K-S components and cancels in the ratio). The gate's profile and the Δ function SHALL NOT
take a bandwidth parameter. Band center SHALL be taken (it drives extinction wavelength scaling
and twilight's Rayleigh scaling).

#### Scenario: Same Δmag across bandwidths
- **WHEN** two evaluations differ only in what a bandwidth parameter would have been
- **THEN** there is no such parameter to vary — the profile carries `CenterNm` only

### Requirement: Apparent-altitude moon convention
The gate SHALL refraction-correct the moon's altitude (`Refraction.SaemundssonDeg`) before the
K-S evaluation, matching the Sky-chart convention. `MoonSeparation.ObserveAt` SHALL continue to
return geometric moon altitude (CONSUMERS #3 unchanged); the correction is internal to the gate.

#### Scenario: Moonset boundary uses apparent altitude
- **WHEN** the geometric moon altitude is slightly below 0 but the apparent altitude is above 0
- **THEN** the moon still contributes to Δmag (the gate cuts off at apparent altitude ≤ 0,
  ~34′ / ~2 min later than a geometric cutoff)

### Requirement: Site parameters derive from Location
The gate SHALL derive its site inputs from the `Location` already in scope:
`v0Mag = Bortle.DefaultZenithMag(location.BortleClass)` and band extinction
`k = SkyBrightness.ScaleK(location.ExtinctionK, profile.CenterNm)`. The profile SHALL NOT carry
site fields.

#### Scenario: Site change flows without profile change
- **WHEN** the same profile is evaluated at two Locations differing in BortleClass or ExtinctionK
- **THEN** the gate decisions reflect the site difference with no profile modification

### Requirement: Twilight dilutes the moon term
The moonless baseline SHALL include the solar-twilight contribution (sun altitude sampled
per-minute), so a brighter twilight sky reduces Δmag for the same moon geometry. This is
deliberate physics and SHALL NOT be "corrected" to a dark-only baseline.

#### Scenario: Same moon, brighter twilight, smaller Δmag
- **WHEN** the same moon/target geometry is evaluated at sun −18° and at sun −13°
- **THEN** Δmag at −13° is strictly smaller than at −18°

### Requirement: Gate profile shape and short-circuits
The gate SHALL be parameterized by an immutable `MoonLimitProfile` (`Enabled`, `ToleranceMag`,
`CenterNm`; `With(...)` builder; singletons `Disabled`, `Narrowband` (1.0 / 656), `Broadband`
(0.30 / 540), factory `Custom`). A disabled profile or a null profile argument SHALL short-circuit
to "accept everything" exactly as the previous gate did.

#### Scenario: Null and Disabled behave identically
- **WHEN** `BestSession.For`/`ResolveCandidates`/`SessionSolvers.*` receive `profile: null` or
  `MoonLimitProfile.Disabled`
- **THEN** the emitted windows equal the moon-blind visibility windows

### Requirement: Interval semantics and boundary accuracy
`MoonClearIntersect` SHALL keep its 1-minute walk and locate accept/reject boundaries by linear
interpolation on `ToleranceMag − Δmag(t)`, accurate to a few seconds. A target at or below the
horizon inside a supplied visibility window is an input-contract violation and SHALL fail fast
(throw), not be silently skipped.

#### Scenario: Boundary interpolated between samples
- **WHEN** consecutive minutes straddle the tolerance (Δmag crosses ToleranceMag between them)
- **THEN** the emitted sub-interval boundary falls between the two sample instants at the
  interpolated crossing, not on a sample instant

### Requirement: Calibration anchors are pinned
Tests SHALL pin the calibration anchors at the reference site (Bortle 5, k₅₀₀ 0.28, sun −18°),
mid-altitude geometry: the NB Lorentzian full-moon boundary (sep 60°) sits at Δmag ≈ 1.6 —
**above** the shipped Narrowband tolerance, recording that the gate is deliberately stricter than
the classic rule at full moon; the NB gibbous boundary (sep 48°, the cycle-median anchor for the
shipped 1.0 default) sits at Δmag ≈ 1.0; and the BB half-moon boundary (sep 96°) sits at
Δmag ≈ 0.3 — each within a stated band, so the shipped defaults remain reproducible if K-S
internals ever change. (Both shipped defaults are the respective Lorentzian boundary's
cycle-median Δmag.)

#### Scenario: NB median anchor reproduces
- **WHEN** Δmag is evaluated at the recorded NB gibbous boundary geometry
- **THEN** the result is within the pinned band around 1.0

#### Scenario: Full-moon strictness relationship holds
- **WHEN** Δmag is evaluated at the recorded NB full-moon boundary geometry
- **THEN** the result exceeds the shipped Narrowband tolerance (the gate is stricter than the
  classic rule there)

### Requirement: Purity and thread-safety
The gate path (`MoonLimitProfile`, the Δ function, `MoonClearIntersect`, and every physics call it
makes) SHALL remain pure static / immutable-input code safe for per-target parallel callers, per
the ARCHITECTURE thread-safety contract. The `PlaceBest(..., altitudeQuality: null)` sin(alt)
fast-path dispatch (CONSUMERS #11) SHALL be unaffected.

#### Scenario: Parallel evaluation is unsynchronized
- **WHEN** many targets evaluate the gate concurrently against the same Location and profile
- **THEN** no shared mutable state exists and results equal sequential evaluation

### Requirement: Known validity limits are documented, not masked
The gate SHALL document (not compensate for) two inherited K-S validity limits: separations
< ~10° are outside K-S calibration (any sane tolerance rejects there anyway), and the low-altitude
urban extinction-overdrive (parked ROADMAP item) *understates* Δmag below ~10° target altitude at
high-k sites, making the gate permissive there — consumers keep their existing low-altitude floor
policies.

#### Scenario: Near-moon minutes reject on magnitude, not on a special case
- **WHEN** the target-moon separation is < 10° with the moon up and meaningfully illuminated
- **THEN** Δmag is large enough that any tolerance ≤ 3 mag rejects, with no separation special-case
  in the code

## REMOVED Requirements

### Requirement: Lorentzian moon-avoidance model
**Reason**: Replaced by the K-S Δmag gate. Calibration (2026-07-24) showed the Lorentzian was
never an iso-quality contour: its implied sky-brightness tolerance varied ~10–20× across the lunar
cycle (crescent boundaries at Δmag ≈ 0.1; broadband full-moon boundaries at Δmag ≈ 1.7–2.0 ≈ 5–6×
integration cost).
**Migration**: `MoonAvoidanceProfile` (SeparationDeg/WidthDays/Relax*) → `MoonLimitProfile`
(ToleranceMag/CenterNm); `MoonAvoidance.{LorentzianRequiredSep, RequiredSepWithRelax, IsRejected}`
deleted with no shim; `DaysInLunarCycle` moves to `LunarAge`. TP migrates per the in-repo
direction note (design D6); defaults NB 1.0 / BB 0.30.
