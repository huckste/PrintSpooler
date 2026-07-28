using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintSpooler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinterForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Jobs_PrinterId",
                table: "Jobs",
                column: "PrinterId");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Printers_PrinterId",
                table: "Jobs",
                column: "PrinterId",
                principalTable: "Printers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Printers_PrinterId",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_PrinterId",
                table: "Jobs");
        }
    }
}
