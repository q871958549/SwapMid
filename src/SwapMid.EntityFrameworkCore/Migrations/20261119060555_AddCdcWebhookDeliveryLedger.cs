using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapMid.Migrations
{
    /// <inheritdoc />
    public partial class AddCdcWebhookDeliveryLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppCdcWebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StreamId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: false),
                    AcceptedEventCount = table.Column<long>(type: "bigint", nullable: false),
                    DeliveredCount = table.Column<long>(type: "bigint", nullable: false),
                    CheckpointLsn = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorSummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PayloadSizeBytes = table.Column<int>(type: "integer", nullable: false),
                    StoredPayloadJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReplayedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReplayCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCdcWebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppCdcWebhookDeliveries_AppCdcStreams_StreamId",
                        column: x => x.StreamId,
                        principalTable: "AppCdcStreams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCdcWebhookDeliveries_StreamId_ReceivedAtUtc",
                table: "AppCdcWebhookDeliveries",
                columns: new[] { "StreamId", "ReceivedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCdcWebhookDeliveries");
        }
    }
}
