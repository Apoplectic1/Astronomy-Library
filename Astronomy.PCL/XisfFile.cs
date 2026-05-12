using System;
using System.IO;
using Astronomy.PCL.Interop;

namespace Astronomy.PCL
{
    /// <summary>
    /// Reads an XISF (PixInsight Extensible Image Serialization Format) file via the native PCL wrapper.
    /// </summary>
    /// <remarks>
    /// Lifecycle: <see cref="Open"/> creates an open handle; <see cref="Dispose"/> closes and frees it.
    /// All access after Dispose throws <see cref="ObjectDisposedException"/>. Not thread-safe — use one
    /// instance per thread.
    /// </remarks>
    public sealed class XisfFile : IDisposable
    {
        private IntPtr _handle;
        private readonly string _path;

        private XisfFile(IntPtr handle, string path)
        {
            _handle = handle;
            _path = path;
        }

        /// <summary>Open an XISF file at <paramref name="path"/>. Throws <see cref="XisfException"/> on failure.</summary>
        public static XisfFile Open(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            if (!File.Exists(path)) throw new FileNotFoundException("XISF file not found.", path);

            int status = NativeMethods.AstronomyXisf_Open(path, out IntPtr handle);
            ThrowOnError(status);
            return new XisfFile(handle, path);
        }

        /// <summary>Full path of the opened file.</summary>
        public string FilePath => _path;

        /// <summary>Number of images in the file.</summary>
        public int ImageCount
        {
            get
            {
                ThrowIfDisposed();
                int status = NativeMethods.AstronomyXisf_NumberOfImages(_handle, out int count);
                ThrowOnError(status);
                return count;
            }
        }

        /// <summary>Select image <paramref name="index"/> as the current image and return its info.</summary>
        public XisfImageInfo SelectImage(int index)
        {
            ThrowIfDisposed();
            int status = NativeMethods.AstronomyXisf_SelectImage(_handle, index);
            ThrowOnError(status);
            status = NativeMethods.AstronomyXisf_GetImageInfo(_handle, out NativeImageInfo info);
            ThrowOnError(status);
            return new XisfImageInfo(
                info.Width,
                info.Height,
                info.NumberOfChannels,
                info.BitsPerSample,
                info.IeeefpSampleFormat != 0,
                (XisfColorSpace)info.ColorSpace);
        }

        /// <summary>
        /// Read the currently selected image as float32 samples. Buffer is planar:
        /// <c>plane0 (W*H), plane1 (W*H), …</c>, length <c>Width*Height*ChannelCount</c>.
        /// </summary>
        public float[] ReadImageF32()
        {
            ThrowIfDisposed();
            int status = NativeMethods.AstronomyXisf_GetImageInfo(_handle, out NativeImageInfo info);
            ThrowOnError(status);
            long samples = (long)info.Width * info.Height * info.NumberOfChannels;
            if (samples <= 0) return Array.Empty<float>();
            var buffer = new float[samples];
            ReadImageF32(buffer);
            return buffer;
        }

        /// <summary>
        /// Read the currently selected image into <paramref name="destination"/>.
        /// The buffer must be exactly <c>Width*Height*ChannelCount</c> elements; planar layout.
        /// </summary>
        public void ReadImageF32(float[] destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ThrowIfDisposed();
            unsafe
            {
                fixed (float* p = destination)
                {
                    int status = NativeMethods.AstronomyXisf_ReadImageF32(_handle, p, destination.LongLength);
                    ThrowOnError(status);
                }
            }
        }

        /// <summary>Close the native handle. Idempotent. Subsequent property reads throw <see cref="ObjectDisposedException"/>.</summary>
        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                _ = NativeMethods.AstronomyXisf_Close(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>Finalizer: closes the native handle if Dispose was not called.</summary>
        ~XisfFile()
        {
            if (_handle != IntPtr.Zero)
            {
                _ = NativeMethods.AstronomyXisf_Close(_handle);
                _handle = IntPtr.Zero;
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        }

        private static void ThrowOnError(int status)
        {
            if (status == 0) return;
            throw new XisfException(status, GetLastErrorMessage());
        }

        private static string GetLastErrorMessage()
        {
            int needed;
            int status = NativeMethods.AstronomyXisf_GetLastErrorMessage(null, 0, out needed);
            if (status != 0 || needed <= 1) return string.Empty;
            var sb = new System.Text.StringBuilder(needed);
            status = NativeMethods.AstronomyXisf_GetLastErrorMessage(sb, needed, out _);
            return status == 0 ? sb.ToString() : string.Empty;
        }
    }
}
