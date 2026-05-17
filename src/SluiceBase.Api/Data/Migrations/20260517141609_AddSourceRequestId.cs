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

            migrationBuilder.CreateIndex(
                name: "ix_update_request_source_request_id",
                table: "update_request",
                column: "source_request_id");

            migrationBuilder.AddForeignKey(
                name: "fk_update_request_update_request_source_request_id",
                table: "update_request",
                column: "source_request_id",
                principalTable: "update_request",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_update_request_update_request_source_request_id",
                table: "update_request");

            migrationBuilder.DropIndex(
                name: "ix_update_request_source_request_id",
                table: "update_request");

            migrationBuilder.DropColumn(
                name: "source_request_id",
                table: "update_request");
        }
    }
}
