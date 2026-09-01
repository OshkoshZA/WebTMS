using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubcontractorPortalContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubcontractorId",
                table: "AspNetUsers",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubcontractorId",
                table: "AspNetUsers");
        }
    }
}
