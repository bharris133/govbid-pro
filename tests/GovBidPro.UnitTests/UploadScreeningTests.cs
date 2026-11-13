using System.IO.Compression;
using System.Text;
using GovBidPro.Application.Documents;

namespace GovBidPro.UnitTests;

public sealed class UploadScreeningTests
{
    [Theory]
    [InlineData("CUI//SP-PRVCY\nbody", "marker.cui")]
    [InlineData("Header\nCUI\nbody", "marker.cui")]
    [InlineData("This document contains CONTROLLED UNCLASSIFIED INFORMATION", "marker.cui")]
    [InlineData("SECRET//NOFORN", "marker.classified")]
    [InlineData("TOP SECRET//SI", "marker.classified")]
    [InlineData("Classified By: someone", null)]
    [InlineData("CLASSIFIED BY: someone", "marker.classified")]
    [InlineData("FOR OFFICIAL USE ONLY", "marker.fouo")]
    [InlineData("Our team has experience with cui handling programs and secret sauce.", null)]
    [InlineData("Ordinary proposal text about cloud migration.", null)]
    public void Detects_security_markings_in_text(string text, string? expected)
    {
        Assert.Equal(expected, ProhibitedMarkerDetector.Detect(".txt", Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void Detects_markings_split_across_docx_runs()
    {
        var docx = BuildDocx("<w:p><w:r><w:t>CUI</w:t></w:r><w:r><w:t>//SP-CTI</w:t></w:r></w:p>");
        Assert.Equal("marker.cui", ProhibitedMarkerDetector.Detect(".docx", docx));
    }

    [Fact]
    public void Accepts_matching_signatures_and_rejects_spoofed_types()
    {
        Assert.NotNull(FileSignatureValidator.Identify("a.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.7 rest")));
        Assert.Null(FileSignatureValidator.Identify("a.pdf", "application/pdf", Encoding.ASCII.GetBytes("MZ executable")));
        Assert.Null(FileSignatureValidator.Identify("a.pdf", "text/plain", Encoding.ASCII.GetBytes("%PDF-1.7")));
        Assert.Null(FileSignatureValidator.Identify("a.exe", "application/octet-stream", Encoding.ASCII.GetBytes("MZ")));
        Assert.Null(FileSignatureValidator.Identify("a.html", "text/html", Encoding.ASCII.GetBytes("<script>")));
        Assert.Null(FileSignatureValidator.Identify("a.txt", "text/plain", new byte[] { 0x41, 0x00, 0x42 }));
        Assert.NotNull(FileSignatureValidator.Identify("a.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", BuildDocx("<w:p/>")));
        Assert.Null(FileSignatureValidator.Identify("a.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Encoding.ASCII.GetBytes("PK\u0003\u0004garbage")));
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\temp\\file<script>.pdf", "C__temp_file_script_.pdf")]
    [InlineData("", "upload")]
    public void Sanitizes_file_names(string input, string expected)
    {
        Assert.Equal(expected, FileSignatureValidator.SanitizeFileName(input));
    }

    private static byte[] BuildDocx(string bodyXml)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write($"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{bodyXml}</w:body></w:document>");
        }

        return stream.ToArray();
    }
}
