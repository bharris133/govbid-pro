using GovBidPro.Domain.Enums;

namespace GovBidPro.Application.Opportunities;

/// <summary>
/// Explicit, versioned interpretation of a response type from a notice (handoff §5: mappings must be
/// explicit and revisable). A notice type alone does not determine RFP vs RFQ vs RFI, so v1 only uses
/// explicit wording; everything else is <see cref="ResponseType.Undetermined"/> for a user to decide.
/// Change <see cref="RuleVersion"/> whenever the rules change.
/// </summary>
public static class ResponseTypeMapper
{
    public const string RuleVersion = "v1";

    public static ResponseType Map(SourceNoticeType noticeType, string? title, string? sourceNoticeTypeRaw)
    {
        switch (noticeType)
        {
            case SourceNoticeType.SourcesSought:
                return ResponseType.SourcesSought;
            case SourceNoticeType.AwardNotice:
                return ResponseType.NoResponseExpected;
            case SourceNoticeType.PreSolicitation:
            case SourceNoticeType.Solicitation:
            case SourceNoticeType.CombinedSynopsisSolicitation:
            case SourceNoticeType.SpecialNotice:
                return FromExplicitWording(title, sourceNoticeTypeRaw);
            default:
                return ResponseType.Undetermined;
        }
    }

    private static ResponseType FromExplicitWording(string? title, string? raw)
    {
        var text = $" {title} {raw} ".ToUpperInvariant();
        var rfi = text.Contains("REQUEST FOR INFORMATION", StringComparison.Ordinal) || ContainsToken(text, "RFI");
        var rfq = text.Contains("REQUEST FOR QUOTATION", StringComparison.Ordinal) || text.Contains("REQUEST FOR QUOTE", StringComparison.Ordinal) || ContainsToken(text, "RFQ");
        var rfp = text.Contains("REQUEST FOR PROPOSAL", StringComparison.Ordinal) || ContainsToken(text, "RFP");

        // Ambiguous wording is left for a human to decide.
        return (rfi, rfq, rfp) switch
        {
            (true, false, false) => ResponseType.Rfi,
            (false, true, false) => ResponseType.Rfq,
            (false, false, true) => ResponseType.Rfp,
            _ => ResponseType.Undetermined,
        };
    }

    private static bool ContainsToken(string upperText, string token)
    {
        var index = 0;
        while ((index = upperText.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 ? ' ' : upperText[index - 1];
            var afterIndex = index + token.Length;
            var after = afterIndex >= upperText.Length ? ' ' : upperText[afterIndex];
            if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after))
            {
                return true;
            }

            index = afterIndex;
        }

        return false;
    }
}
