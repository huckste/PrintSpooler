using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintSpooler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RawData",
                table: "Jobs");

            migrationBuilder.CreateTable(
                name: "JobData",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Bytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobData", x => x.JobId);
                    table.ForeignKey(
                        name: "FK_JobData_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobData");

            migrationBuilder.AddColumn<byte[]>(
                name: "RawData",
                table: "Jobs",
                type: "varbinary(max)",
                nullable: false,
                defaultValue: new byte[0]);
        }
    }
}
