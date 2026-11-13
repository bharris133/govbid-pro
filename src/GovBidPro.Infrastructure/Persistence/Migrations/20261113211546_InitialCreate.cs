using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GovBidPro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ai_request_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Feature = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PromptTemplateVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InputResourceIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    HighestInputClassification = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    ResponseStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_request_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AiEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AiConsentedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    AiConsentedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "import_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartingCursor = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PagesProcessed = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<int>(type: "integer", nullable: false),
                    Amended = table.Column<int>(type: "integer", nullable: false),
                    Unchanged = table.Column<int>(type: "integer", nullable: false),
                    Rejected = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "opportunities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsLiveSource = table.Column<bool>(type: "boolean", nullable: false),
                    SolicitationNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Agency = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Office = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    NoticeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceNoticeTypeRaw = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResponseType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResponseTypeBasis = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResponseTypeRuleVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    NaicsCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PscCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SetAside = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PlaceOfPerformanceState = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PlaceOfPerformanceCountry = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    response_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    response_due_original_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    response_due_time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubmissionInstructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FirstIngestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRetrievedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "retention_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: true),
                    ApprovalStatus = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retention_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "source_sync_states",
                columns: table => new
                {
                    SourceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsLiveSource = table.Column<bool>(type: "boolean", nullable: false),
                    Cursor = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessfulSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastLatencyMs = table.Column<long>(type: "bigint", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    RetryNotBefore = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_sync_states", x => x.SourceKey);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_company_memberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_memberships_companies_TenantId",
                        column: x => x.TenantId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saved_searches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Criteria = table.Column<string>(type: "jsonb", nullable: false),
                    NotificationFrequency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastEvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_searches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_saved_searches_companies_TenantId",
                        column: x => x.TenantId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "opportunity_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ChangeKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunity_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_opportunity_revisions_opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retention_actions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Classification = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecoverableUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LegalHold = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retention_actions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_retention_actions_retention_policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "retention_policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stored_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Classification = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    DeclaredByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    DeclaredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RetentionPolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectVersion = table.Column<int>(type: "integer", nullable: false),
                    ScanStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecoverableUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PurgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LegalHold = table.Column<bool>(type: "boolean", nullable: false),
                    QuarantineReason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stored_documents_companies_TenantId",
                        column: x => x.TenantId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stored_documents_retention_policies_RetentionPolicyId",
                        column: x => x.RetentionPolicyId,
                        principalTable: "retention_policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_request_records_TenantId_OccurredAt",
                table: "ai_request_records",
                columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_OccurredAt",
                table: "audit_events",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_TenantId_OccurredAt",
                table: "audit_events",
                columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_company_memberships_TenantId_UserId",
                table: "company_memberships",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_memberships_UserId",
                table: "company_memberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_import_runs_SourceKey_StartedAt",
                table: "import_runs",
                columns: new[] { "SourceKey", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_NaicsCode",
                table: "opportunities",
                column: "NaicsCode");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_NoticeType",
                table: "opportunities",
                column: "NoticeType");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_PostedAt",
                table: "opportunities",
                column: "PostedAt");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_SourceKey_ExternalId",
                table: "opportunities",
                columns: new[] { "SourceKey", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_revisions_OpportunityId_RevisionNumber",
                table: "opportunity_revisions",
                columns: new[] { "OpportunityId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_retention_actions_PolicyId",
                table: "retention_actions",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_retention_actions_TenantId_ResourceId",
                table: "retention_actions",
                columns: new[] { "TenantId", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_retention_policies_PolicyKey_Version",
                table: "retention_policies",
                columns: new[] { "PolicyKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_saved_searches_TenantId",
                table: "saved_searches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_stored_documents_RetentionPolicyId",
                table: "stored_documents",
                column: "RetentionPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_stored_documents_State_RecoverableUntil",
                table: "stored_documents",
                columns: new[] { "State", "RecoverableUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_stored_documents_StorageKey",
                table: "stored_documents",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stored_documents_TenantId_State",
                table: "stored_documents",
                columns: new[] { "TenantId", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "ai_request_records");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "company_memberships");

            migrationBuilder.DropTable(
                name: "import_runs");

            migrationBuilder.DropTable(
                name: "opportunity_revisions");

            migrationBuilder.DropTable(
                name: "retention_actions");

            migrationBuilder.DropTable(
                name: "saved_searches");

            migrationBuilder.DropTable(
                name: "source_sync_states");

            migrationBuilder.DropTable(
                name: "stored_documents");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "opportunities");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "retention_policies");
        }
    }
}
