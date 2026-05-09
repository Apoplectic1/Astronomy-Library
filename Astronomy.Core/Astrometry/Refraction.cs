using Astronomy.Core.Astrometry.Meeus;

namespace Astronomy.Core.Astrometry
{
    /// <summary>
    /// Atmospheric refraction at the standard atmosphere (1010 mbar, 10&#176;C). Public
    /// thin wrapper over the internal Meeus <see cref="MeeusUtility.RefractionDeg"/> so
    /// callers outside the assembly (and the new Sun classes) have a discoverable surface
    /// without duplicating the math.
    /// </summary>
    public static class Refraction
    {
        /// <summary>
        /// Atmospheric refraction (degrees) at <paramref name="apparentAltDeg"/> using
        /// Bennett 1982 / Saemundsson. Add this to a geometric (true) altitude to get
        /// apparent altitude as seen through the atmosphere; subtract from apparent to get
        /// geometric. ~0.567&#176; (34&#8242;) at the horizon, 0 above ~85&#176;, 0 below
        /// -1&#176; (no upward bend modelled past the geometric horizon).
        /// </summary>
        /// <param name="apparentAltDeg">Apparent altitude (degrees above the local horizon).</param>
        /// <returns>Refraction angle in degrees; non-negative.</returns>
        public static double BennettDeg(double apparentAltDeg)
            => MeeusUtility.RefractionDeg(apparentAltDeg);
    }
}
