using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestorIA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StatementImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StatementImports",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatementImports", x => new { x.ProfileId, x.Sequence });
                    table.CheckConstraint("CK_StatementImports_Period", "\"From\" <= \"To\"");
                    table.ForeignKey(
                        name: "FK_StatementImports_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // One import per import that stored movements before #88, its period the first to the last booking date of what it
            // stored: the span #73 read from the lines, so every estimate stays as it was until a new statement is imported.
            migrationBuilder.Sql(
                """
                INSERT INTO "StatementImports" ("ProfileId", "Sequence", "From", "To")
                SELECT "ProfileId", "ImportSequence", min("BookingDate"), max("BookingDate")
                FROM "BankTransactions"
                GROUP BY "ProfileId", "ImportSequence";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_ProfileId_ImportSequence",
                table: "BankTransactions",
                columns: new[] { "ProfileId", "ImportSequence" });

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransactions_StatementImports_ProfileId_ImportSequence",
                table: "BankTransactions",
                columns: new[] { "ProfileId", "ImportSequence" },
                principalTable: "StatementImports",
                principalColumns: new[] { "ProfileId", "Sequence" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankTransactions_StatementImports_ProfileId_ImportSequence",
                table: "BankTransactions");

            migrationBuilder.DropTable(
                name: "StatementImports");

            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_ProfileId_ImportSequence",
                table: "BankTransactions");
        }
    }
}
