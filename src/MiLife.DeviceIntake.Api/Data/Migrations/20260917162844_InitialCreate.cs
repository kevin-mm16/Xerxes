using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiLife.DeviceIntake.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.SerialNumber);
                });

            migrationBuilder.CreateTable(
                name: "DeviceSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CollectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BranchCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ComputerName = table.Column<string>(type: "TEXT", nullable: true),
                    LoggedInUser = table.Column<string>(type: "TEXT", nullable: true),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    ProcessorJson = table.Column<string>(type: "TEXT", nullable: true),
                    RamGB = table.Column<double>(type: "REAL", nullable: true),
                    RamJson = table.Column<string>(type: "TEXT", nullable: true),
                    DisksJson = table.Column<string>(type: "TEXT", nullable: true),
                    WindowsJson = table.Column<string>(type: "TEXT", nullable: true),
                    NetworkJson = table.Column<string>(type: "TEXT", nullable: true),
                    CollectorVersion = table.Column<string>(type: "TEXT", nullable: false),
                    CollectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceIp = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    IsRepeat = table.Column<bool>(type: "INTEGER", nullable: false),
                    RawJson = table.Column<string>(type: "TEXT", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreviousStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reviewer = table.Column<string>(type: "TEXT", nullable: false)
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
                });

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
