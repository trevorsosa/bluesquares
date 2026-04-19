using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueSquares.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringInvoicesAndAccountingIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuickBooksAccessToken",
                table: "Merchants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuickBooksConnectedAt",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "QuickBooksEnabled",
                table: "Merchants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "QuickBooksEnvironment",
                table: "Merchants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "QuickBooksLastSyncAt",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuickBooksRealmId",
                table: "Merchants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuickBooksRefreshToken",
                table: "Merchants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuickBooksTokenExpiresAt",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "XeroAccessToken",
                table: "Merchants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "XeroConnectedAt",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "XeroEnabled",
                table: "Merchants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "XeroLastSyncAt",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "XeroRefreshToken",
                table: "Merchants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "XeroTenantId",
                table: "Merchants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "XeroTokenExpiresAt",
                table: "Merchants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecurringInvoiceScheduleId",
                table: "Invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringInvoiceSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    DueDaysAfterIssue = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    AutoSendWhatsApp = table.Column<bool>(type: "boolean", nullable: false),
                    AutoSendEmail = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextRunDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRunDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastGeneratedInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringInvoiceSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringInvoiceSchedules_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringInvoiceSchedules_Invoices_LastGeneratedInvoiceId",
                        column: x => x.LastGeneratedInvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecurringInvoiceSchedules_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringInvoiceLineItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecurringInvoiceScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringInvoiceLineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringInvoiceLineItems_RecurringInvoiceSchedules_Recurri~",
                        column: x => x.RecurringInvoiceScheduleId,
                        principalTable: "RecurringInvoiceSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_RecurringInvoiceScheduleId",
                table: "Invoices",
                column: "RecurringInvoiceScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoiceLineItems_RecurringInvoiceScheduleId",
                table: "RecurringInvoiceLineItems",
                column: "RecurringInvoiceScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoiceSchedules_ClientId",
                table: "RecurringInvoiceSchedules",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoiceSchedules_LastGeneratedInvoiceId",
                table: "RecurringInvoiceSchedules",
                column: "LastGeneratedInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoiceSchedules_MerchantId_IsActive_NextRunDate",
                table: "RecurringInvoiceSchedules",
                columns: new[] { "MerchantId", "IsActive", "NextRunDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_RecurringInvoiceSchedules_RecurringInvoiceSchedule~",
                table: "Invoices",
                column: "RecurringInvoiceScheduleId",
                principalTable: "RecurringInvoiceSchedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_RecurringInvoiceSchedules_RecurringInvoiceSchedule~",
                table: "Invoices");

            migrationBuilder.DropTable(
                name: "RecurringInvoiceLineItems");

            migrationBuilder.DropTable(
                name: "RecurringInvoiceSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_RecurringInvoiceScheduleId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "QuickBooksAccessToken",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "QuickBooksConnectedAt",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "QuickBooksEnabled",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "QuickBooksEnvironment",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "QuickBooksLastSyncAt",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "QuickBooksRealmId",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "QuickBooksRefreshToken",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "QuickBooksTokenExpiresAt",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "XeroAccessToken",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "XeroConnectedAt",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "XeroEnabled",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "XeroLastSyncAt",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "XeroRefreshToken",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "XeroTenantId",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "XeroTokenExpiresAt",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "RecurringInvoiceScheduleId",
                table: "Invoices");
        }
    }
}
