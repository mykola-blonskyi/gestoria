using System.Runtime.CompilerServices;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;
using GestorIA.Infrastructure.Parsers;
using GestorIA.Infrastructure.Transactions;
using Xunit;

namespace GestorIA.Domain.Tests;

public class TransactionRulesTests
{
    private static readonly TransactionRules Shipped = TransactionRules.Load(Path.Combine(RepoRoot(), "config", "transaction-rules.json"));

    private static Classification Classify(string description, decimal amount, TransactionClass? decided = null) =>
        Shipped.Classify(decided, description, new Money(amount));

    [Theory]
    [InlineData("PAGO MODELO 130 1T AEAT", -312.88, TransactionClass.AeatPayment, "aeat")]
    [InlineData("SUPERMERCADO FICTICIO", -47.16, TransactionClass.Personal, "personal")]
    [InlineData("COMPRA DIA SUPER", -12.00, TransactionClass.Personal, "personal")]
    [InlineData("café bar el rincón", -3.10, TransactionClass.Personal, "personal")]
    [InlineData("COMPRA SUPER DIA", -12.00, TransactionClass.Personal, "personal")]
    [InlineData("CAFE BAR", -3.10, TransactionClass.Personal, "personal")]
    [InlineData("BAR,LA ESQUINA", -3.10, TransactionClass.Personal, "personal")]
    [InlineData("PAGO MOD.130 2T", -200.00, TransactionClass.AeatPayment, "aeat")]
    public void ACertainRuleConfirmsItsClass(string description, decimal amount, TransactionClass expected, string rule)
    {
        Assert.Equal(new Classification.Confirmed(expected, rule), Classify(description, amount));
    }

    // Money coming in could be activity income, and a class that counts moves the estimate, so a rule may only suggest either
    // (business rule 2): a client named Hacienda Los Olivos is a client, and a TGSS receipt may be a domestic employee's.
    [Theory]
    [InlineData("GITHUB INC SAN FRANCISCO", -4.00, TransactionClass.DeductibleExpense, "vendors")]
    [InlineData("CUOTA AUTONOMOS TGSS", -87.61, TransactionClass.SocialSecurity, "tgss")]
    [InlineData("RECIBO SEGURIDAD SOCIAL REG.ESP.AUTONOMOS", -87.61, TransactionClass.SocialSecurity, "tgss")]
    [InlineData("RECIBO TGSS EMPLEADA HOGAR", -190.00, TransactionClass.SocialSecurity, "tgss")]
    [InlineData("TGSS RECARGO APREMIO", -20.00, TransactionClass.SocialSecurity, "tgss")]
    [InlineData("Nómina marzo", 1500.00, TransactionClass.EmploymentIncome, "nomina")]
    [InlineData("ABONO NOMINA", 1500.00, TransactionClass.EmploymentIncome, "nomina")]
    [InlineData("ABONO INTERESES CUENTA", 0.43, TransactionClass.SavingsIncome, "savings")]
    [InlineData("ABONO DIVIDENDO ACCIONES", 12.30, TransactionClass.SavingsIncome, "savings")]
    [InlineData("ABONO DIVIDEND ETF", 12.30, TransactionClass.SavingsIncome, "savings")]
    [InlineData("DEVOLUCION AEAT IRPF", 300.00, TransactionClass.AeatPayment, "aeat-credit")]
    [InlineData("TRANSF HACIENDA LOS OLIVOS SL FRA 12", 1200.00, TransactionClass.AeatPayment, "aeat-credit")]
    public void ARuleOnlySuggestsAClassThatCountsAndEveryCredit(string description, decimal amount, TransactionClass expected, string rule)
    {
        Assert.Equal(new Classification.Suggested(expected, rule), Classify(description, amount));
    }

    [Fact]
    public void NoShippedRuleConfirmsACreditOrAClassThatCounts()
    {
        Assert.All(Shipped.Rules.Where(rule => rule.Certain), rule =>
        {
            Assert.Equal(RuleDirection.Debit, rule.Direction);
            Assert.False(LedgerActuals.Counts(rule.Class), rule.Id);
        });
    }

    [Theory]
    [InlineData("PARAGUAS Y DIARIOS", -9.99)]
    [InlineData("VIAJE BARCELONA", -60.00)]
    [InlineData("SUSCRIPCION DIARIO", -9.99)]
    [InlineData("BARRA DE PAN", -1.20)]
    [InlineData("SUBTGSS", -1.00)]
    [InlineData("NOMINA", -1500.00)]
    [InlineData("DEVOLUCION GITHUB", 4.00)]
    [InlineData("TRANSFERENCIA RECIBIDA CLIENTE", 2345.67)]
    [InlineData("TRANSFERENCIA AEATX CONSULTING", 500.00)]
    [InlineData("ABONO NOMINAL CLIENTE", 700.00)]
    [InlineData("AMORT VALOR NOMINAL BONO", 1000.00)]
    [InlineData("PAGO AEATX CONSULTING", -50.00)]
    [InlineData("CUOTA AUTONOMICA COLEGIO", -60.00)]
    public void NoRuleMatchesInsideAWordOrAgainstItsDirection(string description, decimal amount)
    {
        Assert.Equal(new Classification.Unclear(), Classify(description, amount));
    }

