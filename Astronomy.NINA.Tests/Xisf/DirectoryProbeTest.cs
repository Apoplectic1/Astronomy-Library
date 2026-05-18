using Xunit;
using Xunit.Abstractions;

namespace Astronomy.NINA.Tests.Xisf;

public class DirectoryProbeTest
{
    private readonly ITestOutputHelper mOut;
    public DirectoryProbeTest(ITestOutputHelper output) => mOut = output;

    [Fact]
    public async Task ProbeSingleFile_DumpsKeywords()
    {
        string root = Environment.GetEnvironmentVariable("TP_SMOKE_IMAGE_LIBRARY");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            mOut.WriteLine("Skip: env not set.");
            return;
        }

        string firstTarget = Directory.EnumerateDirectories(root).First();
        string capturesDir = Path.Combine(firstTarget, "Captures");
        string firstCam = Directory.EnumerateDirectories(capturesDir).First(d => !Path.GetFileName(d).Equals("Calibration", StringComparison.OrdinalIgnoreCase));
        string firstFilter = Directory.EnumerateDirectories(firstCam).First();
        string firstFile = Directory.EnumerateFiles(firstFilter, "*.xisf").First();

        mOut.WriteLine($"Reading: {firstFile}");
        try
        {
            Astronomy.NINA.Xisf.XisfHeader h = await Astronomy.NINA.Xisf.XisfHeaderReader.ReadAsync(firstFile);
            mOut.WriteLine($"OBJECT:   {h.ObjectName ?? "(null)"}");
            mOut.WriteLine($"RA:       {h.RaDegrees}");
            mOut.WriteLine($"DEC:      {h.DecDegrees}");
            mOut.WriteLine($"DATE-OBS: {h.DateObsUtc}");
            mOut.WriteLine($"EXPTIME:  {h.ExposureSec}");
            mOut.WriteLine($"FILTER:   {h.Filter}");
            mOut.WriteLine($"GAIN:     {h.Gain}");
            mOut.WriteLine($"OFFSET:   {h.OffsetRaw} → norm {h.OffsetNormalized}");
            mOut.WriteLine($"SET-TEMP: {h.SetTempC}");
            mOut.WriteLine($"X/YBIN:   {h.XBinning}/{h.YBinning}");
            mOut.WriteLine($"IMAGETYP: {h.ImageType}");
            mOut.WriteLine($"INSTRUME: {h.Instrument}");
            mOut.WriteLine($"INSTRUME comment: {h.InstrumentDescription ?? "(none)"}");
            mOut.WriteLine($"-- All keywords (value | comment) --");
            foreach (string k in h.KeywordNames.OrderBy(s => s))
            {
                var e = h.Entry(k);
                mOut.WriteLine($"  {k,-12} = {e?.Value,-40} | {e?.Comment ?? ""}");
            }
        }
        catch (Exception ex)
        {
            mOut.WriteLine($"FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [Fact]
    public void ProbeLibrary_DumpsStructure()
    {
        string root = Environment.GetEnvironmentVariable("TP_SMOKE_IMAGE_LIBRARY");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            mOut.WriteLine("Skip: env not set.");
            return;
        }

        mOut.WriteLine($"Root: {root}");
        string[] targetDirs = Directory.EnumerateDirectories(root).ToArray();
        mOut.WriteLine($"Target dirs: {targetDirs.Length}");
        for (int i = 0; i < Math.Min(3, targetDirs.Length); i++)
        {
            string td = targetDirs[i];
            mOut.WriteLine($"  [{i}] {Path.GetFileName(td)}");
            string capturesDir = Path.Combine(td, "Captures");
            mOut.WriteLine($"      Captures exists: {Directory.Exists(capturesDir)}");
            if (Directory.Exists(capturesDir))
            {
                string[] camDirs = Directory.EnumerateDirectories(capturesDir).ToArray();
                mOut.WriteLine($"      Camera dirs: {camDirs.Length}");
                foreach (string cd in camDirs)
                {
                    mOut.WriteLine($"        cam: {Path.GetFileName(cd)}");
                    string[] filterDirs = Directory.EnumerateDirectories(cd).ToArray();
                    foreach (string fd in filterDirs)
                    {
                        string[] xisfs = Directory.EnumerateFiles(fd, "*.xisf", SearchOption.TopDirectoryOnly).ToArray();
                        mOut.WriteLine($"          filter: {Path.GetFileName(fd)}  ({xisfs.Length} .xisf)");
                    }
                }
            }
        }
    }
}
