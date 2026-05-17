using Astronomy.Core.Astrometry.Meeus;

namespace Astronomy.Core.Astrometry
{
    /// <summary>
    /// Atmospheric refraction at the standard atmosphere (1010 mbar, 10&#176;C). Public
    /// thin wrapper over the internal Meeus implementation so callers outside the
    /// assembly have a discoverable surface without duplicating the math.
    /// </summary>
    public static class Refraction
    {
        /// <summary>
        /// Atmospheric refraction (degrees) at <paramref name="trueAltDeg"/> using
        /// Saemundsson 1986. Input is <b>geometric (true)</b> altitude; add the result
        /// to true altitude to get apparent altitude (the direction this formula was
        /// derived for). Use when starting from a geometric altitude (e.g. converting
        /// a topocentric AltAz computation to what the observer sees). ~0.483&#176;
        /// (29&#8242;) at the geometric horizon -- the moon at geometric altitude 0
        /// appears 29&#8242; above the horizon visually; conversely the visually-
        /// observed horizon (apparent 0) corresponds to geometric ~-34&#8242;.
        /// Returns 0 for true altitudes below ~-1&#176;.
        /// </summary>
        /// <param name="trueAltDeg">Geometric (true) altitude (degrees, pre-refraction).</param>
        /// <returns>Refraction angle in degrees; non-negative.</returns>
        public static double SaemundssonDeg(double trueAltDeg)
            => MeeusUtility.SaemundssonRefractionDeg(trueAltDeg);
    }
}
