using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookRetryBackoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "WebhookDeliveries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAtUtc",
                table: "WebhookDeliveries",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                table: "WebhookDeliveries");
        }
    }
}
