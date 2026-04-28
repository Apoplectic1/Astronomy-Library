using System.IO;
using System.Linq;
using Astronomy.PCL;
using Xunit;

namespace Astronomy.Core.Tests.Tests.PCL
{
    public class XisfReadTests
    {
        private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "TestData", "test.xisf");

        [Fact]
        public void Open_GetInfo_ReadsFloat32()
        {
            Assert.True(File.Exists(FixturePath), $"Fixture missing: {FixturePath}");

            using var x = XisfFile.Open(FixturePath);
            Assert.True(x.ImageCount >= 1);

            var info = x.SelectImage(0);
            Assert.True(info.Width > 0);
            Assert.True(info.Height > 0);
            Assert.True(info.ChannelCount >= 1);
            Assert.Equal(info.Width * (long)info.Height * info.ChannelCount, info.SampleCount);

            float[] samples = x.ReadImageF32();
            Assert.Equal(info.SampleCount, samples.LongLength);
            Assert.Contains(samples, v => v > 0f && float.IsFinite(v));
        }

        [Fact]
        public void ReadImageF32_CallerAllocated_FillsBuffer()
        {
            using var x = XisfFile.Open(FixturePath);
            var info = x.SelectImage(0);

            var buffer = new float[info.SampleCount];
            x.ReadImageF32(buffer);
            Assert.Contains(buffer, v => v > 0f && float.IsFinite(v));
        }

        [Fact]
        public void Open_NonexistentPath_Throws()
        {
            Assert.Throws<FileNotFoundException>(() => XisfFile.Open("Z:\\nope\\does-not-exist.xisf"));
        }
    }
}
