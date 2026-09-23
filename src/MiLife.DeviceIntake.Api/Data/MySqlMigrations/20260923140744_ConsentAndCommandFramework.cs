using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiLife.DeviceIntake.Api.Data.MySqlMigrations
{
    /// <inheritdoc />
    public partial class ConsentAndCommandFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommandType",
                table: "SupportJobs",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                collation: "utf8mb4_bin");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "SupportJobs",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                collation: "utf8mb4_bin");

            migrationBuilder.AddColumn<bool>(
                name: "RunSilently",
                table: "SupportJobs",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentAcceptedAtUtc",
                table: "Agents",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentVersion",
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
                name: "CommandType",
                table: "SupportJobs");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "SupportJobs");

            migrationBuilder.DropColumn(
                name: "RunSilently",
                table: "SupportJobs");

            migrationBuilder.DropColumn(
                name: "ConsentAcceptedAtUtc",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ConsentVersion",
                table: "Agents");
        }
    }
}
