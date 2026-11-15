# SAM.gov sync

GovBid Pro can import public notices from the official SAM.gov **Get Opportunities Public API v2**
(`https://api.sam.gov/opportunities/v2/search`, documented at <https://open.gsa.gov/api/get-opportunities-public-api/>).
It is **off by default**. The fixture source stays the default until an operator turns the live sync on.

The adapter was written from the official documentation on 2026-11-15 and is covered by tests against a fake HTTP
server. It has **not been run against the live service**, because no API key has been issued yet. The product says so
(`/api/v1/me` reports `samGovLiveIntegrationVerified: false`) until the checklist below is completed.

## What it does

- Uses only the documented API with your own key. No scraping.
- Each run asks for notices posted in the last `LookbackDays` days, one page per request, at most `MaxPagesPerRun`
  requests. An interrupted run resumes from its saved cursor.
- Imports are idempotent. Changed notices become new revisions; nothing is overwritten silently.
- Records are stored as public data with `sourceKey = "sam.gov"` and `isLiveSource = true`.
- A response deadline becomes an instant only when SAM.gov states a UTC offset. Otherwise only the original text is shown.
- Notice descriptions are not downloaded (each one costs an extra keyed request). Users follow the source link.
- HTTP 429 stops the run and records when to retry. Other failures back off exponentially.
- The API key is sent as the documented `api_key` query parameter. Request logging is removed for this HTTP client and
  error messages never contain the request URL.

## Settings

| Setting | Default | Meaning |
|---|---|---|
| `OpportunitySources:SamGov:Enabled` | `false` | Registers the SAM.gov source. |
| `OpportunitySources:SamGov:ApiKey` | none | Secret. Supply as the environment variable `OpportunitySources__SamGov__ApiKey` from your secret store. |
| `OpportunitySources:SamGov:PageSize` | `100` | Records per request (API maximum 1000). |
| `OpportunitySources:SamGov:LookbackDays` | `2` | Size of the posted-date window. Raise temporarily for a backfill. |
| `OpportunitySources:SamGov:OffsetMode` | `PageIndex` | The documentation calls `offset` a page index. Switch to `RecordOffset` if verification shows otherwise. |
| `OpportunitySources:Sync:Enabled` | `false` | Runs the scheduled sync. |
| `OpportunitySources:Sync:IntervalMinutes` | `360` | Minutes between runs. |
| `OpportunitySources:Sync:MaxPagesPerRun` | `5` | Request budget per source per run. |

SAM.gov limits requests per day by account type, and the documentation does not publish the numbers. Start with the
defaults (about 20 requests a day) and check your account's limit before raising them.

## Verification checklist (before calling the integration verified)

1. Request a public API key on the SAM.gov account details page and confirm the terms allow this use.
2. Set the key and `Enabled=true` in a non-production environment. Leave the scheduled sync off.
3. As a platform administrator, call `POST /api/v1/admin/imports/sync`, then `GET /api/v1/admin/imports/sources`.
4. Compare ten imported notices with sam.gov: title, notice type, set-aside, NAICS, deadline and link.
5. Run it again with a window that holds more than `PageSize` notices and confirm the totals match, which confirms `OffsetMode`.
6. Record the result in `docs/production-gates.md` and change `SamGovIntegrationStatus.LiveIntegrationVerified`.
