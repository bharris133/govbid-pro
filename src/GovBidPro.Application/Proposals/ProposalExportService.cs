using System.Globalization;
using System.Text;
using GovBidPro.Application.Auditing;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Application.Proposals;

public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Basic exports of a proposal's own data (P1.5): the compliance matrix as CSV and a working outline as
/// Markdown. Access follows <see cref="ProposalService.GetAsync"/> (tenant-scoped, 404 across tenants).
/// Exports are generated on request and never stored.
/// </summary>
public sealed class ProposalExportService(ProposalService proposals, IAuditLog audit, TimeProvider clock)
{
    private static readonly string[] MatrixHeader = ["Reference", "Requirement", "Source location", "Category", "Mandatory", "Status", "Response section", "Response location", "Owner", "Evidence", "Last updated (UTC)"];

    public async Task<ExportFile> ComplianceMatrixCsvAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        var detail = await proposals.GetAsync(proposalId, cancellationToken);
        var csv = new StringBuilder();
        AppendRow(csv, MatrixHeader);
        foreach (var r in detail.Requirements)
        {
            AppendRow(csv, [
                r.Reference, r.Text, r.SourceLocator, r.Category, r.Mandatory ? "Yes" : "No", StatusLabel(r.Status), r.SectionTitle, r.ResponseLocation, r.Assignee, r.Evidence,
                r.UpdatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ]);
        }

        await audit.RecordAsync(AuditActions.ExportGenerated, "proposal", proposalId.ToString(), AuditOutcome.Success, "compliance_matrix_csv", cancellationToken);

        // UTF-8 with BOM so spreadsheet applications detect the encoding.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return new ExportFile(bytes, "text/csv", $"{Slug(detail.Title)}-compliance-matrix.csv");
    }

    public async Task<ExportFile> OutlineMarkdownAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        var d = await proposals.GetAsync(proposalId, cancellationToken);
        var md = new StringBuilder();
        md.Append("# ").AppendLine(Line(d.Title)).AppendLine();
        md.Append("- Opportunity: ").AppendLine(Line(d.OpportunityTitle));
        md.Append("- Solicitation number: ").AppendLine(Line(d.SolicitationNumber ?? "Not stated"));
        md.Append("- Status: ").AppendLine(Line(d.StatusLabel));
        md.Append("- Owner: ").AppendLine(Line(d.Owner));
        md.Append("- Response due: ").AppendLine(d.DueAt is { } due ? due.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : Line(d.DueText ?? "Not stated"));
        md.Append("- Exported: ").AppendLine(clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        md.AppendLine().AppendLine("The solicitation is authoritative. Verify the deadline and submission method at the source. GovBid Pro does not submit responses.").AppendLine();

        md.AppendLine("## Sections").AppendLine();
        foreach (var s in d.Sections)
        {
            md.Append(CultureInfo.InvariantCulture, $"{s.Order}. ").Append(Line(s.Title)).Append(" (").Append(Line(s.Assignee ?? "unassigned")).Append(s.IsComplete ? ", complete" : string.Empty).AppendLine(")");
            foreach (var r in d.Requirements.Where(r => r.SectionId == s.Id))
            {
                AppendRequirement(md, r, "   ");
            }
        }

        var unlinked = d.Requirements.Where(r => r.SectionId is null).ToList();
        if (unlinked.Count > 0)
        {
            md.AppendLine().AppendLine("## Requirements not linked to a section").AppendLine();
            foreach (var r in unlinked)
            {
                AppendRequirement(md, r, string.Empty);
            }
        }

        md.AppendLine().AppendLine("## Compliance summary").AppendLine();
        md.Append(CultureInfo.InvariantCulture, $"{d.Compliance.Total} requirements, {d.Compliance.Mandatory} mandatory, {d.Compliance.MandatoryOpen} mandatory still open, {d.Compliance.Compliant} compliant, {d.Compliance.Exceptions} exceptions.").AppendLine();

        await audit.RecordAsync(AuditActions.ExportGenerated, "proposal", proposalId.ToString(), AuditOutcome.Success, "outline_markdown", cancellationToken);
        return new ExportFile(Encoding.UTF8.GetBytes(md.ToString()), "text/markdown", $"{Slug(d.Title)}-outline.md");
    }

    /// <summary>
    /// RFC 4180 quoting plus spreadsheet formula neutralization: a cell that starts with = + - @, tab or
    /// carriage return gets a leading apostrophe so user-entered text cannot run as a formula.
    /// </summary>
    public static string CsvCell(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return text.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }

    private static void AppendRow(StringBuilder csv, IEnumerable<string?> cells) => csv.AppendJoin(',', cells.Select(CsvCell)).Append("\r\n");

    private static void AppendRequirement(StringBuilder md, RequirementDto r, string indent) =>
        md.Append(indent).Append("- [").Append(r.Status is ComplianceStatus.Compliant or ComplianceStatus.Exception ? 'x' : ' ').Append("] ")
            .Append(Line(r.Reference)).Append(r.Mandatory ? " (mandatory): " : ": ").Append(Line(r.Text)).Append(" [").Append(StatusLabel(r.Status)).AppendLine("]");

    private static string StatusLabel(ComplianceStatus status) => status switch
    {
        ComplianceStatus.NotStarted => "Not started",
        ComplianceStatus.InProgress => "In progress",
        ComplianceStatus.NeedsReview => "Needs review",
        ComplianceStatus.Compliant => "Compliant",
        ComplianceStatus.Exception => "Exception",
        _ => status.ToString(),
    };

    /// <summary>Keeps user text on one line so it cannot inject Markdown structure.</summary>
    private static string Line(string value) => value.ReplaceLineEndings(" ").Trim();

    private static string Slug(string title)
    {
        var slug = new string(title.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length == 0 ? "proposal" : slug[..Math.Min(slug.Length, 60)].Trim('-');
    }
}
