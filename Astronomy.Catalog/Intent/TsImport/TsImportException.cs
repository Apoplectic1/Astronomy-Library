namespace Astronomy.Catalog.Intent.TsImport;

/// <summary>
/// Raised when the one-time Target Scheduler lift aborts: a non-empty destination store, a
/// required source field that is missing/blank/unparseable, an enum code with no translation, or a
/// dangling source reference. The message names the source entity, field, offending value, and
/// expectation; the import transaction is rolled back, so the store never holds a partial lift.
/// The driver surfaces the message (console + log) and stops — no fallback, no skip-the-row.
/// </summary>
public sealed class TsImportException : Exception
{
    /// <summary>Creates the exception with a diagnostic naming entity, field, value, and expectation.</summary>
    public TsImportException(string message) : base(message) { }
}
