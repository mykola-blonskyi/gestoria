using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using GestorIA.Api.Profiles;
using Npgsql;

namespace GestorIA.Api.Tests;

// POST /profiles/restore (SPEC-009 §2.2, #75): an export stored back into an empty installation, the same file again a no-op,
// anything else a 409, and a file this installation cannot store exactly refused whole before anything is written.
public class ProfileRestoreEndpoint(ProfileRestoreEndpoint.DeletedInstallation deleted) : IClassFixture<ProfileRestoreEndpoint.DeletedInstallation>
{
    // The restore's promise, held against the database like the export's and the delete's: every table of the EF Core model
    // holds as many rows after the round trip as before, the answer counts each, and the export after equals the one before.
    // A table added later that the restore does not carry fails here.
    [Fact]
    public async Task AnExportRestoredIntoTheEmptiedInstallationGivesBackEveryTableAndTheSameExport()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await StoredData.Seed(client);
        var tables = await StoredData.RowsPerTable(api);
        Assert.All(tables, table => Assert.True(table.Rows > 0, $"{table.Name} holds no row; seed it in StoredData.Seed."));
        var before = await client.GetStringAsync($"/api/v1/profiles/{id}/export");
        await client.DeleteAsync($"/api/v1/profiles/{id}");

        var response = await client.Restore(before);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/profiles/{id}", response.Headers.Location!.OriginalString);
        Assert.Equal(tables, await StoredData.RowsPerTable(api));
        var answer = await response.Json();
        Assert.Equal(id, answer["profileId"]!.GetValue<string>());
        var entities = answer["entities"]!.AsObject();
        Assert.Equal(tables.Select(table => ProfileExportEndpoint.Member(table.Name)).Order(StringComparer.Ordinal), entities.Select(kind => kind.Key).Order(StringComparer.Ordinal));
        Assert.All(tables, table => Assert.Equal(table.Rows, entities[ProfileExportEndpoint.Member(table.Name)]!.GetValue<int>()));
        AssertSameExport(before, await client.GetStringAsync($"/api/v1/profiles/{id}/export"));
    }

    [Fact]
    public async Task TheSameFileAgainChangesNothingAndAnswersTheSame()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var file = await ExportedAndDeleted(client);
        var first = await (await client.Restore(file)).Json();
        var tables = await StoredData.RowsPerTable(api);
        var id = first["profileId"]!.GetValue<string>();
        var stored = await client.GetStringAsync($"/api/v1/profiles/{id}/export");

        var again = await client.Restore(file);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.True(JsonNode.DeepEquals(first, await again.Json()));
        Assert.Equal(tables, await StoredData.RowsPerTable(api));
        AssertSameExport(stored, await client.GetStringAsync($"/api/v1/profiles/{id}/export"));
    }

    // The unique index on the profile's Singleton column refuses every insert but one; the others then find its rows. The test
    // holds the Profiles table until all four restores wait for it, so they read it empty together and race to insert, rather
    // than one finishing before the next starts.
    [Fact]
    public async Task ConcurrentRestoresOfOneFileStoreItOnce()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var file = await ExportedAndDeleted(client);
        await using var gate = new NpgsqlConnection(api.ConnectionString);
        await gate.OpenAsync();
        await using var held = await gate.BeginTransactionAsync();
        await Execute(gate, "LOCK TABLE \"Profiles\" IN EXCLUSIVE MODE");
        // Another connection, since pg_stat_activity holds still for the length of a transaction.
        await using var watch = new NpgsqlConnection(api.ConnectionString);
        await watch.OpenAsync();

        var restores = Enumerable.Range(0, 4).Select(_ => api.CreateClient().Restore(file)).ToList();
        var waiting = Stopwatch.StartNew();
        while (await Execute(watch, "SELECT count(*)::int FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'") < 4)
        {
            Assert.True(waiting.Elapsed < TimeSpan.FromSeconds(10), "The four restores did not all wait for the Profiles table.");
            await Task.Delay(20);
        }

        await held.CommitAsync();
        var answers = await Task.WhenAll(restores);

        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.Created);
        Assert.Equal(3, answers.Count(answer => answer.StatusCode == HttpStatusCode.OK));
        var movements = JsonNode.Parse(file)!["entities"]!["bankTransactions"]!.AsArray().Count;
        var rows = await StoredData.RowsPerTable(api);
        Assert.Contains(("Profiles", 1), rows);
        Assert.Contains(("BankTransactions", movements), rows);
    }

    [Fact]
    public async Task ADifferentFileIntoAFilledInstallationIsA409NamingWhatIsThereAndChangesNothing()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await StoredData.Seed(client);
        var stored = await client.GetStringAsync($"/api/v1/profiles/{id}/export");
        var movements = JsonNode.Parse(stored)!["entities"]!["bankTransactions"]!.AsArray().Count;
        var other = await OtherInstallationsExport("G16");

        var response = await client.Restore(other);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(Http.ProblemJson, response.MediaType());
        var problem = await response.Json();
        Assert.Equal("https://gestoria.local/problems/installation-not-empty", problem["type"]!.GetValue<string>());
        Assert.Equal(id, problem["profileId"]!.GetValue<string>());
        Assert.Equal(2025, problem["taxYear"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["profiles"] = 1, ["bankTransactions"] = movements }, problem["entities"]), problem.ToJsonString());
        AssertSameExport(stored, await client.GetStringAsync($"/api/v1/profiles/{id}/export"));
    }

    [Fact]
    public async Task AFileWhoseProfileDiffersByOneAmountIsA409()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var id = await client.CreateProfile();
        var stored = await client.GetStringAsync($"/api/v1/profiles/{id}/export");
        var changed = JsonNode.Parse(stored)!;
        changed["entities"]!["profiles"]![0]!["projection"]!["gastos"] = "1200.01";

        var response = await client.Restore(changed.ToJsonString());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("https://gestoria.local/problems/installation-not-empty", (await response.Json())["type"]!.GetValue<string>());
        AssertSameExport(stored, await client.GetStringAsync($"/api/v1/profiles/{id}/export"));
    }

    // The line keys come back with the movements, so the statement they came from adds nothing when imported again.
    [Fact]
    public async Task AfterARestoreImportingTheStatementAgainAddsNothing()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var restored = await client.Restore(await ExportedAndDeleted(client));
        var id = (await restored.Json())["profileId"]!.GetValue<string>();

        var import = await (await client.ImportStatement(id, RepoFiles.Statement)).Json();

        Assert.Equal(0, import["imported"]!.GetValue<int>());
        Assert.Equal(18, import["alreadyImported"]!.GetValue<int>());
    }

    // SPEC-009 §2.1: a known kind missing from a version 1 file reads as zero rows, as in a file exported before #72.
    [Fact]
    public async Task AVersion1FileWithoutBankTransactionsRestoresTheProfileAlone()
    {
        await using var api = await Api();
        var client = api.CreateClient();
        var file = JsonNode.Parse(deleted.Export)!;
        Assert.True(file["entities"]!.AsObject().Remove("bankTransactions"));

        var response = await client.Restore(file.ToJsonString());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["profiles"] = 1, ["bankTransactions"] = 0 }, (await response.Json())["entities"]));
        Assert.Contains(("BankTransactions", 0), await StoredData.RowsPerTable(api));
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "format", "$.format" },
        { "classification", "$.classification" },
        { "unknown kind", "$.entities.invoices" },
        { "unknown field", "$.owner" },
        { "a movement's profileId", "$.entities.bankTransactions[0].profileId" },
        { "no profile", "$.entities.profiles" },
        { "two profiles", "$.entities.profiles" },
        { "a profile amount with three decimals", "$.entities.profiles[0].employment.ingresos" },
        { "a tax year without configuration", "$.entities.profiles[0].taxYear" },
        { "a movement amount with three decimals", "$.entities.bankTransactions[0].amount" },
        { "a balance with three decimals", "$.entities.bankTransactions[0].balance" },
        { "a line break in a description", "$.entities.bankTransactions[0].description" },
        { "a leading space in a description", "$.entities.bankTransactions[0].description" },
        { "line number 0", "$.entities.bankTransactions[0].lineNumber" },
        { "import 0", "$.entities.bankTransactions[0].importSequence" },
        { "import -1", "$.entities.bankTransactions[0].importSequence" },
        { "import int.MaxValue", "$.entities.bankTransactions[0].importSequence" },
        { "a repeated id", "$.entities.bankTransactions[1].id" },
        { "a repeated line of an import", "$.entities.bankTransactions[1].lineNumber" },
        { "a movement stored twice", "$.entities.bankTransactions[18].lineKey" },
        { "a line key with one digit changed", "$.entities.bankTransactions[0].lineKey" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task AFileTheInstallationCannotStoreExactlyIsRefusedWhole(string change, string path)
    {
        var file = JsonNode.Parse(deleted.Export)!.AsObject();
        var movements = file["entities"]!["bankTransactions"]!.AsArray();
        var first = movements[0]!;
        switch (change)
        {
            case "format": file["format"] = "gestoria.backup"; break;
            case "classification": file["classification"] = "public"; break;
            case "unknown kind": file["entities"]!["invoices"] = new JsonArray(); break;
            case "unknown field": file["owner"] = "someone"; break;
            case "a movement's profileId": first["profileId"] = Guid.NewGuid().ToString(); break;
            case "no profile": file["entities"]!["profiles"] = new JsonArray(); break;
            case "two profiles": file["entities"]!["profiles"]!.AsArray().Add(file["entities"]!["profiles"]![0]!.DeepClone()); break;
            case "a profile amount with three decimals": file["entities"]!["profiles"]![0]!["employment"]!["ingresos"] = "1.234"; break;
            case "a tax year without configuration": file["entities"]!["profiles"]![0]!["taxYear"] = 1999; break;
            case "a movement amount with three decimals": first["amount"] = "12.345"; break;
            case "a balance with three decimals": first["balance"] = "12.345"; break;
            case "a line break in a description": first["description"] = first["description"]!.GetValue<string>() + "\nX"; break;
            case "a leading space in a description": first["description"] = " " + first["description"]!.GetValue<string>(); break;
            case "line number 0": first["lineNumber"] = 0; break;
            case "import 0": first["importSequence"] = 0; break;
            case "import -1": first["importSequence"] = -1; break;
            case "import int.MaxValue": first["importSequence"] = int.MaxValue; break;
            case "a repeated id": movements[1]!["id"] = first["id"]!.DeepClone(); break;
            case "a repeated line of an import":
                movements[1]!["importSequence"] = first["importSequence"]!.DeepClone();
                movements[1]!["lineNumber"] = first["lineNumber"]!.DeepClone();
                break;
            case "a movement stored twice":
                var twin = first.DeepClone();
                twin["id"] = Guid.NewGuid().ToString();
                twin["lineNumber"] = movements.Max(movement => movement!["lineNumber"]!.GetValue<int>()) + 1;
                movements.Add(twin);
                break;
            case "a line key with one digit changed":
                var key = first["lineKey"]!.GetValue<string>();
                first["lineKey"] = (key[0] == '0' ? "1" : "0") + key[1..];
                break;
        }

        await AssertRefused(file.ToJsonString(), path);
    }

    // A key moved to another movement is a key that no longer fits either.
    [Fact]
    public async Task TwoMovementsWithTheirLineKeysSwappedAreBothRefused()
    {
        var file = JsonNode.Parse(deleted.Export)!;
        var movements = file["entities"]!["bankTransactions"]!.AsArray();
        (movements[0]!["lineKey"], movements[1]!["lineKey"]) = (movements[1]!["lineKey"]!.DeepClone(), movements[0]!["lineKey"]!.DeepClone());

        var errors = await AssertRefused(file.ToJsonString(), "$.entities.bankTransactions[0].lineKey");

        Assert.Equal(["$.entities.bankTransactions[0].lineKey", "$.entities.bankTransactions[1].lineKey"], errors.Order(StringComparer.Ordinal));
    }

    // SPEC-009 §2.1: a file from a newer GestorIA is refused for its version, not for the first thing that version adds.
    [Fact]
    public async Task AVersion2FileIsRefusedForItsVersionAlone()
    {
        var file = JsonNode.Parse(deleted.Export)!;
        file["formatVersion"] = 2;
        file["entities"]!["invoices"] = new JsonArray();
        file["receipts"] = new JsonArray();

        var errors = await AssertRefused(file.ToJsonString(), "$.formatVersion");

        Assert.Equal(["$.formatVersion"], errors);
    }

    [Theory]
    [InlineData("{", "$")]
    [InlineData("[]", "$")]
    [InlineData("\"gestoria.export\"", "$")]
    public async Task WhatIsNotAnExportObjectIsRefused(string body, string path)
    {
        await AssertRefused(body, path);
    }

    [Fact]
    public async Task AVersionWrittenAsTextIsRefused()
    {
        var file = JsonNode.Parse(deleted.Export)!;
        file["formatVersion"] = "1";

        Assert.Equal(["$.formatVersion"], await AssertRefused(file.ToJsonString(), "$.formatVersion"));
    }

    // Every refused movement is named up to twenty, as a refused statement's lines are, and then counted.
    [Fact]
    public async Task ManyRefusedMovementsAreListedUpToTwentyAndCounted()
    {
        var file = JsonNode.Parse(deleted.Export)!;
        var movements = file["entities"]!["bankTransactions"]!.AsArray();
        var template = movements[0]!;
        for (var i = 0; i < 25; i++)
        {
            var copy = template.DeepClone();
            copy["id"] = Guid.NewGuid().ToString();
            copy["lineNumber"] = 1000 + i;
            copy["amount"] = "1.234";
            movements.Add(copy);
        }

        var errors = await AssertRefused(file.ToJsonString(), "$.entities.bankTransactions");

        Assert.Equal(21, errors.Count);
    }

    [Fact]
    public async Task AnExportSentAsSomethingOtherThanJsonIsA415()
    {
        var response = await deleted.Api.CreateClient().Restore(deleted.Export, "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("https://gestoria.local/problems/export-media-type", (await response.Json())["type"]!.GetValue<string>());
        await AssertEmpty();
    }

    [Fact]
    public async Task AnExportOverTheLimitIsA413()
    {
        var response = await deleted.Api.CreateClient().Restore($"\"{new string('x', ProfileRestore.MaxBytes)}\"");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("https://gestoria.local/problems/export-too-large", (await response.Json())["type"]!.GetValue<string>());
        await AssertEmpty();
    }

    // The refusal is invalid-input keyed at the path, names no movement's description, and leaves every table empty.
    private async Task<List<string>> AssertRefused(string body, string path)
    {
        var response = await deleted.Api.CreateClient().Restore(body);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = JsonNode.Parse(text)!;
        Assert.Equal("https://gestoria.local/problems/invalid-input", problem["type"]!.GetValue<string>());
        var errors = problem["errors"]!.AsObject().Select(error => error.Key).ToList();
        Assert.Contains(path, errors);
        foreach (var movement in JsonNode.Parse(deleted.Export)!["entities"]!["bankTransactions"]!.AsArray())
        {
            Assert.DoesNotContain(movement!["description"]!.GetValue<string>(), text, StringComparison.Ordinal);
        }

        await AssertEmpty();
        return errors;
    }

    private static async Task<int> Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync() is int count ? count : 0;
    }

    private async Task AssertEmpty() =>
        Assert.All(await StoredData.RowsPerTable(deleted.Api), table => Assert.True(table.Rows == 0, $"{table.Name} holds {table.Rows} row(s)."));

    // Two exports hold the same data when they are equal but for the moment each was made.
    private static void AssertSameExport(string expected, string actual)
    {
        var (before, after) = (JsonNode.Parse(expected)!.AsObject(), JsonNode.Parse(actual)!.AsObject());
        before.Remove("exportedAt");
        after.Remove("exportedAt");
        Assert.True(JsonNode.DeepEquals(before, after), after.ToJsonString());
    }

    private static async Task<string> ExportedAndDeleted(HttpClient client)
    {
        var id = await StoredData.Seed(client);
        var export = await client.GetStringAsync($"/api/v1/profiles/{id}/export");
        (await client.DeleteAsync($"/api/v1/profiles/{id}")).EnsureSuccessStatusCode();
        return export;
    }

    // An export made by another installation, from a golden's profile.
    private static async Task<string> OtherInstallationsExport(string golden)
    {
        await using var other = await Api();
        var client = other.CreateClient();
        var id = await client.CreateProfile(golden);
        return await client.GetStringAsync($"/api/v1/profiles/{id}/export");
    }

    private static async Task<ApiFactory> Api()
    {
        var api = new ApiFactory();
        await api.InitializeAsync();
        return api;
    }

    // An installation whose data was exported and then deleted: empty again, with the file that held it. The refusals share
    // it, since none of them may write anything.
    public sealed class DeletedInstallation : IAsyncLifetime
    {
        internal ApiFactory Api { get; } = new();

        internal string Export { get; private set; } = "";

        public async Task InitializeAsync()
        {
            await Api.InitializeAsync();
            Export = await ExportedAndDeleted(Api.CreateClient());
        }

        public async Task DisposeAsync() => await Api.DisposeAsync();
    }
}
