using System;

namespace Astronomy.Core.Horizons
{
    /// <summary>
    /// Pointwise maximum of two <see cref="IHorizonProfile"/> instances:
    /// <see cref="AltitudeAt"/> returns the higher of the two component altitudes
    /// at every azimuth. Used to compose a per-site polyline horizon with a user-
    /// chosen scalar floor, so a target qualifies only when it clears whichever
    /// of the two is higher at the target's azimuth.
    /// </summary>
    /// <remarks>
    /// <see cref="MinAltitude"/> returns <c>max(a.MinAltitude, b.MinAltitude)</c>,
    /// which is a safe lower bound on the true minimum of the composed profile --
    /// it may underestimate the actual min when the two components reach their
    /// minima at different azimuths, but the rise/set fast path remains correct
    /// (a target below the returned bound is guaranteed below the profile at
    /// every azimuth).
    /// </remarks>
    public sealed class MaxOfHorizonProfile : IHorizonProfile
    {
        private readonly IHorizonProfile mA;
        private readonly IHorizonProfile mB;

        /// <summary>
        /// Constructs the composed profile. Neither argument may be
        /// <see langword="null"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="a"/> or <paramref name="b"/> is <see langword="null"/>.
        /// </exception>
        public MaxOfHorizonProfile(IHorizonProfile a, IHorizonProfile b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            mA = a;
            mB = b;
        }

        /// <inheritdoc />
        public double AltitudeAt(double azimuthDeg) =>
            Math.Max(mA.AltitudeAt(azimuthDeg), mB.AltitudeAt(azimuthDeg));

        /// <inheritdoc />
        public double MinAltitude => Math.Max(mA.MinAltitude, mB.MinAltitude);
    }
}
