using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapMid.Migrations
{
    /// <inheritdoc />
    public partial class AddCdcSourceTypeAndLakehouseCompaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LakehouseCompactMinFiles",
                table: "AppCdcStreams",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceType",
                table: "AppCdcStreams",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LakehouseCompactMinFiles",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "AppCdcStreams");
        }
    }
}
