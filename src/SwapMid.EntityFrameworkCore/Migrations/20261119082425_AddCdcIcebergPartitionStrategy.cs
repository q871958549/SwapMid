using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapMid.Migrations
{
    /// <inheritdoc />
    public partial class AddCdcIcebergPartitionStrategy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LakehousePartitionStrategy",
                table: "AppCdcStreams",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LakehousePartitionStrategy",
                table: "AppCdcStreams");
        }
    }
}
