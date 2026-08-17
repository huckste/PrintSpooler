using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintSpooler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinterDiscoveryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Host",
                table: "Printers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrinterUuid",
                table: "Printers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportedContentTypes",
                table: "Printers",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Host",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "PrinterUuid",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "SupportedContentTypes",
                table: "Printers");
        }
    }
}
