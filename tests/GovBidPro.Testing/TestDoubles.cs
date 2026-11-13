using System.Collections.Concurrent;
using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Ai;
using GovBidPro.Application.Auditing;
using GovBidPro.Application.Documents;
using GovBidPro.Domain.Enums;

namespace GovBidPro.Testing;

/// <summary>Controllable clock for retention and import tests.</summary>
public sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

public sealed class InMemoryObjectStorage : IObjectStorage
{
    public ConcurrentDictionary<string, byte[]> Objects { get; } = new();

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (!Objects.TryAdd(key, buffer.ToArray()))
        {
            throw new InvalidOperationException("Object already exists.");
        }
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new MemoryStream(Objects[key], writable: false));

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        Objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

public sealed class FixedMalwareScanner(ScanStatus result) : IMalwareScanner
{
    public int Calls { get; private set; }

    public Task<ScanStatus> ScanAsync(ReadOnlyMemory<byte> content, string fileName, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(result);
    }
}

/// <summary>Network-free AI provider (AI-AZ-06). Counts calls so tests can prove zero egress.</summary>
public sealed class FakeAiProvider : IAiProvider
{
    public int Calls { get; private set; }

    public List<AiProviderRequest> Requests { get; } = [];

    public Func<AiProviderRequest, CancellationToken, Task<AiProviderResult>> Behavior { get; set; } =
        (_, _) => Task.FromResult(new AiProviderResult("Suggested summary.", 100, 20));

    public string Name => "Fake";

    public string? Model => "fake-model";

    public async Task<AiProviderResult> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        Requests.Add(request);
        return await Behavior(request, cancellationToken);
    }
}

public sealed class FixedCorrelation : ICorrelationContext
{
    public string CorrelationId { get; } = "test-" + Guid.NewGuid().ToString("N");
}

/// <summary>Collects audit entries in memory (used where the DB-backed log is not under test).</summary>
public sealed class RecordingAuditLog : IAuditLog
{
    public List<(string Action, string? ResourceId, AuditOutcome Outcome, string Reason)> Entries { get; } = [];

    public Task RecordAsync(string action, string resourceType, string? resourceId, AuditOutcome outcome, string reasonCode, CancellationToken cancellationToken)
    {
        Entries.Add((action, resourceId, outcome, reasonCode));
        return Task.CompletedTask;
    }
}
