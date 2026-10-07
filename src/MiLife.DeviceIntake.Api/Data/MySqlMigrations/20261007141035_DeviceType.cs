using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiLife.DeviceIntake.Api.Data.MySqlMigrations
{
    /// <inheritdoc />
    public partial class DeviceType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceType",
                table: "DeviceSubmissions",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unknown",
                collation: "utf8mb4_bin");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSubmissions_DeviceType",
                table: "DeviceSubmissions",
                column: "DeviceType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceSubmissions_DeviceType",
                table: "DeviceSubmissions");

            migrationBuilder.DropColumn(
                name: "DeviceType",
                table: "DeviceSubmissions");
        }
    }
}
