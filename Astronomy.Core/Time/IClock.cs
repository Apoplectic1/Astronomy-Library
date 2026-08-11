using System;

namespace Astronomy.Core.Time
{
    /// <summary>
    /// The clock abstraction consumers inject instead of reading the ambient clock.
    /// A scheduler whose engine takes "now" through this seam can run a full simulated
    /// night — or sweep hundreds of nights — in milliseconds under test, with the
    /// production implementation being a trivial system-clock wrapper.
    /// </summary>
    /// <remarks>
    /// Contract: <see cref="UtcNow"/> is always <see cref="DateTimeKind.Utc"/>, so the
    /// value flows into the Library's UTC-gated math (see <see cref="TimeKindGuard"/>)
    /// without conversion. Test fakes (fixed or stepping clocks) are consumer-side —
    /// the Library ships only the contract and <see cref="SystemClock"/>.
    /// </remarks>
    public interface IClock
    {
        /// <summary>The current instant, <see cref="DateTimeKind.Utc"/>.</summary>
        DateTime UtcNow { get; }
    }

    /// <summary>
    /// Production <see cref="IClock"/>: the system UTC clock.
    /// </summary>
    public sealed class SystemClock : IClock
    {
        /// <summary>The shared instance — the class is stateless.</summary>
        public static SystemClock Instance { get; } = new SystemClock();

        private SystemClock() { }

        /// <inheritdoc/>
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
