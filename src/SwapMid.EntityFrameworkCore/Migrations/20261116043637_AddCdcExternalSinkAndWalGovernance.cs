using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapMid.Migrations
{
    /// <inheritdoc />
    public partial class AddCdcExternalSinkAndWalGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetSinkDestination",
                table: "AppCdcStreams",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetSinkId",
                table: "AppCdcStreams",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCdcStreams_TargetSinkId",
                table: "AppCdcStreams",
                column: "TargetSinkId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppCdcStreams_AppDataSources_TargetSinkId",
                table: "AppCdcStreams",
                column: "TargetSinkId",
                principalTable: "AppDataSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppCdcStreams_AppDataSources_TargetSinkId",
                table: "AppCdcStreams");

            migrationBuilder.DropIndex(
                name: "IX_AppCdcStreams_TargetSinkId",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "TargetSinkDestination",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "TargetSinkId",
                table: "AppCdcStreams");
        }
    }
}
