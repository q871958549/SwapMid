using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapMid.Migrations
{
    /// <inheritdoc />
    public partial class AddCdcLakehouseCatalogSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CatalogLastSyncStatus",
                table: "AppCdcStreams",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CatalogLastSyncedAt",
                table: "AppCdcStreams",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CatalogLastSyncedVersion",
                table: "AppCdcStreams",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CatalogNamespace",
                table: "AppCdcStreams",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CatalogSinkId",
                table: "AppCdcStreams",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CatalogTableName",
                table: "AppCdcStreams",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CatalogType",
                table: "AppCdcStreams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AppCdcStreams_CatalogSinkId",
                table: "AppCdcStreams",
                column: "CatalogSinkId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppCdcStreams_AppDataSources_CatalogSinkId",
                table: "AppCdcStreams",
                column: "CatalogSinkId",
                principalTable: "AppDataSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppCdcStreams_AppDataSources_CatalogSinkId",
                table: "AppCdcStreams");

            migrationBuilder.DropIndex(
                name: "IX_AppCdcStreams_CatalogSinkId",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "CatalogLastSyncStatus",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "CatalogLastSyncedAt",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "CatalogLastSyncedVersion",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "CatalogNamespace",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "CatalogSinkId",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "CatalogTableName",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "CatalogType",
                table: "AppCdcStreams");
        }
    }
}
