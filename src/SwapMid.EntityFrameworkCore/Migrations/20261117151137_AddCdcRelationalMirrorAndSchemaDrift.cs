using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapMid.Migrations
{
    /// <inheritdoc />
    public partial class AddCdcRelationalMirrorAndSchemaDrift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SchemaBaselineHash",
                table: "AppCdcStreams",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchemaBaselineJson",
                table: "AppCdcStreams",
                type: "character varying(32768)",
                maxLength: 32768,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SchemaDriftDetected",
                table: "AppCdcStreams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SchemaDriftDetectedAt",
                table: "AppCdcStreams",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchemaDriftPolicy",
                table: "AppCdcStreams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SchemaDriftSummary",
                table: "AppCdcStreams",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetDeletePolicy",
                table: "AppCdcStreams",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SchemaBaselineHash",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "SchemaBaselineJson",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "SchemaDriftDetected",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "SchemaDriftDetectedAt",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "SchemaDriftPolicy",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "SchemaDriftSummary",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "TargetDeletePolicy",
                table: "AppCdcStreams");
        }
    }
}
