using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiLife.DeviceIntake.Api.Data.Migrations
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
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unknown");

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
