using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiLife.DeviceIntake.Api.Data.MySqlMigrations
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
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_bin");
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
