namespace GovBidPro.Application.Common;

/// <summary>
/// The resource does not exist or is not visible to the caller. Cross-tenant access deliberately
/// produces this (404), so callers cannot probe for other tenants' identifiers.
/// </summary>
public sealed class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException()
        : base("Resource not found.")
    {
    }

    public ResourceNotFoundException(string message)
        : base(message)
    {
    }

    public ResourceNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A policy denied the operation. <see cref="ReasonCode"/> is safe to show to users.</summary>
public sealed class PolicyDeniedException : Exception
{
    public PolicyDeniedException()
        : this("policy.denied")
    {
    }

    public PolicyDeniedException(string reasonCode)
        : base($"Operation denied by policy ({reasonCode}).")
    {
        ReasonCode = reasonCode;
    }

    public PolicyDeniedException(string reasonCode, Exception innerException)
        : base($"Operation denied by policy ({reasonCode}).", innerException)
    {
        ReasonCode = reasonCode;
    }

    public string ReasonCode { get; } = "policy.denied";
}

/// <summary>Request validation failed. Messages must not echo uploaded content.</summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException()
        : this("validation.failed", "The request is invalid.")
    {
    }

    public RequestValidationException(string message)
        : this("validation.failed", message)
    {
    }

    public RequestValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        ReasonCode = "validation.failed";
    }

    public RequestValidationException(string reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    public string ReasonCode { get; } = "validation.failed";
}

/// <summary>The operation conflicts with current state (e.g. user already belongs to a company).</summary>
public sealed class ConflictException : Exception
{
    public ConflictException()
    {
    }

    public ConflictException(string message)
        : base(message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
