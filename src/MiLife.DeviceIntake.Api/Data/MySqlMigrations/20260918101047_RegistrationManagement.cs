using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiLife.DeviceIntake.Api.Data.MySqlMigrations
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
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployeeName",
                table: "Agents",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true,
                collation: "utf8mb4_bin");

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Agents",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LocationCapturedAtUtc",
                table: "Agents",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Agents",
                type: "double",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AgentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Script = table.Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: false, collation: "utf8mb4_bin"),
                    RequestedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_bin"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_bin"),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Output = table.Column<string>(type: "longtext", maxLength: 16000, nullable: true, collation: "utf8mb4_bin"),
                    ExitCode = table.Column<int>(type: "int", nullable: true)
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
                })
                .Annotation("Relational:Collation", "utf8mb4_bin");

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
