using System.Text;
using Astronomy.NINA.Xisf;
using Xunit;

namespace Astronomy.NINA.Tests.Xisf;

public class XisfHeaderReaderTests : IDisposable
{
    private readonly string mTempDir;

    public XisfHeaderReaderTests()
    {
        mTempDir = Path.Combine(Path.GetTempPath(), "astronomy-nina-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mTempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(mTempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    /// <summary>
    /// Builds a minimal valid XISF file at the given path with the supplied FITS keywords.
    /// Format: 8-byte signature + 4-byte LE XML length + 4-byte reserved + UTF-8 XML payload.
    /// No image attachment block — header-only readers don't need one.
    /// </summary>
    private static void WriteSyntheticXisf(string path, IDictionary<string, string> fitsKeywords)
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.Append("<xisf version=\"1.0\" xmlns=\"http://www.pixinsight.com/xisf\">");
        xml.Append("<Image>");
        foreach (var kv in fitsKeywords)
        {
            // Strings get FITS-quoted; numerics unquoted.
            string val = double.TryParse(kv.Value, out _) ? kv.Value : $"'{kv.Value}'";
            xml.Append($"<FITSKeyword name=\"{kv.Key}\" value=\"{val}\" comment=\"\" />");
        }
        xml.Append("</Image>");
        xml.Append("</xisf>");

        byte[] xmlBytes = Encoding.UTF8.GetBytes(xml.ToString());

        byte[] header = new byte[16];
        Encoding.ASCII.GetBytes("XISF0100", 0, 8, header, 0);
        int len = xmlBytes.Length;
        header[8] = (byte)(len & 0xFF);
        header[9] = (byte)((len >> 8) & 0xFF);
        header[10] = (byte)((len >> 16) & 0xFF);
        header[11] = (byte)((len >> 24) & 0xFF);
        // bytes 12-15 reserved (left as zero)

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write(header, 0, 16);
        fs.Write(xmlBytes, 0, xmlBytes.Length);
    }

    [Fact]
    public async Task Read_ValidXisf_PopulatesAccessors()
    {
        string path = Path.Combine(mTempDir, "valid.xisf");
        WriteSyntheticXisf(path, new Dictionary<string, string>
        {
            ["OBJECT"] = "M51",
            ["RA"] = "202.469625",
            ["DEC"] = "47.195167",
            ["DATE-OBS"] = "2024-02-18T04:51:28",
            ["EXPTIME"] = "600.0",
            ["FILTER"] = "H",
            ["GAIN"] = "111",
            ["OFFSET"] = "10",
            ["SET-TEMP"] = "-20.0",
            ["XBINNING"] = "1",
            ["YBINNING"] = "1",
            ["IMAGETYP"] = "LIGHT",
            ["INSTRUME"] = "ZWO ASI183MM Pro",
        });

        XisfHeader h = await XisfHeaderReader.ReadAsync(path);

        Assert.Equal("M51", h.ObjectName);
        Assert.Equal(202.469625, h.RaDegrees);
        Assert.Equal(47.195167, h.DecDegrees);
        Assert.Equal(600.0, h.ExposureSec);
        Assert.Equal("H", h.Filter);
        Assert.Equal(111, h.Gain);
        Assert.Equal(10, h.OffsetRaw);
        Assert.Equal(2, h.OffsetNormalized);    // 10 / 5 (ASI183)
        Assert.Equal(-20.0, h.SetTempC);
        Assert.Equal(1, h.XBinning);
        Assert.Equal("LIGHT", h.ImageType);
        Assert.Equal(DateTimeKind.Utc, h.DateObsUtc!.Value.Kind);
    }

    [Fact]
    public async Task Read_StripsFitsQuotes()
    {
        string path = Path.Combine(mTempDir, "quoted.xisf");
        WriteSyntheticXisf(path, new Dictionary<string, string> { ["OBJECT"] = "M31 Andromeda" });

        XisfHeader h = await XisfHeaderReader.ReadAsync(path);
        // Synthetic writer FITS-quoted string values; reader strips surrounding quotes.
        Assert.Equal("M31 Andromeda", h.ObjectName);
    }

    [Fact]
    public async Task Read_BadSignature_Throws()
    {
        string path = Path.Combine(mTempDir, "bad-sig.xisf");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("NOTANXISFFILE000"));

        await Assert.ThrowsAsync<InvalidDataException>(() => XisfHeaderReader.ReadAsync(path));
    }

    [Fact]
    public async Task Read_MalformedXml_Throws()
    {
        string path = Path.Combine(mTempDir, "bad-xml.xisf");
        byte[] xmlBytes = Encoding.UTF8.GetBytes("<not-valid-xml");
        byte[] header = new byte[16];
        Encoding.ASCII.GetBytes("XISF0100", 0, 8, header, 0);
        int len = xmlBytes.Length;
        header[8] = (byte)(len & 0xFF);
        header[9] = (byte)((len >> 8) & 0xFF);
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            fs.Write(header, 0, 16);
            fs.Write(xmlBytes, 0, xmlBytes.Length);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => XisfHeaderReader.ReadAsync(path));
    }

    [Fact]
    public async Task Read_EmptyPath_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => XisfHeaderReader.ReadAsync(""));
    }
}
