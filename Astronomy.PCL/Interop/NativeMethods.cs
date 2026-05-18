using System.Runtime.InteropServices;
using System.Text;

namespace Astronomy.PCL.Interop
{
    // Cross-thread caveat for AstronomyXisf_GetLastErrorMessage: the native side
    // stores the last error in `thread_local` storage (src/LastError.cpp). A caller
    // that catches an exception on thread A and queries the message from thread B
    // will read an empty string -- not a bug, just the per-call-status semantics.
    // Stick to retrieving the message on the same thread that made the failing call.
    internal static class NativeMethods
    {
        private const string Lib = "Astronomy.PCL.Native";

        [DllImport(Lib, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int AstronomyXisf_Open(string utf16Path, out System.IntPtr outHandle);

        [DllImport(Lib, ExactSpelling = true)]
        internal static extern int AstronomyXisf_Close(System.IntPtr handle);

        [DllImport(Lib, ExactSpelling = true)]
        internal static extern int AstronomyXisf_NumberOfImages(System.IntPtr handle, out int outCount);

        [DllImport(Lib, ExactSpelling = true)]
        internal static extern int AstronomyXisf_SelectImage(System.IntPtr handle, int index);

        [DllImport(Lib, ExactSpelling = true)]
        internal static extern int AstronomyXisf_GetImageInfo(System.IntPtr handle, out NativeImageInfo outInfo);

        [DllImport(Lib, ExactSpelling = true)]
        internal static extern unsafe int AstronomyXisf_ReadImageF32(System.IntPtr handle, float* outSamples, long samplesCount);

#pragma warning disable CA1838 // Error-path retrieval; marshalling overhead is negligible vs. the exception itself.
        [DllImport(Lib, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int AstronomyXisf_GetLastErrorMessage(StringBuilder outBuffer, int bufferCharCount, out int outRequiredCharCount);
#pragma warning restore CA1838

        [DllImport(Lib, ExactSpelling = true)]
        internal static extern int AstronomyXisf_Ping(int a, int b);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeImageInfo
    {
        public int Width;
        public int Height;
        public int NumberOfChannels;
        public int BitsPerSample;
        public int IeeefpSampleFormat;
        public int ColorSpace;
        public int Reserved0;
        public int Reserved1;
    }
}
