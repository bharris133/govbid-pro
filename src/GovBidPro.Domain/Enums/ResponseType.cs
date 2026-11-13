namespace GovBidPro.Domain.Enums;

/// <summary>
/// The kind of response a contractor would prepare (handoff §3.1). Interpreted from source data
/// by an explicit, versioned mapping and revisable by users; never assumed from notice type alone.
/// </summary>
public enum ResponseType
{
    /// <summary>The response type could not be determined; a user must decide.</summary>
    Undetermined = 0,

    /// <summary>Request for Information: market research, non-binding, not an award event.</summary>
    Rfi = 1,

    /// <summary>Request for Proposal: structured proposal, volumes, evaluation criteria.</summary>
    Rfp = 2,

    /// <summary>Request for Quotation: price-focused, line items and delivery terms.</summary>
    Rfq = 3,

    /// <summary>Sources Sought: capability statement and set-aside details.</summary>
    SourcesSought = 4,

    /// <summary>Informational notice for which no response is expected (e.g. award notice).</summary>
    NoResponseExpected = 5,
}
