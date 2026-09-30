using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GestorIA.Domain.Models;
using GestorIA.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestorIA.Api.Tests;

// The review queue, the user's classification of a movement, and the estimate its classified movements give, against a real
// PostgreSQL. Each test starts an API on a database of its own, as local mode keeps one profile per installation.
public class ClassificationEndpoint
{
    private const string InvalidInput = "https://gestoria.local/problems/invalid-input";

    // The synthetic statement's lines no certain rule classifies (tests/fixtures/bank), in the transactions list's order. The
    // interest is money coming in and a TGSS cuota enters the estimate, so their rules only suggest.
    private static readonly string[] Queue =
    [
        "TRANSFERENCIA RECIBIDA CLIENTE SINTETICO UNO",
        "CUOTA AUTONOMOS TGSS",
        "COMPRA SUSCRIPCION SOFTWARE EJEMPLO",
        "RECIBO LUZ; FEBRERO",
        "TRANSFERENCIA A ES12 9999 0000 1111 2222 3333",
        "ABONO INTERESES CUENTA",
        "TRANSFERENCIA RECIBIDA CLIENTE SINTETICO DOS",
        "COMISION MANTENIMIENTO",
        "TRANSFERENCIA RECIBIDA CLIENTE SINTETICO UNO",
        "LIBRERIA TECNICA INVENTADA",
        "CUOTA AUTONOMOS TGSS",
        "DEVOLUCION COMPRA",
        "ABONO INTERESES CUENTA",
    ];

    [Fact]
    public async Task TheQueueHoldsTheLinesNoCertainRuleClassifiesInTheListsOrder()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        await client.ImportStatement(id, RepoFiles.Statement);

        var queue = await client.ReviewQueue(id);

