using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovBidPro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Proposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpportunityRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ResponseType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DueAtSnapshot = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DueTextSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OwnerUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmissionEvidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proposals_companies_TenantId",
                        column: x => x.TenantId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_proposals_opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "proposal_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    To = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proposal_events_proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "proposal_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proposal_reviews_proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "proposal_sections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssigneeUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proposal_sections_proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "proposal_requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceLocator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponseLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AssigneeUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Evidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_requirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proposal_requirements_proposal_sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "proposal_sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_proposal_requirements_proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_events_ProposalId",
                table: "proposal_events",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_events_TenantId_ProposalId_OccurredAt",
                table: "proposal_events",
                columns: new[] { "TenantId", "ProposalId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_requirements_ProposalId",
                table: "proposal_requirements",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_requirements_SectionId",
                table: "proposal_requirements",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_requirements_TenantId_AssigneeUserId",
                table: "proposal_requirements",
                columns: new[] { "TenantId", "AssigneeUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_requirements_TenantId_ProposalId",
                table: "proposal_requirements",
                columns: new[] { "TenantId", "ProposalId" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_reviews_ProposalId",
                table: "proposal_reviews",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_reviews_TenantId_ProposalId",
                table: "proposal_reviews",
                columns: new[] { "TenantId", "ProposalId" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_reviews_TenantId_ReviewerUserId_Outcome",
                table: "proposal_reviews",
                columns: new[] { "TenantId", "ReviewerUserId", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_sections_ProposalId",
                table: "proposal_sections",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_sections_TenantId_ProposalId_Order",
                table: "proposal_sections",
                columns: new[] { "TenantId", "ProposalId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_proposals_OpportunityId",
                table: "proposals",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_proposals_TenantId_OpportunityId",
                table: "proposals",
                columns: new[] { "TenantId", "OpportunityId" });

            migrationBuilder.CreateIndex(
                name: "IX_proposals_TenantId_Status",
                table: "proposals",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proposal_events");

            migrationBuilder.DropTable(
                name: "proposal_requirements");

            migrationBuilder.DropTable(
                name: "proposal_reviews");

            migrationBuilder.DropTable(
                name: "proposal_sections");

            migrationBuilder.DropTable(
                name: "proposals");
        }
    }
}
