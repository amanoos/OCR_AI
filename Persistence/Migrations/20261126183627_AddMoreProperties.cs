using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SteelCertExtraction.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMoreProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AluminumPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AntimonyPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ArsenicPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BoronPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CalciumPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CarbonEquivalent",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CobaltPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HardnessScale",
                table: "StoredCertificates",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HardnessValue",
                table: "StoredCertificates",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HydrogenPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LeadPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NiobiumPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NitrogenPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OxygenPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TantalumPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TinPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TitaniumPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TungstenPct",
                table: "StoredCertificates",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FieldName",
                table: "CertificateFieldEvidence",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.CreateTable(
                name: "CertificateMeasurements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertificateId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificateMeasurements_StoredCertificates_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "StoredCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CertificateMeasurements_CertificateId_Key_Label",
                table: "CertificateMeasurements",
                columns: new[] { "CertificateId", "Key", "Label" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CertificateMeasurements");

            migrationBuilder.DropColumn(
                name: "AluminumPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "AntimonyPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "ArsenicPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "BoronPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "CalciumPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "CarbonEquivalent",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "CobaltPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "HardnessScale",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "HardnessValue",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "HydrogenPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "LeadPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "NiobiumPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "NitrogenPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "OxygenPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "TantalumPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "TinPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "TitaniumPct",
                table: "StoredCertificates");

            migrationBuilder.DropColumn(
                name: "TungstenPct",
                table: "StoredCertificates");

            migrationBuilder.AlterColumn<string>(
                name: "FieldName",
                table: "CertificateFieldEvidence",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);
        }
    }
}
