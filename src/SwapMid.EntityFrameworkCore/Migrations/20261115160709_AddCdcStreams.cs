using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapMid.Migrations
{
    /// <inheritdoc />
    public partial class AddCdcStreams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppCdcStreams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationName = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    SlotName = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    MonitoredTables = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AutoCreatePublicationAndSlot = table.Column<bool>(type: "boolean", nullable: false),
                    PollIntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxBatchSize = table.Column<int>(type: "integer", nullable: false),
                    TargetMode = table.Column<int>(type: "integer", nullable: false),
                    TargetTopicName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TargetPipelineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CheckpointLsn = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LastCapturedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastCapturedCount = table.Column<long>(type: "bigint", nullable: false),
                    TotalCapturedCount = table.Column<long>(type: "bigint", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCdcStreams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppCdcStreams_AppDataPipelines_TargetPipelineId",
                        column: x => x.TargetPipelineId,
                        principalTable: "AppDataPipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AppCdcStreams_AppDataSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "AppDataSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCdcStreams_Name",
                table: "AppCdcStreams",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCdcStreams_SourceId",
                table: "AppCdcStreams",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCdcStreams_Status",
                table: "AppCdcStreams",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AppCdcStreams_TargetPipelineId",
                table: "AppCdcStreams",
                column: "TargetPipelineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCdcStreams");
        }
    }
}
