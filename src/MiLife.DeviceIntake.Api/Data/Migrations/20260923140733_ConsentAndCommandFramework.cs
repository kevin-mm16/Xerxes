using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiLife.DeviceIntake.Api.Data.Migrations
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
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "SupportJobs",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RunSilently",
                table: "SupportJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentAcceptedAtUtc",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentVersion",
                table: "Agents",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
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
