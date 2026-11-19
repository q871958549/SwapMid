using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapMid.Migrations
{
    /// <inheritdoc />
    public partial class AddCdcDataContractSla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContractLastEvaluatedAt",
                table: "AppCdcStreams",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ContractViolated",
                table: "AppCdcStreams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ContractViolationSummary",
                table: "AppCdcStreams",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxAllowedWalLagBytes",
                table: "AppCdcStreams",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxFreshnessLagSeconds",
                table: "AppCdcStreams",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequiredColumns",
                table: "AppCdcStreams",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContractLastEvaluatedAt",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "ContractViolated",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "ContractViolationSummary",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "MaxAllowedWalLagBytes",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "MaxFreshnessLagSeconds",
                table: "AppCdcStreams");

            migrationBuilder.DropColumn(
                name: "RequiredColumns",
                table: "AppCdcStreams");
        }
    }
}
