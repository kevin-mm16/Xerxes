using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiLife.DeviceIntake.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AccuracyMeters",
                table: "Agents",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployeeName",
                table: "Agents",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Agents",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LocationCapturedAtUtc",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Agents",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Script = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Output = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportJobs_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportJobs_AgentId_RequestedAtUtc",
                table: "SupportJobs",
                columns: new[] { "AgentId", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportJobs");

            migrationBuilder.DropColumn(
                name: "AccuracyMeters",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "EmployeeName",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "LocationCapturedAtUtc",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Agents");
        }
    }
}
