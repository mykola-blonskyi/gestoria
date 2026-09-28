using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestorIA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransactionClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Class",
                table: "BankTransactions",
                type: "character varying(17)",
                maxLength: 17,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BankTransactions_Class",
                table: "BankTransactions",
                sql: "\"Class\" IS NULL OR \"Class\" IN ('ActivityIncome', 'DeductibleExpense', 'SocialSecurity', 'AeatPayment', 'EmploymentIncome', 'SavingsIncome', 'OwnTransfer', 'Personal')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BankTransactions_Class",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "Class",
                table: "BankTransactions");
        }
    }
}
