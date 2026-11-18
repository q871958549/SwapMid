using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapMid.Migrations
{
    /// <inheritdoc />
    public partial class AddCdcMySqlBinlogAndDeltaLake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CheckpointLsn",
                table: "AppCdcStreams",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LakehouseTableFormat",
                table: "AppCdcStreams",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LakehouseTableFormat",
                table: "AppCdcStreams");

            migrationBuilder.AlterColumn<string>(
                name: "CheckpointLsn",
                table: "AppCdcStreams",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }
    }
}
