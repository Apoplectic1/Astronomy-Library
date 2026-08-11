# meridian-primitives

## Purpose

Side-of-meridian geometry and flip timing for scheduling: signed hour angle, which side of the
meridian a target is on, transit enumeration, the flip moment inside a session, and splitting
candidate windows into same-side pieces.

## Requirements

### Requirement: Signed hour angle at an instant

The library SHALL report a target's hour angle at a UTC instant as signed hours in
`[-12, +12)`: negative before upper transit, zero at transit, positive after.

#### Scenario: Sign convention around transit

- **WHEN** the hour angle is evaluated shortly before, at, and shortly after a target's
  upper transit
- **THEN** the results are negative, approximately zero, and positive respectively

### Requirement: Side of meridian uses sky-side semantics

The library SHALL report which side of the meridian the target is on at a UTC instant: East
when the hour angle is negative (pre-transit), West at or past transit. The convention is the
target's sky position, not a mount's pier side.

#### Scenario: East before transit, West at and after

- **WHEN** the side is evaluated before a target's upper transit, at the transit instant, and
  after it
- **THEN** the results are East, West, and West respectively

### Requirement: Transit enumeration within an interval

The library SHALL enumerate every upper transit inside a half-open UTC interval, in ascending
order. An interval longer than one sidereal day SHALL yield multiple transits; an interval
containing none SHALL yield an empty list.

#### Scenario: A 24-hour window can hold two transits

- **WHEN** transits are enumerated over a 24-hour interval whose start lies just before a
  transit
- **THEN** two transits are returned (the second one sidereal day — about 23h56m — after the
  first), both inside the interval

### Requirement: Flip time within a session

Given a session interval and a track-past-meridian allowance, the library SHALL return the
first flip instant (transit plus allowance) that falls inside the session, or null when no
flip lands inside it. A transit occurring *before* the session whose shifted flip instant
falls inside the session SHALL still produce that flip instant. A negative allowance
(flip before transit) SHALL be honored arithmetically.

#### Scenario: Pre-session transit, in-session flip

- **WHEN** a target transits 10 minutes before the session starts and the allowance is
  60 minutes past meridian
- **THEN** the flip time is transit + 60 minutes, inside the session, and is returned

#### Scenario: No transit near the session

- **WHEN** no shifted transit falls inside the session
- **THEN** null is returned

### Requirement: Splitting windows at flip boundaries

Given a canonical window list and a track-past-meridian allowance, the library SHALL return
the windows split at every in-window flip instant, ordered and pairwise disjoint (pieces of
one window meet — touch — at flip instants, legal under the interval algebra's contract),
preserving the total covered time. Each returned piece SHALL contain no interior flip
instant beyond the split tolerance. Non-canonical input SHALL be rejected with an exception
(same contract as the interval algebra). Because transit computation carries sub-millisecond
floating-point jitter across recomputations, a split SHALL be suppressed when either
resulting piece would be shorter than one second — re-splitting already-split windows (the
replanning path) is a no-op rather than a source of jitter slivers.

#### Scenario: Window straddling one flip splits in two

- **WHEN** a window contains exactly one flip instant strictly inside it (clear of both
  boundaries)
- **THEN** two intervals are returned, meeting at the flip instant, covering the original
  window exactly

#### Scenario: Flip at or within tolerance of a window boundary is a no-op

- **WHEN** the flip instant falls within one second of a window's start or end — including
  a boundary that IS an earlier split's flip instant, recomputed with jitter
- **THEN** the window is returned unsplit
