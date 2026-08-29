using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyCurrencyAllowLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                table: "SupplierInvoices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                table: "SubcontractorExpenses",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                table: "SubcontractorAccruals",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                table: "RateLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "ClientCurrencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditLimit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientCurrencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubcontractorCurrencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubcontractorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubcontractorCurrencies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ClientCurrencyIndex",
                table: "ClientCurrencies",
                columns: new[] { "ClientId", "CurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "SubcontractorCurrencyIndex",
                table: "SubcontractorCurrencies",
                columns: new[] { "SubcontractorId", "CurrencyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientCurrencies");

            migrationBuilder.DropTable(
                name: "SubcontractorCurrencies");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "SupplierInvoices");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "SubcontractorExpenses");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "SubcontractorAccruals");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "RateLines");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "Invoices");
        }
    }
}
