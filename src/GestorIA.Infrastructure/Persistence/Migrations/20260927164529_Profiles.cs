using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestorIA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Profiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Singleton = table.Column<bool>(type: "boolean", nullable: false),
                    TaxYear = table.Column<int>(type: "integer", nullable: false),
                    Region = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    EmploymentIngresos = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    EmploymentSeguridadSocial = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Alta = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousYearRendimientoNeto = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    NewActivityPeriod = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    IngresosFromFormerEmployer = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    ProjectionIngresos = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ProjectionGastos = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    BaseCotizacion = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                    table.CheckConstraint("CK_Profiles_NewActivity", "(\"NewActivityPeriod\" IS NULL) = (\"IngresosFromFormerEmployer\" IS NULL)");
                    table.CheckConstraint("CK_Profiles_Singleton", "\"Singleton\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_Singleton",
                table: "Profiles",
                column: "Singleton",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Profiles");
        }
    }
}
