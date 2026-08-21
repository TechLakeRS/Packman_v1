using System.IO;
using System.Text;
using Packman.Helpers;
using Xunit;

namespace Packman.Tests;

/// <summary>
/// PSADT scripts run under Windows PowerShell 5.1, which reads a BOM-less file as ANSI.
/// Saving a script back must not change its encoding or a non-ASCII character breaks.
/// </summary>
public sealed class TextFileIOTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("packman-tests").FullName;

    private string PathFor(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Read_detects_utf8_with_bom_and_strips_the_preamble()
    {
        var path = PathFor("bom.ps1");
        File.WriteAllText(path, "Write-Host 'héllo'", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var file = TextFileIO.Read(path);

        Assert.Equal("Write-Host 'héllo'", file.Content);
        Assert.NotEmpty(file.Encoding.GetPreamble());
    }

    [Fact]
    public void Read_detects_utf8_without_bom()
    {
        var path = PathFor("nobom.ps1");
        File.WriteAllText(path, "Write-Host 'hi'", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var file = TextFileIO.Read(path);

        Assert.Equal("Write-Host 'hi'", file.Content);
        Assert.Empty(file.Encoding.GetPreamble());
    }

    [Fact]
    public void Read_detects_utf16_little_endian()
    {
        var path = PathFor("utf16.ps1");
        File.WriteAllText(path, "Write-Host 'hi'", new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

        var file = TextFileIO.Read(path);

        Assert.Equal("Write-Host 'hi'", file.Content);
        Assert.IsType<UnicodeEncoding>(file.Encoding);
    }

    [Fact]
    public void Round_trip_preserves_the_original_bytes()
    {
        var path = PathFor("roundtrip.ps1");
        File.WriteAllText(path, "AppVendor = 'Ünicode'", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var before = File.ReadAllBytes(path);

        var file = TextFileIO.Read(path);
        TextFileIO.Write(path, file.Content, file.Encoding);

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Write_creates_a_file_that_does_not_exist_yet()
    {
        var path = PathFor("new.ps1");

        TextFileIO.Write(path, "content", new UTF8Encoding(false));

        Assert.Equal("content", File.ReadAllText(path));
    }

    [Fact]
    public void Write_leaves_no_temp_file_behind()
    {
        var path = PathFor("clean.ps1");
        TextFileIO.Write(path, "first", new UTF8Encoding(false));
        TextFileIO.Write(path, "second", new UTF8Encoding(false));

        Assert.Equal("second", File.ReadAllText(path));
        Assert.False(File.Exists(path + TextFileIO.TempSuffix));
    }

    [Fact]
    public void Read_reports_crlf_for_windows_line_endings()
        => Assert.True(WriteThenRead("crlf.ps1", "one\r\ntwo").Crlf);

    [Fact]
    public void Read_reports_lf_for_unix_line_endings()
        => Assert.False(WriteThenRead("lf.ps1", "one\ntwo").Crlf);

    private TextFileIO.TextFile WriteThenRead(string name, string content)
    {
        var path = PathFor(name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return TextFileIO.Read(path);
    }
}
