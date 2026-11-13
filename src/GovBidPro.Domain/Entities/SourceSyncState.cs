namespace GovBidPro.Domain.Entities;

/// <summary>Checkpoint and health for one opportunity source (handoff §6.2).</summary>
public sealed class SourceSyncState
{
    private SourceSyncState()
    {
        SourceKey = string.Empty;
    }

    public SourceSyncState(string sourceKey, bool isLiveSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        SourceKey = sourceKey;
        IsLiveSource = isLiveSource;
    }

    public string SourceKey { get; private set; }

    public bool IsLiveSource { get; private set; }

    /// <summary>Opaque cursor of the last fully persisted page; resuming starts after it.</summary>
    public string? Cursor { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    public DateTimeOffset? LastSuccessfulSyncAt { get; private set; }

    public string? LastErrorCode { get; private set; }

    public string? LastErrorMessage { get; private set; }

    public long? LastLatencyMs { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    public DateTimeOffset? RetryNotBefore { get; private set; }

    public void RecordAttempt(DateTimeOffset at) => LastAttemptAt = at;

    public void Checkpoint(string? cursor, long latencyMs)
    {
        Cursor = cursor;
        LastLatencyMs = latencyMs;
    }

    public void RecordSuccess(DateTimeOffset at)
    {
        LastSuccessfulSyncAt = at;
        ConsecutiveFailures = 0;
        LastErrorCode = null;
        LastErrorMessage = null;
        RetryNotBefore = null;
    }

    public void RecordFailure(string errorCode, string safeMessage, DateTimeOffset? retryNotBefore)
    {
        ConsecutiveFailures++;
        LastErrorCode = errorCode;
        LastErrorMessage = safeMessage.Length > 500 ? safeMessage[..500] : safeMessage;
        RetryNotBefore = retryNotBefore;
    }
}
