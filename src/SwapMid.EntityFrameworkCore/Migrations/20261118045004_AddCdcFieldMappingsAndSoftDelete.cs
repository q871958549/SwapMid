using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapMid.Migrations
{
    /// <inheritdoc />
    public partial class AddCdcFieldMappingsAndSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SoftDeleteColumn",
                table: "AppCdcStreams",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SoftDeleteTimestampColumn",
                table: "AppCdcStreams",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetFieldMappingsJson",
                table: "AppCdcStreams",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SoftDeleteColumn",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "SoftDeleteTimestampColumn",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "TargetFieldMappingsJson",
                table: "AppCdcStreams");
        }
    }
}
