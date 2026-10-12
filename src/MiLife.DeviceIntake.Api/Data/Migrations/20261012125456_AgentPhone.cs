using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiLife.DeviceIntake.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgentPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Agents",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Agents");
        }
    }
}
