using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace GovBidPro.Application.Documents;

/// <summary>
/// Best-effort detection of security/handling markings (§9B.2, SEC-03). A match is a detection
/// signal that triggers rejection before storage; absence of a match does NOT prove the absence of
/// FCI/CUI/classified content. PDF detection covers only uncompressed text streams in P0.
/// </summary>
public static partial class ProhibitedMarkerDetector
{
    private const int MaxScanCharacters = 2_000_000;

    public static string? Detect(string extension, ReadOnlyMemory<byte> content)
    {
        var text = ExtractText(extension, content);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > MaxScanCharacters)
        {
            text = text[..MaxScanCharacters];
        }

        if (ClassifiedMarking().IsMatch(text))
        {
            return "marker.classified";
        }

        if (CuiMarking().IsMatch(text))
        {
            return "marker.cui";
        }

        return LegacyMarking().IsMatch(text) ? "marker.fouo" : null;
    }

    private static string ExtractText(string extension, ReadOnlyMemory<byte> content)
    {
        switch (extension)
        {
            case ".txt":
            case ".csv":
                return Encoding.UTF8.GetString(content.Span);
            case ".pdf":
                return Encoding.Latin1.GetString(content.Span);
            case ".docx":
                return ReadZipText(content, e => e.FullName.StartsWith("word/", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal));
            case ".xlsx":
                return ReadZipText(content, e => e.FullName is "xl/sharedStrings.xml" || e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal));
            default:
                return string.Empty;
        }
    }

    private static string ReadZipText(ReadOnlyMemory<byte> content, Func<ZipArchiveEntry, bool> include)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var builder = new StringBuilder();
            foreach (var entry in zip.Entries.Where(include))
            {
                if (entry.Length > MaxScanCharacters * 4L)
                {
                    continue;
                }

                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var xml = reader.ReadToEnd();

                // Strip XML tags so markings split across runs still match.
                builder.Append(XmlTag().Replace(xml, string.Empty)).Append('\n');
                if (builder.Length > MaxScanCharacters)
                {
                    break;
                }
            }

            return builder.ToString();
        }
        catch (InvalidDataException)
        {
            return string.Empty;
        }
    }

    [GeneratedRegex(@"\b(TOP\s+SECRET|SECRET|CONFIDENTIAL)\s*//|\bNOFORN\b|\b(CLASSIFIED\s+BY|DECLASSIFY\s+ON)\s*:", RegexOptions.CultureInvariant)]
    private static partial Regex ClassifiedMarking();

    [GeneratedRegex(@"\bCUI\s*//|^\s*CUI\s*$|CONTROLLED\s+UNCLASSIFIED\s+INFORMATION|\bCONTROLLED\s+BY\s*:", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex CuiMarking();

    [GeneratedRegex(@"\bFOUO\b|FOR\s+OFFICIAL\s+USE\s+ONLY", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyMarking();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex XmlTag();
}
