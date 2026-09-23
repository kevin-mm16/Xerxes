using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiLife.DeviceIntake.Api.Data.MySqlMigrations
{
    /// <inheritdoc />
    public partial class InitialMySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    SerialNumber = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.SerialNumber);
                })
                .Annotation("Relational:Collation", "utf8mb4_bin");

            migrationBuilder.CreateTable(
                name: "DeviceSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CollectionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PayloadHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin"),
                    SerialNumber = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin"),
                    BranchCode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_bin"),
                    ComputerName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true, collation: "utf8mb4_bin"),
                    LoggedInUser = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_bin"),
                    Manufacturer = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_bin"),
                    Model = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_bin"),
                    ProcessorJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_bin"),
                    RamGB = table.Column<double>(type: "double", nullable: true),
                    RamJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_bin"),
                    DisksJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_bin"),
                    WindowsJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_bin"),
                    NetworkJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_bin"),
                    CollectorVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_bin"),
                    CollectedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SourceIp = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_bin"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_bin"),
                    IsRepeat = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RawJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceSubmissions_Devices_SerialNumber",
                        column: x => x.SerialNumber,
                        principalTable: "Devices",
                        principalColumn: "SerialNumber",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("Relational:Collation", "utf8mb4_bin");

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PreviousStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_bin"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_bin"),
                    Note = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: "utf8mb4_bin"),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Reviewer = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reviews_DeviceSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "DeviceSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("Relational:Collation", "utf8mb4_bin");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSubmissions_BranchCode",
                table: "DeviceSubmissions",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSubmissions_CollectionId",
                table: "DeviceSubmissions",
                column: "CollectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSubmissions_ComputerName",
                table: "DeviceSubmissions",
                column: "ComputerName");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSubmissions_SerialNumber_ReceivedAtUtc",
                table: "DeviceSubmissions",
                columns: new[] { "SerialNumber", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSubmissions_Status_ReceivedAtUtc",
                table: "DeviceSubmissions",
                columns: new[] { "Status", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_SubmissionId",
                table: "Reviews",
                column: "SubmissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropTable(
                name: "DeviceSubmissions");

            migrationBuilder.DropTable(
                name: "Devices");
        }
    }
}
