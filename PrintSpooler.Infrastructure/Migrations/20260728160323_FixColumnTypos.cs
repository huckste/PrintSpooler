using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintSpooler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixColumnTypos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FailvoerPrinterId",
                table: "Printers",
                newName: "FailoverPrinterId");

            migrationBuilder.RenameColumn(
                name: "SubmitteBy",
                table: "Jobs",
                newName: "SubmittedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FailoverPrinterId",
                table: "Printers",
                newName: "FailvoerPrinterId");

            migrationBuilder.RenameColumn(
                name: "SubmittedBy",
                table: "Jobs",
                newName: "SubmitteBy");
        }
    }
}
