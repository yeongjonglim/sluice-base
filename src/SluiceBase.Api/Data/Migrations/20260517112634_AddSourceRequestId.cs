using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SluiceBase.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_request_id",
                table: "update_request",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_request_id",
                table: "update_request");
        }
    }
}
