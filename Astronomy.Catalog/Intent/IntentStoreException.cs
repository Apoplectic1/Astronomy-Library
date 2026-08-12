namespace Astronomy.Catalog.Intent;

/// <summary>
/// Raised when the intent store cannot be opened or evolved: a non-local path, a store newer than
/// this library understands, or a migration script failure. The message names the offending path,
/// versions, or script — callers surface it and stop; there is no degraded fallback path.
/// </summary>
public sealed class IntentStoreException : Exception
{
    /// <summary>Creates the exception with a caller-facing message.</summary>
    public IntentStoreException(string message) : base(message) { }

    /// <summary>Creates the exception with a caller-facing message and the underlying cause.</summary>
    public IntentStoreException(string message, Exception innerException) : base(message, innerException) { }
}
