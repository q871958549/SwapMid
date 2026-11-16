using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapMid.Migrations
{
    /// <inheritdoc />
    public partial class AddCdcInitialSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InitialSnapshotEnabled",
                table: "AppCdcStreams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SnapshotCompletedAt",
                table: "AppCdcStreams",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SnapshotRowCount",
                table: "AppCdcStreams",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotStatus",
                table: "AppCdcStreams",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitialSnapshotEnabled",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "SnapshotCompletedAt",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "SnapshotRowCount",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "SnapshotStatus",
                table: "AppCdcStreams");
        }
    }
}
