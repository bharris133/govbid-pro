using GovBidPro.Application.Opportunities;
using GovBidPro.Domain.Entities;
using GovBidPro.Domain.Enums;
using GovBidPro.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovBidPro.Web.Controllers;

public sealed class OpportunitySearchQuery
{
    public string? Keywords { get; set; }

    public List<string> Naics { get; set; } = [];

    public List<string> Agency { get; set; } = [];

    public List<SourceNoticeType> NoticeType { get; set; } = [];

    public List<ResponseType> ResponseType { get; set; } = [];

    public List<string> SetAside { get; set; } = [];

    public List<string> State { get; set; } = [];

    public DateTimeOffset? PostedFrom { get; set; }

    public DateTimeOffset? PostedTo { get; set; }

    public DateTimeOffset? DueAfter { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;

    public OpportunitySearchCriteria ToCriteria() => new()
    {
        Keywords = Keywords,
        NaicsCodes = Naics,
        Agencies = Agency,
        NoticeTypes = NoticeType,
        ResponseTypes = ResponseType,
        SetAsides = SetAside,
        PlaceOfPerformanceStates = State,
        PostedFrom = PostedFrom,
        PostedTo = PostedTo,
        DueAfter = DueAfter,
    };
}

[ApiController]
[Route("api/v1/opportunities")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class OpportunitiesController(OpportunityQueryService queries) : ControllerBase
{
    [HttpGet]
    public Task<OpportunitySearchResult> Search([FromQuery] OpportunitySearchQuery query, CancellationToken cancellationToken) =>
        queries.SearchAsync(query.ToCriteria(), query.Page, query.PageSize, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<OpportunityDetailDto> Get(Guid id, CancellationToken cancellationToken) => queries.GetAsync(id, cancellationToken);
}
