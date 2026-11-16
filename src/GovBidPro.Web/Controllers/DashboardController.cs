using GovBidPro.Application.Dashboard;
using GovBidPro.Web.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovBidPro.Web.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet]
    public Task<DashboardDto> Get(CancellationToken cancellationToken) => dashboard.GetAsync(cancellationToken);
}
