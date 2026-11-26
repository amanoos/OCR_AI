using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SteelCertExtraction.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoredCertificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeatNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Specification = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CertifyingLab = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CertDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PurchaseOrderNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CarbonPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    ManganesePct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    PhosphorusPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    SulfurPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    SiliconPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    CopperPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    NickelPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    ChromiumPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    MolybdenumPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    VanadiumPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    YieldStrengthPsi = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    TensileStrengthPsi = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    ElongationPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    ReductionOfAreaPct = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourceFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoredPdfPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExtractionDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelUsed = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExtractedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredCertificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CertificateFieldEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertificateId = table.Column<int>(type: "int", nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExtractedValueText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Page = table.Column<int>(type: "int", nullable: false),
                    BboxX = table.Column<double>(type: "float", nullable: true),
                    BboxY = table.Column<double>(type: "float", nullable: true),
                    BboxW = table.Column<double>(type: "float", nullable: true),
                    BboxH = table.Column<double>(type: "float", nullable: true),
                    Confirmed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateFieldEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificateFieldEvidence_StoredCertificates_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "StoredCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CertificateFieldEvidence_CertificateId_FieldName",
                table: "CertificateFieldEvidence",
                columns: new[] { "CertificateId", "FieldName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredCertificates_HeatNumber",
                table: "StoredCertificates",
                column: "HeatNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CertificateFieldEvidence");

            migrationBuilder.DropTable(
                name: "StoredCertificates");
        }
    }
}
