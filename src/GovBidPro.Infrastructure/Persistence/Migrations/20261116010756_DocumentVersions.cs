using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovBidPro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTemplate",
                table: "stored_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ProposalId",
                table: "stored_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "stored_documents",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateTable(
                name: "document_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ScanStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_versions_stored_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "stored_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Existing documents: UpdatedAt starts at CreatedAt and each gets its current object as version 1..n.
            migrationBuilder.Sql("""UPDATE stored_documents SET "UpdatedAt" = "CreatedAt";""");
            migrationBuilder.Sql("""
                INSERT INTO document_versions ("Id", "TenantId", "DocumentId", "Number", "FileName", "ContentType", "SizeBytes", "Sha256", "StorageKey", "ScanStatus", "UploadedByUserId", "UploadedAt")
                SELECT gen_random_uuid(), "TenantId", "Id", "ObjectVersion", "FileName", "ContentType", "SizeBytes", "Sha256", "StorageKey", "ScanStatus", "DeclaredByUserId", "CreatedAt"
                FROM stored_documents;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_stored_documents_ProposalId",
                table: "stored_documents",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_stored_documents_TenantId_IsTemplate",
                table: "stored_documents",
                columns: new[] { "TenantId", "IsTemplate" });

            migrationBuilder.CreateIndex(
                name: "IX_stored_documents_TenantId_ProposalId",
                table: "stored_documents",
                columns: new[] { "TenantId", "ProposalId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_DocumentId_Number",
                table: "document_versions",
                columns: new[] { "DocumentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_ScanStatus_UploadedAt",
                table: "document_versions",
                columns: new[] { "ScanStatus", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_StorageKey",
                table: "document_versions",
                column: "StorageKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_stored_documents_proposals_ProposalId",
                table: "stored_documents",
                column: "ProposalId",
                principalTable: "proposals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stored_documents_proposals_ProposalId",
                table: "stored_documents");

            migrationBuilder.DropTable(
                name: "document_versions");

            migrationBuilder.DropIndex(
                name: "IX_stored_documents_ProposalId",
                table: "stored_documents");

            migrationBuilder.DropIndex(
                name: "IX_stored_documents_TenantId_IsTemplate",
                table: "stored_documents");

            migrationBuilder.DropIndex(
                name: "IX_stored_documents_TenantId_ProposalId",
                table: "stored_documents");

            migrationBuilder.DropColumn(
                name: "IsTemplate",
                table: "stored_documents");

            migrationBuilder.DropColumn(
                name: "ProposalId",
                table: "stored_documents");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "stored_documents");
        }
    }
}
