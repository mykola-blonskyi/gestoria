using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GestorIA.Api.Transactions;
using GestorIA.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestorIA.Api.Tests;

// Importing a bank statement into the stored profile and reading its movements back, against a real PostgreSQL. Each test
// starts an API on a database of its own, as local mode keeps one profile per installation.
public class TransactionsEndpoint
{
    private const string InvalidInput = "https://gestoria.local/problems/invalid-input";

    [Fact]
    public async Task AStatementIsImportedAndItsMovementsReadBackInDateOrderAndADaysInTheFilesOrder()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();

        var response = await client.ImportStatement(id, RepoFiles.Statement);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(JsonNode.DeepEquals(Imported(18, 18, 0), await response.Json()));
        var all = await Transactions(client, id);
        Assert.Equal(18, all.Count);
        var first = all[0]!;
        Assert.Equal("2025-01-02", first["bookingDate"]!.GetValue<string>());
        Assert.Equal("TRANSFERENCIA RECIBIDA CLIENTE SINTETICO UNO", first["description"]!.GetValue<string>());
        Assert.Equal("2345.67", first["amount"]!.GetValue<string>());
        Assert.Equal("8345.67", first["balance"]!.GetValue<string>());
        Assert.Equal(("8256.21", "8254.36"), (all[2]!["balance"]!.GetValue<string>(), all[3]!["balance"]!.GetValue<string>()));
        Assert.Equal("RECIBO LUZ; FEBRERO", all[5]!["description"]!.GetValue<string>());
        Assert.Equal(("2025-03-31", "2025-04-01"), (all[7]!["bookingDate"]!.GetValue<string>(), all[7]!["valueDate"]!.GetValue<string>()));
        Assert.Equal([.. all.Select(t => t!["bookingDate"]!.GetValue<string>()).Order(StringComparer.Ordinal)], all.Select(t => t!["bookingDate"]!.GetValue<string>()));
    }

    [Fact]
    public async Task ImportingTheSameStatementAgainStoresNothingNew()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        await client.ImportStatement(id, RepoFiles.Statement);

        var again = await client.ImportStatement(id, RepoFiles.Statement);

        Assert.True(JsonNode.DeepEquals(Imported(18, 0, 18), await again.Json()));
        Assert.Equal(18, (await Transactions(client, id)).Count);
    }

    [Fact]
    public async Task TwoIdenticalLinesOnOneDayAreBothKeptAndAnOverlappingExportAddsOnlyWhatIsNew()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        var lines = Encoding.UTF8.GetString(RepoFiles.Statement).ReplaceLineEndings("\n").TrimEnd().Split('\n');
        // January to the first coffee; then a re-export from the coffees to the end of March, with a third coffee that day.
        var january = string.Join('\n', lines[..4]);
        var overlapping = string.Join('\n', [lines[0], lines[3], lines[4], "15/01/2025;15/01/2025;CAFETERIA LA PRUEBA;-1,85;8.252,51", .. lines[5..9]]);

        var first = await client.ImportStatement(id, Encoding.UTF8.GetBytes(january));
        var second = await client.ImportStatement(id, Encoding.UTF8.GetBytes(overlapping));

        Assert.True(JsonNode.DeepEquals(Imported(3, 3, 0), await first.Json()));
        Assert.True(JsonNode.DeepEquals(Imported(7, 6, 1), await second.Json()));
        var coffees = (await Transactions(client, id, "?year=2025&quarter=Q1")).Where(t => t!["description"]!.GetValue<string>() == "CAFETERIA LA PRUEBA");
        Assert.Equal(3, coffees.Count());
    }

    // A long statement followed at once by a short one: each day's lines follow their imports' order and then their files',
    // whatever the clock says. The lines of the long one fill a single day so that the short one's line must land last.
    [Fact]
    public async Task ALaterImportsLineComesAfterEveryEarlierLineOfTheSameDay()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        var header = "Fecha;Fecha Valor;Concepto;Importe;Saldo\n";
        var many = header + string.Concat(Enumerable.Range(1, 5_000).Select(n => $"15/05/2025;15/05/2025;LINEA {n};-{n},00;\n"));

        await client.ImportStatement(id, Encoding.UTF8.GetBytes(many));
        await client.ImportStatement(id, Encoding.UTF8.GetBytes(header + "15/05/2025;15/05/2025;LA ULTIMA;-0,01;\n"));
        await client.ImportStatement(id, Encoding.UTF8.GetBytes(header + "14/05/2025;14/05/2025;EL DIA ANTES;-0,02;\n"));

        var descriptions = (await Transactions(client, id, "?year=2025&quarter=Q2")).Select(t => t!["description"]!.GetValue<string>()).ToList();
        Assert.Equal(5_002, descriptions.Count);
        Assert.Equal("EL DIA ANTES", descriptions[0]);
        Assert.Equal([.. Enumerable.Range(1, 5_000).Select(n => $"LINEA {n}")], descriptions[1..^1]);
        Assert.Equal("LA ULTIMA", descriptions[^1]);
    }

    [Theory]
    [InlineData("text/csv; charset=utf-8")]
    [InlineData("text/plain")]
    [InlineData("application/vnd.ms-excel")]
    public async Task ACsvFileIsAcceptedUnderTheTypesBrowsersGiveIt(string contentType)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();

        var response = await client.ImportStatement(id, RepoFiles.Statement, contentType: contentType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    public async Task AStatementSentAsAnythingElseIsAnUnsupportedMediaType(string? contentType)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();

        var response = await client.ImportStatement(id, RepoFiles.Statement, contentType: contentType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(Http.ProblemJson, response.MediaType());
        Assert.Equal("https://gestoria.local/problems/statement-media-type", (await response.Json())["type"]!.GetValue<string>());
        Assert.Empty(await Transactions(client, id));
    }

    [Fact]
    public async Task AStatementWithoutTheKeyIsRefusedBeforeItsTypeIsLookedAt()
    {
        await using var api = await Api();
        var id = await api.CreateClient().CreateProfile();

        var response = await api.CreateClientWithoutKey().ImportStatement(id, RepoFiles.Statement, contentType: "application/json");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TheDatabaseRefusesAStatementLineStoredTwice()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        await client.ImportStatement(id, RepoFiles.Statement);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GestoriaDbContext>();
        var stored = await db.BankTransactions.AsNoTracking().FirstAsync();

        db.BankTransactions.Add(new() { Id = Guid.NewGuid(), ProfileId = stored.ProfileId, LineKey = stored.LineKey, Description = "copy" });

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", (refused.InnerException as Npgsql.PostgresException)?.SqlState);
    }

    [Fact]
    public async Task ConcurrentImportsOfOneStatementStoreEachLineOnce()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();

        var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.ImportStatement(id, RepoFiles.Statement)));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.OK, answer.StatusCode));
        Assert.Equal(18, (await Transactions(client, id)).Count);
        var imported = await Task.WhenAll(answers.Select(async answer => (await answer.Json())["imported"]!.GetValue<int>()));
        Assert.Equal(18, imported.Sum());
    }

    [Theory]
    [InlineData("?year=2025&quarter=Q1", 8)]
    [InlineData("?year=2025&quarter=Q2", 4)]
    [InlineData("?year=2025&quarter=Q3", 3)]
    [InlineData("?year=2025&quarter=Q4", 3)]
    [InlineData("?year=2025", 18)]
    [InlineData("?year=2024", 0)]
    [InlineData("", 18)]
    public async Task TheMovementsAreFilteredByTheQuarterOfTheirBookingDate(string query, int count)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        await client.ImportStatement(id, RepoFiles.Statement);

        var found = await Transactions(client, id, query);

        Assert.Equal(count, found.Count);
    }

    [Theory]
    [InlineData("?quarter=Q1", "quarter")]
    [InlineData("?year=2025&quarter=Q5", "quarter")]
    [InlineData("?year=25x", "year")]
    public async Task AMalformedFilterIsRefusedByName(string query, string field)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();

        var response = await client.GetAsync($"/api/v1/profiles/{id}/transactions{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Json();
        Assert.Equal(InvalidInput, problem["type"]!.GetValue<string>());
        Assert.Equal([field], problem["errors"]!.AsObject().Select(e => e.Key));
    }

    [Fact]
    public async Task AnUnknownProfileIsANotFoundForBothEndpoints()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = Guid.NewGuid().ToString();

        Assert.Equal(HttpStatusCode.NotFound, (await client.ImportStatement(id, RepoFiles.Statement)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/profiles/{id}/transactions")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sabadell")]
    [InlineData("BBVA")]
    public async Task AnUnknownBankIsRefused(string? bank)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        var content = new ByteArrayContent(RepoFiles.Statement);

        var response = await client.PostAsync($"/api/v1/profiles/{id}/bank-statements{(bank is null ? "" : $"?bank={bank}")}", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["bank"], (await response.Json())["errors"]!.AsObject().Select(e => e.Key));
    }

    [Fact]
    public async Task AStatementOverTheLimitIsRefusedAndNothingIsStored()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        var line = "01/02/2025;01/02/2025;RELLENO;-1,00;1,00\n"u8.ToArray();
        var big = new List<byte>(RepoFiles.Statement);
        while (big.Count <= StatementFile.MaxBytes)
        {
            big.AddRange(line);
        }

        var response = await client.ImportStatement(id, [.. big]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("https://gestoria.local/problems/statement-too-large", (await response.Json())["type"]!.GetValue<string>());
        Assert.Empty(await Transactions(client, id));
    }

    [Fact]
    public async Task AStatementWithABadLineIsRefusedWholeNamingTheLine()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        var text = Encoding.UTF8.GetString(RepoFiles.Statement).Replace("-47,16", "-47,1x", StringComparison.Ordinal);

        var response = await client.ImportStatement(id, Encoding.UTF8.GetBytes(text));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Json();
        Assert.Equal(["line 12"], problem["errors"]!.AsObject().Select(e => e.Key));
        Assert.DoesNotContain("SUPERMERCADO", problem.ToJsonString(), StringComparison.Ordinal);
        Assert.Empty(await Transactions(client, id));
    }

    public static TheoryData<byte[]> NotAStatement => new()
    {
        { "PK\u0003\u0004[Content_Types].xml"u8.ToArray() },
        { Encoding.Unicode.GetBytes("Fecha;Fecha Valor;Concepto;Importe;Saldo\n") },
        { [] },
        { "<html><body>not a statement</body></html>"u8.ToArray() },
    };

    [Theory]
    [MemberData(nameof(NotAStatement))]
    public async Task AFileThatIsNotAStatementIsRefused(byte[] file)
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();

        var response = await client.ImportStatement(id, file);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["file"], (await response.Json())["errors"]!.AsObject().Select(e => e.Key));
    }

    [Fact]
    public async Task AWindows1252ExportAndAUtf8OneWithItsMarkReadTheSame()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        const string text = "Fecha;Fecha Valor;Concepto;Importe;Saldo\n09/09/2025;09/09/2025;CAFÉ ÑANDÚ 5 €;-5,00;\n";

        var legacy = await client.ImportStatement(id, Encoding.GetEncoding(1252).GetBytes(text));
        var marked = await client.ImportStatement(id, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]);

        Assert.True(JsonNode.DeepEquals(Imported(1, 1, 0), await legacy.Json()));
        Assert.True(JsonNode.DeepEquals(Imported(1, 0, 1), await marked.Json()));
        Assert.Equal("CAFÉ ÑANDÚ 5 €", (await Transactions(client, id)).Single()!["description"]!.GetValue<string>());
    }

    [Fact]
    public async Task DeletingTheProfileDeletesItsMovements()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        await client.ImportStatement(id, RepoFiles.Statement);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GestoriaDbContext>();

        await db.Profiles.Where(p => p.Id == Guid.Parse(id)).ExecuteDeleteAsync();

        Assert.Equal(0, await db.BankTransactions.CountAsync());
    }

    private static JsonObject Imported(int lines, int imported, int alreadyImported) => new()
    {
        ["bank"] = "bbva",
        ["lines"] = lines,
        ["imported"] = imported,
        ["alreadyImported"] = alreadyImported,
    };

    private static async Task<JsonArray> Transactions(HttpClient client, string id, string query = "") =>
        JsonNode.Parse(await client.GetStringAsync($"/api/v1/profiles/{id}/transactions{query}"))!.AsArray();

    private static async Task<ApiFactory> Api()
    {
        var api = new ApiFactory();
        await api.InitializeAsync();
        return api;
    }
}