    // SPEC-004 §6 holds the TGSS rule to its precision for what TGSS charges. Money TGSS pays is a benefit (incapacidad
    // temporal, cese de actividad) or a refund, and which one only the user knows, so it waits for review.
    [Theory]
    [InlineData("PRESTACION SEGURIDAD SOCIAL INCAPACIDAD TEMPORAL", 900.00)]
    [InlineData("DEVOLUCION TGSS", 20.00)]
    public void MoneyFromTheTgssIsNotConfirmedAsACuota(string description, decimal amount)
    {
        Assert.Equal(new Classification.Unclear(), Classify(description, amount));
    }

    [Fact]
    public void TheFirstMatchingRuleWins()
    {
        Assert.Equal(new Classification.Suggested(TransactionClass.SocialSecurity, "tgss"), Classify("HACIENDA Y TGSS", -10.00m));
    }

    [Fact]
    public void TheUsersDecisionWinsOverEveryRule()
    {
        Assert.Equal(new Classification.Confirmed(TransactionClass.Personal, null), Classify("CUOTA AUTONOMOS TGSS", -87.61m, TransactionClass.Personal));
    }

    [Fact]
    public void AMatchIgnoresCaseAndDiacriticsOnBothSides()
    {
        var rules = TransactionRules.Parse(Rules("{ \"id\": \"agua\", \"class\": \"personal\", \"direction\": \"debit\", \"certain\": true, \"patterns\": [\"Agüa\"], \"source\": \"test\" }"), "rules.json");

        Assert.Equal(new Classification.Confirmed(TransactionClass.Personal, "agua"), rules.Classify(null, "RECIBO AGUA", new Money(-9.00m)));
        Assert.Equal(new Classification.Unclear(), rules.Classify(null, "PARAGÜAS", new Money(-9.00m)));
    }

    // The synthetic statement's lines (tests/fixtures/bank), each with the class the shipped rules give it; null is unclear.
    [Fact]
    public void TheSyntheticStatementIsClassifiedAsSpec004Says()
    {
        var statement = new BbvaCsvStatementParser().Parse(File.ReadAllText(Path.Combine(RepoRoot(), "tests", "fixtures", "bank", "bbva-2025-synthetic.csv")));

        var classes = statement.Select(line => Classify(line.Movement.Description, line.Movement.Amount.Amount) switch
        {
            Classification.Confirmed confirmed => confirmed.Class,
            _ => (TransactionClass?)null,
        });

        TransactionClass? queue = null;
        Assert.Equal(
            [
                queue, queue, TransactionClass.Personal, TransactionClass.Personal, queue, queue, queue,
                queue, queue, TransactionClass.AeatPayment, TransactionClass.Personal, TransactionClass.Personal, queue, queue,
                TransactionClass.Personal, queue, TransactionClass.Personal, queue, queue, TransactionClass.Personal,
                TransactionClass.Personal, TransactionClass.Personal, queue,
            ],
            classes);
    }

    public static TheoryData<string, string> MalformedRules => new()
    {
        { Rule("r", transactionClass: "unclear"), "rule \"r\": class \"unclear\"" },
        { Rule("r", direction: "in"), "rule \"r\": direction \"in\"" },
        { Rule("r", patterns: "[]"), "rule \"r\": patterns" },
        { Rule("r", patterns: "[\" \"]"), "rule \"r\": patterns" },
        { Rule("r", patterns: "[\"..\"]"), "rule \"r\": patterns" },
        { Rule("r", direction: "credit"), "rule \"r\" is certain for money coming in" },
        { Rule("r", direction: "any"), "rule \"r\" is certain for money coming in" },
        { Rule("r", transactionClass: "socialSecurity"), "rule \"r\" is certain for socialSecurity, a class whose money enters the estimate" },
        { Rule("r", transactionClass: "activityIncome"), "rule \"r\" is certain for activityIncome, a class whose money enters the estimate" },
        { Rule("r", transactionClass: "deductibleExpense"), "rule \"r\" is certain for deductibleExpense, a class whose money enters the estimate" },
        { Rule(""), "rule 1 has no id" },
    };

    [Theory]
    [MemberData(nameof(MalformedRules))]
    public void AMalformedRuleIsRefusedByName(string rule, string failure)
    {
        var refused = Assert.Throws<InvalidTransactionRulesException>(() => TransactionRules.Parse(Rules(rule), "rules.json"));

        Assert.StartsWith(failure, Assert.Single(refused.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void ARepeatedIdIsRefused()
    {
        var refused = Assert.Throws<InvalidTransactionRulesException>(() => TransactionRules.Parse(Rules(Rule("r"), Rule("r")), "rules.json"));

        Assert.Equal("rule \"r\" repeats an id an earlier rule has.", Assert.Single(refused.Failures));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[{ \"id\": \"r\" }]")]
    public void AFileOfTheWrongShapeIsRefused(string json)
    {
        Assert.Throws<InvalidTransactionRulesException>(() => TransactionRules.Parse(json, "rules.json"));
    }

    [Fact]
    public void AnUnknownFieldIsRefused()
    {
        Assert.Throws<InvalidTransactionRulesException>(() => TransactionRules.Parse(Rules(Rule("r").Replace("\"certain\"", "\"regex\": \".*\", \"certain\"", StringComparison.Ordinal)), "rules.json"));
    }

    private static string Rule(string id, string transactionClass = "personal", string direction = "debit", string patterns = "[\"X\"]") =>
        $"{{ \"id\": \"{id}\", \"class\": \"{transactionClass}\", \"direction\": \"{direction}\", \"certain\": true, \"patterns\": {patterns}, \"source\": \"test\" }}";

    private static string Rules(params string[] rules) => $"[{string.Join(", ", rules)}]";

    private static string RepoRoot([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
