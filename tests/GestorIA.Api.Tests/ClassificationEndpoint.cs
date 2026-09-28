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

    // The G12 profile with the synthetic statement imported and every movement of its review queue classified. The ids are of
    // the first two movements in the list, the first transfer and the first TGSS cuota.
    private static async Task<(string Id, List<string> Movements)> ClassifiedG12(HttpClient client)
    {
        var id = await client.CreateProfile("G12");
        await client.ImportStatement(id, RepoFiles.Statement);
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
