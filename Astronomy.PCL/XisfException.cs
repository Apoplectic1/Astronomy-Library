using System;

namespace Astronomy.PCL
{
    /// <summary>
    /// Thrown when an Astronomy.PCL.Native call returns a non-success status.
    /// </summary>
    public sealed class XisfException : Exception
    {
        internal XisfException(int statusCode, string message)
            : base(string.IsNullOrEmpty(message) ? $"Astronomy.PCL.Native status {statusCode}" : message)
        {
            StatusCode = statusCode;
        }

        /// <summary>The native status code. See AstronomyXisfStatus in the C ABI header.</summary>
        public int StatusCode { get; }
    }
}
