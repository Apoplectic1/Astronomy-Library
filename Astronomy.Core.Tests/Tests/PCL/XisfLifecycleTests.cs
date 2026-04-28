using System;
using System.Diagnostics;
using System.IO;
using Astronomy.PCL;
using Xunit;

namespace Astronomy.Core.Tests.Tests.PCL
{
    public class XisfLifecycleTests
    {
        private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "TestData", "test.xisf");

        [Fact]
        public void OpenClose_Loop_DoesNotLeak()
        {
            Assert.True(File.Exists(FixturePath));

            var proc = Process.GetCurrentProcess();
            proc.Refresh();
            long startWs = proc.WorkingSet64;

            for (int i = 0; i < 1000; i++)
            {
                using var x = XisfFile.Open(FixturePath);
                _ = x.ImageCount;
            }

            proc.Refresh();
            long endWs = proc.WorkingSet64;

            // Allow generous headroom — PCL may pool memory. The point is to catch
            // a leak that grows linearly per open/close, which would push past 1 GB easily.
            Assert.True(endWs < 1_500_000_000L,
                $"Working set grew suspiciously: start={startWs:N0}, end={endWs:N0}");
        }

        [Fact]
        public void DoubleDispose_DoesNotThrow()
        {
            var x = XisfFile.Open(FixturePath);
            x.Dispose();
            x.Dispose();
        }

        [Fact]
        public void AccessAfterDispose_Throws()
        {
            var x = XisfFile.Open(FixturePath);
            x.Dispose();
            Assert.Throws<ObjectDisposedException>(() => x.ImageCount);
        }
    }
}
