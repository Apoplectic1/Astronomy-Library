namespace Astronomy.Core.Session
{
    /// <summary>
    /// Which side of the local meridian a target is on — <b>sky-side semantics</b>: the
    /// target's position, not a mount's pier side.
    /// </summary>
    /// <remarks>
    /// East = hour angle negative (before upper transit, target rising toward the
    /// meridian); West = at or past transit. Mapping to a GEM's pier side is the mount
    /// adapter's job, and the two vocabularies are inverted in the way that regularly
    /// trips people: ASCOM's <c>pierEast</c> means the OTA is on the east side of the
    /// pier <em>pointing west</em> — i.e. at a target this library calls
    /// <see cref="West"/>. Keeping mount vocabulary out of the astrometry layer is
    /// deliberate.
    /// </remarks>
    public enum MeridianSide
    {
        /// <summary>Target east of the meridian: hour angle &lt; 0, before upper transit.</summary>
        East,

        /// <summary>Target at or west of the meridian: hour angle &gt;= 0, at/past upper transit.</summary>
        West,
    }
}
