using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SluiceBase.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSensitiveColumnsToQueryLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "sensitive_columns",
                table: "query_log",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sensitive_columns",
                table: "query_log");
        }
    }
}
