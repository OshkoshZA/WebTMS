using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyGuardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "SubcontractorExpenseAccrualIndex",
                table: "SubcontractorExpenses",
                column: "AccrualId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "SubcontractorAccrualRateLineIndex",
                table: "SubcontractorAccruals",
                column: "RateLineBuyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "LoadConfirmationLegIndex",
                table: "LoadConfirmations",
                column: "LoadLegId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "InvoiceLineRateLineSellIndex",
                table: "InvoiceLines",
                column: "RateLineSellId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "FinancialPeriodOneOpenPerCompanyIndex",
                table: "FinancialPeriods",
                column: "CompanyId",
                unique: true,
                filter: "[Status] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "SubcontractorExpenseAccrualIndex",
                table: "SubcontractorExpenses");

            migrationBuilder.DropIndex(
                name: "SubcontractorAccrualRateLineIndex",
                table: "SubcontractorAccruals");

            migrationBuilder.DropIndex(
                name: "LoadConfirmationLegIndex",
                table: "LoadConfirmations");

            migrationBuilder.DropIndex(
                name: "InvoiceLineRateLineSellIndex",
                table: "InvoiceLines");

            migrationBuilder.DropIndex(
                name: "FinancialPeriodOneOpenPerCompanyIndex",
                table: "FinancialPeriods");
        }
    }
}