        Assert.Equal(Queue, queue.Select(item => item!["description"]!.GetValue<string>()));
        var first = queue[0]!.AsObject();
        Assert.Equal(["id", "bookingDate", "valueDate", "description", "amount", "suggestion"], first.Select(member => member.Key));
        Assert.Equal(("2025-01-02", "2025-01-02", "2345.67"), (first["bookingDate"]!.GetValue<string>(), first["valueDate"]!.GetValue<string>(), first["amount"]!.GetValue<string>()));
        JsonNode? Suggestion(string description) => description switch
        {
            "ABONO INTERESES CUENTA" => new JsonObject { ["class"] = "savingsIncome", ["ruleId"] = "savings" },
            "CUOTA AUTONOMOS TGSS" => new JsonObject { ["class"] = "socialSecurity", ["ruleId"] = "tgss" },
            _ => null,
        };
        Assert.All(queue, item => Assert.True(JsonNode.DeepEquals(Suggestion(item!["description"]!.GetValue<string>()), item["suggestion"])));
    }

    [Fact]
    public async Task ASuggestedLineCarriesItsSuggestionAndALineOfAnotherYearIsLeftOut()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        var statement = "Fecha;Fecha Valor;Concepto;Importe;Saldo\n30/12/2024;30/12/2024;RECIBO ANTIGUO;-5,00;\n03/02/2025;03/02/2025;GITHUB INC;-4,00;\n";
        await client.ImportStatement(id, Encoding.UTF8.GetBytes(statement));

        var item = Assert.Single(await client.ReviewQueue(id))!;

        Assert.Equal("GITHUB INC", item["description"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["class"] = "deductibleExpense", ["ruleId"] = "vendors" }, item["suggestion"]));
    }

    [Fact]
    public async Task AClassifiedLineLeavesTheQueueAndALaterDecisionReplacesTheEarlier()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        await client.ImportStatement(id, RepoFiles.Statement);
        var line = (await client.ReviewQueue(id))[0]!["id"]!.GetValue<string>();

        var first = await client.ClassifyAs(line, "activityIncome");
        var again = await client.ClassifyAs(line, "activityIncome");
        var replaced = await client.ClassifyAs(line, "ownTransfer");

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NoContent], new[] { first.StatusCode, again.StatusCode, replaced.StatusCode });
        Assert.Equal(Queue[1..], (await client.ReviewQueue(id)).Select(item => item!["description"]!.GetValue<string>()));
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GestoriaDbContext>();
        Assert.Equal(TransactionClass.OwnTransfer, (await db.BankTransactions.SingleAsync(t => t.Id == Guid.Parse(line))).Class);
    }

    [Fact]
    public async Task AnUnknownLineOrALineOfADeletedProfileIsATransactionNotFound()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        await client.ImportStatement(id, RepoFiles.Statement);
        var line = (await client.ReviewQueue(id))[0]!["id"]!.GetValue<string>();
        using (var scope = api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<GestoriaDbContext>().Profiles.ExecuteDeleteAsync();
        }

        foreach (var unknown in new[] { Guid.NewGuid().ToString(), line })
        {
            var response = await client.ClassifyAs(unknown, "personal");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("https://gestoria.local/problems/transaction-not-found", (await response.Json())["type"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task TheQueueOfAnUnknownProfileIsAProfileNotFound()
    {
        await using var api = await Api();

        var response = await api.CreateClient().GetAsync($"/api/v1/profiles/{Guid.NewGuid()}/review-queue");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("https://gestoria.local/problems/profile-not-found", (await response.Json())["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("not json", "$")]
    [InlineData("[\"personal\"]", "$")]
    [InlineData("{ \"class\": \"personal\", \"class\": \"activityIncome\" }", "$")]
    [InlineData("{}", "$.class")]
    [InlineData("{ \"class\": null }", "$.class")]
    [InlineData("{ \"class\": 7 }", "$.class")]
    [InlineData("{ \"class\": \"unclear\" }", "$.class")]
    [InlineData("{ \"class\": \"ActivityIncome\" }", "$.class")]
    [InlineData("{ \"class\": \"personal, activityIncome\" }", "$.class")]
    [InlineData("{ \"class\": \"personal\", \"note\": \"cafe\" }", "$.note")]
    public async Task ABodyThatNamesNoKnownClassIsRefusedAtItsPathAndNothingIsStored(string body, string path)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        await client.ImportStatement(id, RepoFiles.Statement);
        var line = (await client.ReviewQueue(id))[0]!["id"]!.GetValue<string>();

        var response = await client.Classify(line, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Json();
        Assert.Equal(InvalidInput, problem["type"]!.GetValue<string>());
        Assert.Equal([path], problem["errors"]!.AsObject().Select(e => e.Key));
        Assert.Equal(Queue.Length, (await client.ReviewQueue(id)).Count);
    }

    [Fact]
    public async Task BothRoutesNeedTheKey()
    {
        await using var api = await Api();
        var id = await api.CreateClient().CreateProfile();
        var anonymous = api.CreateClientWithoutKey();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/profiles/{id}/review-queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.ClassifyAs(Guid.NewGuid().ToString(), "personal")).StatusCode);
    }

    // G12's 2025 profile, every quarter of which is closed today, with every movement of the review queue classified: the
    // three client transfers as activity income, the software and the books as expenses. Its estimate is the estimate of the
    // input file that states the same actuals and what is left of the projection, and its trace names each movement that
    // entered by id and date. The expenses wait for an invoice.
    public static TheoryData<string, JsonArray, string, string, int, int> Actuals => new()
    {
        { "Q2", new JsonArray(Actual("Q1", "2345.67", "87.61"), Actual("Q2", "4222.21", "87.61")), "15000.00", "600.00", 3, 1 },
        {
            "Q4",
            new JsonArray(Actual("Q1", "2345.67", "87.61"), Actual("Q2", "4222.21", "87.61"), Actual("Q3", "6567.88", "175.22"), Actual("Q4", "6567.88", "175.22")),
            "0.00", "0.00", 5, 2
        },
    };

    [Theory]
    [MemberData(nameof(Actuals))]
    public async Task TheEstimateOfClassifiedMovementsIsTheEstimateOfAFileStatingTheSameActuals(
        string asOf, JsonArray actuals, string ingresos, string gastos, int counted, int awaitingInvoice)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var (id, movements) = await ClassifiedG12(client);

        var fromProfile = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf={asOf}")).Json();
        var fromFile = await (await client.Estimate(2025, File(asOf, actuals, ingresos, gastos))).Json();

        var trace = fromProfile["trace"]!.AsArray();
        var ledgerSteps = trace.TakeWhile(step => step!["id"]!.GetValue<string>().StartsWith("ledger.", StringComparison.Ordinal)).ToList();
        Assert.True(JsonNode.DeepEquals(EngineOnly(fromFile, 0), EngineOnly(fromProfile, ledgerSteps.Count)), fromProfile.ToJsonString());
        Assert.True(JsonNode.DeepEquals(Ledger(asOf, counted, 0, awaitingInvoice), fromProfile["ledger"]));
        var q1 = ledgerSteps[0]!["inputs"]!.AsArray().Select(input => (input!["name"]!.GetValue<string>(), input["value"]!.GetValue<string>()));
        Assert.Equal(
            [("activityIncome", $"{movements[0]} 2025-01-02"), ("socialSecurity", $"{movements[1]} 2025-01-03")],
            q1.Take(2));
        Assert.Equal("ledger.projection-remaining", ledgerSteps[^1]!["id"]!.GetValue<string>());
    }

    // Just imported, nothing reviewed: every closed quarter holds a movement whose class is not known, so none is actuals, the
    // estimate is the projection's, as it was before the import, and the trace names what waits.
    [Fact]
    public async Task AnUnreviewedStatementLeavesTheEstimateOnTheProjection()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile("G12");
        await client.ImportStatement(id, RepoFiles.Statement);

        var response = await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q4");
        var input = RepoFiles.GoldenInput("G12");
        input["asOf"] = "Q4";
        var fromFile = await (await client.Estimate(2025, input)).Json();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fromProfile = await response.Json();
        Assert.Equal("ledger.pending-review", fromProfile["trace"]![0]!["id"]!.GetValue<string>());
        Assert.Equal(Queue.Length, fromProfile["trace"]![0]!["inputs"]!.AsArray().Count);
        Assert.True(JsonNode.DeepEquals(EngineOnly(fromFile, 0), EngineOnly(fromProfile, 1)), fromProfile.ToJsonString());
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["actualsThrough"] = null, ["counted"] = 0, ["awaitingReview"] = Queue.Length, ["awaitingInvoice"] = 0 }, fromProfile["ledger"]));
    }

    // Money the TGSS pays, here a sick-leave benefit, is no cuota: no rule confirms it, so it waits in the queue and holds its
    // quarter on the projection. Once the user calls it salary (LIRPF art. 17.2.a), the cuotas are the charges alone and the
    // estimate is that of the reviewed statement without it.
    [Fact]
    public async Task ATgssCreditWaitsForReviewAndNeverLowersTheCuotas()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var (id, _) = await ClassifiedG12(client);
        await client.ImportStatement(id, Encoding.UTF8.GetBytes("Fecha;Fecha Valor;Concepto;Importe;Saldo\n15/05/2025;15/05/2025;PRESTACION SEGURIDAD SOCIAL INCAPACIDAD TEMPORAL;900,00;\n"));

        var benefit = Assert.Single(await client.ReviewQueue(id))!;
        Assert.Equal("PRESTACION SEGURIDAD SOCIAL INCAPACIDAD TEMPORAL", benefit["description"]!.GetValue<string>());
        Assert.Null(benefit["suggestion"]);
        var waiting = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q4")).Json();
        Assert.True(JsonNode.DeepEquals(Ledger("Q1", 2, 1, 1), waiting["ledger"]));

        await client.ClassifyAs(benefit["id"]!.GetValue<string>(), "employmentIncome");
        var reviewed = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q4")).Json();
        var actuals = new JsonArray(Actual("Q1", "2345.67", "87.61"), Actual("Q2", "4222.21", "87.61"), Actual("Q3", "6567.88", "175.22"), Actual("Q4", "6567.88", "175.22"));
        var fromFile = await (await client.Estimate(2025, File("Q4", actuals, "0.00", "0.00"))).Json();
        var ledgerSteps = reviewed["trace"]!.AsArray().Count(step => step!["id"]!.GetValue<string>().StartsWith("ledger.", StringComparison.Ordinal));
        Assert.True(JsonNode.DeepEquals(EngineOnly(fromFile, 0), EngineOnly(reviewed, ledgerSteps)), reviewed.ToJsonString());
    }

    // A client whose name reads like the tax office is still a client: its payment is only suggested as an AEAT line, waits in
    // the queue and holds Q2 on the projection. Classified as activity income, it adds to Q2's ingresos.
    [Fact]
    public async Task ACreditNamedLikeTheAeatWaitsForReview()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var (id, _) = await ClassifiedG12(client);
        await client.ImportStatement(id, Encoding.UTF8.GetBytes("Fecha;Fecha Valor;Concepto;Importe;Saldo\n12/05/2025;12/05/2025;TRANSF HACIENDA LOS OLIVOS SL FRA 12;1.200,00;\n"));

        var payment = Assert.Single(await client.ReviewQueue(id))!;
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["class"] = "aeatPayment", ["ruleId"] = "aeat-credit" }, payment["suggestion"]));
        var waiting = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2")).Json();
        Assert.Equal("Q1", waiting["ledger"]!["actualsThrough"]!.GetValue<string>());

        await client.ClassifyAs(payment["id"]!.GetValue<string>(), "activityIncome");
        var reviewed = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2")).Json();
        var fromFile = await (await client.Estimate(2025, File("Q2", new JsonArray(Actual("Q1", "2345.67", "87.61"), Actual("Q2", "5422.21", "87.61")), "15000.00", "600.00"))).Json();
        var ledgerSteps = reviewed["trace"]!.AsArray().Count(step => step!["id"]!.GetValue<string>().StartsWith("ledger.", StringComparison.Ordinal));
        Assert.True(JsonNode.DeepEquals(EngineOnly(fromFile, 0), EngineOnly(reviewed, ledgerSteps)), reviewed.ToJsonString());
    }

    // A TGSS receipt may be a domestic employee's, whose cuota is no expense of the activity: it is only suggested, holds Q2 on
    // the projection, and classified as personal leaves the reviewed estimate as it was.
    [Fact]
    public async Task ATgssDebitIsOnlySuggestedAndCountsOnlyWhenTheUserSaysSo()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var (id, _) = await ClassifiedG12(client);
        var before = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2")).Json();
        await client.ImportStatement(id, Encoding.UTF8.GetBytes("Fecha;Fecha Valor;Concepto;Importe;Saldo\n05/05/2025;05/05/2025;RECIBO TGSS EMPLEADA HOGAR;-190,00;\n"));

        var receipt = Assert.Single(await client.ReviewQueue(id))!;
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["class"] = "socialSecurity", ["ruleId"] = "tgss" }, receipt["suggestion"]));
        var waiting = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2")).Json();
        Assert.Equal("Q1", waiting["ledger"]!["actualsThrough"]!.GetValue<string>());

        await client.ClassifyAs(receipt["id"]!.GetValue<string>(), "personal");
        var reviewed = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2")).Json();
        Assert.Equal(before["nextPayment"]!.ToJsonString(), reviewed["nextPayment"]!.ToJsonString());
        Assert.Equal(before["holdBackShare"]!.GetValue<string>(), reviewed["holdBackShare"]!.GetValue<string>());
    }

    // A statement exported on 22 April: Q1 and three weeks of Q2, whose two lines rules confirm and nobody is asked about. Q2
    // holds lines but the statement does not span it, so it stays projected. A later statement meeting it and reaching past
    // 30 June covers it but holds no movement in May or June, so a statement is missing (#94); the one holding them makes Q2
    // actuals.
    [Fact]
    public async Task AQuarterTheStatementDoesNotSpanStaysProjected()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile("G12");
        var april = SyntheticLines(line => line[3..5] is "01" or "02" or "03") + "20/04/2025;20/04/2025;PAGO MODELO 130 1T AEAT;-312,88;\n22/04/2025;22/04/2025;MERCADONA;-35,10;\n";
        await client.ImportStatement(id, Encoding.UTF8.GetBytes(april));
        await client.ClassifySyntheticQueue(id);
        Assert.Empty(await client.ReviewQueue(id));

        var estimate = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2")).Json();
        var fromFile = await (await client.Estimate(2025, File("Q2", new JsonArray(Actual("Q1", "2345.67", "87.61")), "22500.00", "900.00"))).Json();

        var ledgerSteps = estimate["trace"]!.AsArray().Count(step => step!["id"]!.GetValue<string>().StartsWith("ledger.", StringComparison.Ordinal));
        Assert.True(JsonNode.DeepEquals(EngineOnly(fromFile, 0), EngineOnly(estimate, ledgerSteps)), estimate.ToJsonString());
        Assert.Equal("Q1", estimate["ledger"]!["actualsThrough"]!.GetValue<string>());
        Assert.Equal(
            "no imported statement's period covers the days from 2025-04-23 on; Q2 stays on the projection, with what follows, until statements whose periods cover its days from 2025-04-01 through 2025-07-01 are imported",
            Coverage(estimate));

        await client.ImportStatement(id, Encoding.UTF8.GetBytes("Fecha;Fecha Valor;Concepto;Importe;Saldo\n23/04/2025;23/04/2025;MERCADONA;-20,00;\n15/07/2025;15/07/2025;MERCADONA;-20,00;\n"));
        var july = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2")).Json();
        Assert.Equal("Q1", july["ledger"]!["actualsThrough"]!.GetValue<string>());
        Assert.StartsWith("no stored movement from 2025-04-24 through 2025-07-14 (82 days, over 33)", Coverage(july), StringComparison.Ordinal);

        await client.ImportStatement(id, Encoding.UTF8.GetBytes("Fecha;Fecha Valor;Concepto;Importe;Saldo\n20/05/2025;20/05/2025;MERCADONA;-20,00;\n18/06/2025;18/06/2025;MERCADONA;-20,00;\n"));
        var complete = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2")).Json();
        Assert.Equal("Q2", complete["ledger"]!["actualsThrough"]!.GetValue<string>());
    }

    // The export carries each statement's period and each movement's class, so a restored installation covers the same days and counts the same
    // actuals: the estimate is the one before, the partial April import's coverage step included.
    [Fact]
    public async Task ARestoredExportGivesTheSameEstimate()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile("G12");
        await client.ImportStatement(id, Encoding.UTF8.GetBytes(SyntheticLines(line => line[3..5] is "01" or "02" or "03") + "20/04/2025;20/04/2025;MERCADONA;-35,10;\n"));
        await client.ImportStatement(id, Encoding.UTF8.GetBytes("Fecha;Fecha Valor;Concepto;Importe;Saldo\n10/06/2025;10/06/2025;MERCADONA;-20,00;\n15/07/2025;15/07/2025;MERCADONA;-20,00;\n"));
        await client.ClassifySyntheticQueue(id);
        var before = await client.GetStringAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2");
        var export = await client.GetStringAsync($"/api/v1/profiles/{id}/export");

        (await client.DeleteAsync($"/api/v1/profiles/{id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, (await client.Restore(export)).StatusCode);

        var after = await client.GetStringAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2");
        Assert.Equal(before, after);
        Assert.Contains("no imported statement's period covers the days from 2025-04-21 through 2025-06-09", before, StringComparison.Ordinal);
    }

    // A first statement starting on 15 February lacks the first month of alta: Q1 stays projected, and the trace names the days.
    [Fact]
    public async Task AFirstStatementStartingAfterTheQuarterBeginsLeavesItProjected()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile("G12");
        await client.ImportStatement(id, Encoding.UTF8.GetBytes(SyntheticLines(line => line[3..5] != "01")));
        await client.ClassifySyntheticQueue(id);

        var estimate = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2")).Json();

        Assert.Null(estimate["ledger"]!["actualsThrough"]);
        Assert.Equal(
            "no imported statement's period covers the days from 2025-01-15 through 2025-02-13; Q1 stays on the projection, with what follows, until statements whose periods cover its days from 2025-01-15 through 2025-04-01 are imported",
            Coverage(estimate));
    }

    // Two statements that overlap (#88): A runs from 2 January to 7 April, B from 31 March to 31 December, so B's first two
    // lines are A's last two and B stores nothing for those days. B's period is still its lines' first to last date, the
    // duplicates included, so the two periods meet and Q2 is actuals.
    [Fact]
    public async Task OverlappingStatementsCoverTheDaysTheyShare()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile("G12");
        await client.ImportStatement(id, Encoding.UTF8.GetBytes(SyntheticLines(line => Day(line) <= new DateOnly(2025, 4, 7))));

        var b = await (await client.ImportStatement(id, Encoding.UTF8.GetBytes(SyntheticLines(line => Day(line) >= new DateOnly(2025, 3, 31))))).Json();
        await client.ClassifySyntheticQueue(id);

        Assert.Equal((2, "2025-03-31", "2025-12-31"), (b["alreadyImported"]!.GetValue<int>(), b["from"]!.GetValue<string>(), b["to"]!.GetValue<string>()));
        var estimate = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q2")).Json();
        Assert.Equal("Q2", estimate["ledger"]!["actualsThrough"]!.GetValue<string>());
        Assert.DoesNotContain(estimate["trace"]!.AsArray(), step => step!["id"]!.GetValue<string>() == "ledger.coverage");
    }

    // Quarterly exports, each stated for its quarter (#88). Nothing moved between 31 March and 7 April, or between 5 May and
    // 30 June, so the lines alone leave those days uncovered; the stated periods meet. A period ending on a quarter's last day
    // shows only that day covered, so Q3, the last one stated, waits for the statement after it.
    [Fact]
    public async Task QuarterlyStatementsStatedForTheirQuartersMakeTheQuartersActuals()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile("G12");
        foreach (var (first, last) in new[] { ("2025-01-01", "2025-03-31"), ("2025-04-01", "2025-06-30"), ("2025-07-01", "2025-09-30") })
        {
            var (from, to) = (DateOnly.Parse(first, CultureInfo.InvariantCulture), DateOnly.Parse(last, CultureInfo.InvariantCulture));
            var quarter = Encoding.UTF8.GetBytes(SyntheticLines(line => Day(line) >= from && Day(line) <= to));
            (await client.ImportStatement(id, quarter, from: first, to: last)).EnsureSuccessStatusCode();
        }

        await client.ClassifySyntheticQueue(id);

        var estimate = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q3")).Json();
        Assert.Equal("Q2", estimate["ledger"]!["actualsThrough"]!.GetValue<string>());
        Assert.Equal(
            "no imported statement's period covers the days from 2025-10-01 on; Q3 stays on the projection, with what follows, until statements whose periods cover its days from 2025-07-01 through 2025-10-01 are imported",
            Coverage(estimate));
    }

    private static DateOnly Day(string line) => DateOnly.ParseExact(line[..10], "dd/MM/yyyy", CultureInfo.InvariantCulture);

    // The synthetic statement's header and the lines the filter keeps.
    private static string SyntheticLines(Func<string, bool> keep) =>
        string.Join("\n", Encoding.UTF8.GetString(RepoFiles.Statement).ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("Fecha", StringComparison.Ordinal) || keep(line))) + "\n";

    private static string Coverage(JsonObject estimate) =>
        estimate["trace"]!.AsArray().Single(step => step!["id"]!.GetValue<string>() == "ledger.coverage")!["formula"]!.GetValue<string>();

    // 10 August 2025: Q1 and Q2 are closed, Q3 is not, so the actuals end with Q2 and the projection keeps six of its twelve months.
    [Fact]
    public async Task AQuarterStillOpenStaysProjected()
    {
        await using var api = await Api(new DateTimeOffset(2025, 8, 10, 12, 0, 0, TimeSpan.Zero));
        var client = api.CreateClient();
        var (id, _) = await ClassifiedG12(client);

        var fromProfile = await (await client.GetAsync($"/api/v1/profiles/{id}/set-aside/estimate?asOf=Q3")).Json();
        var fromFile = await (await client.Estimate(2025, File("Q3", new JsonArray(Actual("Q1", "2345.67", "87.61"), Actual("Q2", "4222.21", "87.61")), "15000.00", "600.00"))).Json();

        var trace = fromProfile["trace"]!.AsArray();
        Assert.Equal(["ledger.Q1.movements", "ledger.Q2.movements", "ledger.projection-remaining"], trace.Take(3).Select(step => step!["id"]!.GetValue<string>()));
        Assert.Equal("30000.00 × 6 / 12 = 15000.00; 1200.00 × 6 / 12 = 600.00", trace[2]!["formula"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(EngineOnly(fromFile, 0), EngineOnly(fromProfile, 3)), fromProfile.ToJsonString());
        Assert.True(JsonNode.DeepEquals(Ledger("Q2", 3, 0, 1), fromProfile["ledger"]));
    }

    // The G12 profile with the synthetic statement and a movement of next January imported, so every quarter of 2025 is
    // covered, and every movement of its review queue classified. The ids are of
    // the first two movements in the list, the first transfer and the first TGSS cuota.
    private static async Task<(string Id, List<string> Movements)> ClassifiedG12(HttpClient client)
    {
        var id = await client.CreateProfile("G12");
        await client.ImportStatement(id, RepoFiles.Statement);
        await client.ImportNextJanuary(id);
        await client.ClassifySyntheticQueue(id);
        var movements = JsonNode.Parse(await client.GetStringAsync($"/api/v1/profiles/{id}/transactions?year=2025"))!.AsArray();
        return (id, [.. movements.Take(2).Select(t => t!["id"]!.GetValue<string>())]);
    }

    private static JsonObject Actual(string quarter, string ingresosYtd, string cuotasSsYtd) => new()
    {
        ["quarter"] = quarter,
        ["ingresosYtd"] = ingresosYtd,
        ["gastosYtd"] = cuotasSsYtd,
        ["cuotasSsYtd"] = cuotasSsYtd,
    };

    private static JsonObject File(string asOf, JsonArray actuals, string ingresos, string gastos)
    {
        var input = RepoFiles.GoldenInput("G12");
        input["asOf"] = asOf;
        input["activity"]!["actuals"] = actuals.DeepClone();
        input["activity"]!["projection"]!["ingresos"] = ingresos;
        input["activity"]!["projection"]!["gastos"] = gastos;
        return input;
    }

    // The answer without the ledger's part: its steps at the head of the trace and its counts.
    private static JsonObject EngineOnly(JsonObject estimate, int ledgerSteps)
    {
        var engine = estimate.DeepClone().AsObject();
        engine.Remove("ledger");
        engine["trace"] = new JsonArray([.. estimate["trace"]!.AsArray().Skip(ledgerSteps).Select(step => step!.DeepClone())]);
        return engine;
    }

    private static JsonObject Ledger(string actualsThrough, int counted, int awaitingReview, int awaitingInvoice) => new()
    {
        ["actualsThrough"] = actualsThrough,
        ["counted"] = counted,
        ["awaitingReview"] = awaitingReview,
        ["awaitingInvoice"] = awaitingInvoice,
    };

    private static async Task<ApiFactory> Api(DateTimeOffset? now = null)
    {
        var api = new ApiFactory { Clock = now is { } fixedNow ? new FixedTimeProvider(fixedNow) : TimeProvider.System };
        await api.InitializeAsync();
        return api;
    }
}
