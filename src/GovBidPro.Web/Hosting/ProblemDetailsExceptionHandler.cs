using GovBidPro.Application.Ai;
using GovBidPro.Application.Common;
using GovBidPro.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GovBidPro.Web.Hosting;

/// <summary>Maps application exceptions to RFC 9457 problem details without leaking internals.</summary>
internal sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problemDetails, ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, reason) = exception switch
        {
            ResourceNotFoundException => (StatusCodes.Status404NotFound, "Not found", "resource.not_found"),
            PolicyDeniedException denied => (StatusCodes.Status403Forbidden, "Forbidden", denied.ReasonCode),
            RequestValidationException invalid => (StatusCodes.Status400BadRequest, invalid.Message, invalid.ReasonCode),
            ConflictException conflict => (StatusCodes.Status409Conflict, conflict.Message, "conflict"),
            DomainException domain => (StatusCodes.Status400BadRequest, domain.Message, "domain.invalid"),
            AiProviderUnavailableException => (StatusCodes.Status503ServiceUnavailable, "AI provider unavailable", "ai.provider_unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", "server.error"),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            // Log the server-defined endpoint rather than the raw request path, so client input never reaches the log line.
            var endpoint = httpContext.GetEndpoint()?.DisplayName ?? "(no endpoint)";
            logger.LogError(exception, "Unhandled exception in {Endpoint} (trace {TraceId})", endpoint, httpContext.TraceIdentifier);
        }

        httpContext.Response.StatusCode = status;
        var details = new ProblemDetails { Status = status, Title = title };
        details.Extensions["reasonCode"] = reason;
        details.Extensions["traceId"] = httpContext.TraceIdentifier;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = details, Exception = exception });
    }
}
