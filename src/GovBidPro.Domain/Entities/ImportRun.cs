using GovBidPro.Domain.Enums;

namespace GovBidPro.Domain.Entities;

/// <summary>One execution of an opportunity import, for import history and operator visibility.</summary>
public sealed class ImportRun
{
    private ImportRun()
    {
        SourceKey = string.Empty;
    }

    public ImportRun(Guid id, string sourceKey, DateTimeOffset startedAt, string? startingCursor)
    {
        Id = id;
        SourceKey = sourceKey;
        StartedAt = startedAt;
        StartingCursor = startingCursor;
        Status = ImportRunStatus.Running;
    }

    public Guid Id { get; private set; }

    public string SourceKey { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public ImportRunStatus Status { get; private set; }

    public string? StartingCursor { get; private set; }

    public int PagesProcessed { get; private set; }

    public int Created { get; private set; }

    public int Amended { get; private set; }

    public int Unchanged { get; private set; }

    public int Rejected { get; private set; }

    public string? ErrorCode { get; private set; }

    public void RecordPage(int created, int amended, int unchanged, int rejected)
    {
        PagesProcessed++;
        Created += created;
        Amended += amended;
        Unchanged += unchanged;
        Rejected += rejected;
    }

    public void Complete(ImportRunStatus status, DateTimeOffset at, string? errorCode = null)
    {
        Status = status;
        CompletedAt = at;
        ErrorCode = errorCode;
    }
}
