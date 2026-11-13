using System.IO.Compression;
using System.Text;

namespace GovBidPro.Application.Documents;

/// <summary>Approved file types, verified by extension, declared content type and file signature.</summary>
public static class FileSignatureValidator
{
    public sealed record FileKind(string Extension, string ContentType, Func<ReadOnlyMemory<byte>, bool> Matches);

    public static IReadOnlyList<FileKind> Allowed { get; } =
    [
        new(".pdf", "application/pdf", b => StartsWith(b.Span, "%PDF-"u8)),
        new(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", b => IsZipWith(b, "word/document.xml")),
        new(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", b => IsZipWith(b, "xl/workbook.xml")),
        new(".txt", "text/plain", IsPlainUtf8Text),
        new(".csv", "text/csv", IsPlainUtf8Text),
        new(".png", "image/png", b => StartsWith(b.Span, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])),
        new(".jpg", "image/jpeg", b => StartsWith(b.Span, [0xFF, 0xD8, 0xFF])),
    ];

    public static FileKind? Identify(string fileName, string declaredContentType, ReadOnlyMemory<byte> content)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (extension == ".jpeg")
        {
            extension = ".jpg";
        }

        var contentType = (declaredContentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        var kind = Allowed.FirstOrDefault(k => k.Extension == extension);
        if (kind is null || !string.Equals(kind.ContentType, contentType, StringComparison.Ordinal))
        {
            return null;
        }

        return kind.Matches(content) ? kind : null;
    }

    public static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ' ' ? c : '_');
        }

        var cleaned = builder.ToString().Trim();
        if (cleaned.Length > 200)
        {
            cleaned = cleaned[^200..];
        }

        return cleaned.Length == 0 ? "upload" : cleaned;
    }

    private static bool StartsWith(ReadOnlySpan<byte> content, ReadOnlySpan<byte> prefix) => content.StartsWith(prefix);

    private static bool IsPlainUtf8Text(ReadOnlyMemory<byte> content)
    {
        if (content.Span.Contains((byte)0))
        {
            return false;
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetString(content.Span);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsZipWith(ReadOnlyMemory<byte> content, string requiredEntry)
    {
        if (!content.Span.StartsWith("PK\u0003\u0004"u8))
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            return zip.GetEntry(requiredEntry) is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
