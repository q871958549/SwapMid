using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapMid.Migrations
{
    /// <inheritdoc />
    public partial class AddCdcIcebergWapAndMaintenanceSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LakehouseDeleteOrphanedFiles",
                table: "AppCdcStreams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LakehouseExpireOlderThanMinutes",
                table: "AppCdcStreams",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LakehouseExpireRetainLast",
                table: "AppCdcStreams",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LakehouseLastMaintenanceAt",
                table: "AppCdcStreams",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LakehouseLastMaintenanceSummary",
                table: "AppCdcStreams",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LakehouseMaintenanceCron",
                table: "AppCdcStreams",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LakehouseWriteBranch",
                table: "AppCdcStreams",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LakehouseDeleteOrphanedFiles",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "LakehouseExpireOlderThanMinutes",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "LakehouseExpireRetainLast",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "LakehouseLastMaintenanceAt",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "LakehouseLastMaintenanceSummary",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "LakehouseMaintenanceCron",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "LakehouseWriteBranch",
                table: "AppCdcStreams");
        }
    }
}
