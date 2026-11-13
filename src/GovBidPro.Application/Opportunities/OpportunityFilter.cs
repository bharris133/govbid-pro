using System.Linq.Expressions;
using GovBidPro.Domain.Entities;

namespace GovBidPro.Application.Opportunities;

// ToLower().Contains(...) is used deliberately: it translates to SQL via EF Core. Full-text search is a P1 item.
#pragma warning disable CA1862

/// <summary>Translates <see cref="OpportunitySearchCriteria"/> into a provider-translatable query.</summary>
public static class OpportunityFilter
{
    public static IQueryable<Opportunity> Apply(IQueryable<Opportunity> query, OpportunitySearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(criteria);

        if (!string.IsNullOrWhiteSpace(criteria.Keywords))
        {
            foreach (var term in criteria.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8))
            {
                var lowered = term.ToLowerInvariant();
                query = query.Where(o =>
                    o.Title.ToLower().Contains(lowered) ||
                    (o.Description != null && o.Description.ToLower().Contains(lowered)) ||
                    (o.SolicitationNumber != null && o.SolicitationNumber.ToLower().Contains(lowered)));
            }
        }

        var naics = Clean(criteria.NaicsCodes);
        if (naics.Count > 0)
        {
            query = query.Where(o => o.NaicsCode != null && naics.Contains(o.NaicsCode));
        }

        var agencies = Clean(criteria.Agencies).Select(a => a.ToLowerInvariant()).ToList();
        if (agencies.Count > 0)
        {
            query = query.Where(AnyAgencyContains(agencies));
        }

        if (criteria.NoticeTypes.Count > 0)
        {
            var types = criteria.NoticeTypes.Distinct().ToList();
            query = query.Where(o => types.Contains(o.NoticeType));
        }

        if (criteria.ResponseTypes.Count > 0)
        {
            var types = criteria.ResponseTypes.Distinct().ToList();
            query = query.Where(o => types.Contains(o.ResponseType));
        }

        var setAsides = Clean(criteria.SetAsides);
        if (setAsides.Count > 0)
        {
            query = query.Where(o => o.SetAside != null && setAsides.Contains(o.SetAside));
        }

        var states = Clean(criteria.PlaceOfPerformanceStates).Select(s => s.ToUpperInvariant()).ToList();
        if (states.Count > 0)
        {
            query = query.Where(o => o.PlaceOfPerformanceState != null && states.Contains(o.PlaceOfPerformanceState));
        }

        if (criteria.PostedFrom is { } from)
        {
            query = query.Where(o => o.PostedAt >= from);
        }

        if (criteria.PostedTo is { } to)
        {
            query = query.Where(o => o.PostedAt <= to);
        }

        if (criteria.DueAfter is { } dueAfter)
        {
            query = query.Where(o => o.Deadline.DueAt >= dueAfter);
        }

        return query;
    }

    private static List<string> Clean(IEnumerable<string>? values) =>
        values?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToList() ?? [];

    private static Expression<Func<Opportunity, bool>> AnyAgencyContains(IReadOnlyList<string> loweredAgencies)
    {
        var parameter = Expression.Parameter(typeof(Opportunity), "o");
        var agency = Expression.Property(parameter, nameof(Opportunity.Agency));
        var notNull = Expression.NotEqual(agency, Expression.Constant(null, typeof(string)));
        var toLower = Expression.Call(agency, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        var contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

        Expression? body = null;
        foreach (var value in loweredAgencies)
        {
            Expression<Func<string>> captured = () => value;
            var match = Expression.AndAlso(notNull, Expression.Call(toLower, contains, captured.Body));
            body = body is null ? match : Expression.OrElse(body, match);
        }

        return Expression.Lambda<Func<Opportunity, bool>>(body ?? Expression.Constant(true), parameter);
    }
}
#pragma warning restore CA1862
