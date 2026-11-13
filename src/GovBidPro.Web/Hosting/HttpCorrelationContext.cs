using System.Diagnostics;
using GovBidPro.Application.Abstractions;

namespace GovBidPro.Web.Hosting;

/// <summary>Correlation id from the current trace (W3C trace id) or the request identifier.</summary>
internal sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    private readonly string _fallback = Guid.NewGuid().ToString("N");

    public string CorrelationId =>
        Activity.Current?.TraceId.ToString() ?? accessor.HttpContext?.TraceIdentifier ?? _fallback;
}
