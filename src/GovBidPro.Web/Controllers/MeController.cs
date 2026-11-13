using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Ai;
using GovBidPro.Infrastructure.SamGov;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GovBidPro.Web.Controllers;

public sealed record MeDto(string UserId, Guid? TenantId, string? CompanyName, string? Role, bool AiPlatformEnabled, bool AiEnabledForTenant, bool SamGovLiveIntegrationVerified, string DataSourceNotice);

[ApiController]
[Route("api/v1/me")]
[Authorize]
public sealed class MeController(ITenantContext tenant, IAppDbContext db, IOptions<AiOptions> ai) : ControllerBase
{
    [HttpGet]
    public async Task<MeDto> Get(CancellationToken cancellationToken)
    {
        string? companyName = null;
        var aiTenant = false;
        if (tenant.HasTenant)
        {
            var tenantId = tenant.TenantId;
            var company = await db.Companies.AsNoTracking().Where(c => c.Id == tenantId).Select(c => new { c.DisplayName, c.AiEnabled }).SingleAsync(cancellationToken);
            companyName = company.DisplayName;
            aiTenant = company.AiEnabled;
        }

        return new MeDto(tenant.UserId ?? string.Empty, tenant.HasTenant ? tenant.TenantId : null, companyName, tenant.Role?.ToString(), ai.Value.PlatformEnabled, aiTenant, SamGovIntegrationStatus.LiveIntegrationVerified, SamGovIntegrationStatus.Note);
    }
}
