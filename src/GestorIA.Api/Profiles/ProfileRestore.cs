using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GestorIA.Domain.Interfaces;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;
using GestorIA.Infrastructure.Profiles;
using GestorIA.Infrastructure.TaxYears;
using GestorIA.Infrastructure.Transactions;
using Microsoft.Net.Http.Headers;
using static System.FormattableString;

namespace GestorIA.Api.Profiles;

// The boundary between an export file (SPEC-009 §2.1) and the rows a restore stores (§2.2, #75). The whole file is checked
// before anything is written: a version, a kind or a field this installation does not know refuses the file rather than
// restore part of it, and every refused value is named at once by its JSON path. No message quotes a movement's description.
public static class ProfileRestore
{
    // A movement is about 250 bytes of JSON, so this holds some 60,000 of them: decades of a personal account.
    public const int MaxBytes = 16 * 1024 * 1024;

    // As many movements, or statement imports, as a refused file lists, like a refused statement's lines (BbvaCsvStatementParser).
    private const int MaxRowErrors = 20;

    private const string Movements = "$.entities.bankTransactions";

    private const string Imports = "$.entities.statementImports";

    private static readonly Regex Signed = new(ProfileAmounts.Signed, RegexOptions.CultureInvariant);

    // A member written twice would otherwise be read as one of its two values, and the other dropped.
    private static readonly JsonDocumentOptions Document = new() { AllowDuplicateProperties = false };

    // The API's own options, refusing any member the export's types do not have, and a member given twice, which the API's
    // case-insensitive names would read as one. A ConditionalWeakTable keeps one derived copy per options object, for as long
    // as that object lives.
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> Strict = new();

    public static bool IsJson(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        && parsed.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);

    // today bounds the statements' periods as it bounds an import's (StatementPeriod.Refusals).
    public static Restored Read(byte[] body, JsonSerializerOptions apiOptions, TaxYearConfigLoader loader, DateOnly today)
    {
        ProfileExport export;
        try
        {
            export = Parse(body, apiOptions);
        }
        catch (InvalidOperationException)
        {
            // What System.Text.Json throws when a string it reads holds half of a character, such as a lone "\ud800": the
            // escape is valid JSON, the text it stands for is not. Any string of the file may hold one, the format included.
            throw Refused("$", "The file holds text that is not whole Unicode characters.");
        }

        return Check(export, loader, today);
    }

    private static ProfileExport Parse(byte[] body, JsonSerializerOptions apiOptions)
    {
        var options = Strict.GetValue(apiOptions, api => new JsonSerializerOptions(api)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
        });

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body, documentOptions: Document);
        }
        catch (JsonException e)
        {
            throw Refused(e.Path ?? "$", $"The file is not JSON: {e.Message}");
        }

        if (node is not JsonObject root)
        {
            throw Refused("$", "The file is not a GestorIA export: it must be a JSON object.");
        }

        // The format and its version first, so a file from a newer GestorIA is refused for its version and not for the first
        // field that version adds.
        if (root["format"] is not JsonValue format || !format.TryGetValue<string>(out var name) || name != ProfileExport.FormatName)
        {
            throw Refused("$.format", $"The file is not a GestorIA export: $.format must be \"{ProfileExport.FormatName}\".");
        }

        if (root["formatVersion"] is not JsonValue version || version.GetValueKind() != JsonValueKind.Number)
        {
            throw Refused("$.formatVersion", Invariant($"$.formatVersion must be the number of the export's format version; this installation reads version {ProfileExport.CurrentVersion}."));
        }

        if (!version.TryGetValue<int>(out var number) || number != ProfileExport.CurrentVersion)
        {
            throw Refused("$.formatVersion", Invariant($"$.formatVersion is {version.ToJsonString()}, a version this installation does not read; it reads version {ProfileExport.CurrentVersion}."));
        }

        if (root["entities"] is not JsonObject entities)
        {
            throw Refused("$.entities", "$.entities must be an object holding the export's entity kinds.");
        }

        var kinds = options.GetTypeInfo(typeof(ExportedEntities)).Properties.Select(property => property.Name).ToList();
        var unknown = entities.Select(kind => kind.Key).Where(kind => !kinds.Contains(kind, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidExportException(unknown.ToDictionary(
                kind => $"$.entities.{kind}",
                kind => new[] { $"This installation does not know {kind}; a restore never takes part of a file (SPEC-009 §2.1)." }));
        }

        // A kind the file was exported without had no rows then (SPEC-009 §2.1).
        foreach (var kind in kinds.Where(kind => !entities.ContainsKey(kind)))
        {
            entities[kind] = new JsonArray();
        }

        RefuseClassesNotNamedExactly(entities);

        ProfileExport export;
        try
        {
            export = root.Deserialize<ProfileExport>(options)!;
        }
        catch (JsonException e)
        {
            var path = e.Path ?? "$";
            throw Refused(path, $"{path}: {e.Message}");
        }
        catch (NotSupportedException e)
        {
            // A union without its "kind" (ProfileInput.ReadAsync).
            throw Refused("$", e.Message);
        }

        return export;
    }

    private static Restored Check(ProfileExport export, TaxYearConfigLoader loader, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();
        if (export.Classification != ProfileExport.PersonalFinancialData)
        {
            errors["$.classification"] = [$"$.classification must be \"{ProfileExport.PersonalFinancialData}\", the label of what the file holds."];
        }

        ProfileRow? profile = null;
        // A Guid.Empty id would be replaced by one EF Core generates, so what is stored would no longer be what the file holds,
        // and restoring the same file again would find other data.
        const string EmptyId = "is 00000000-0000-0000-0000-000000000000, which no stored row has";
        if (export.Entities.Profiles is [null])
        {
            errors["$.entities.profiles[0]"] = ["$.entities.profiles[0] is null; it must be the profile."];
        }
        else if (export.Entities.Profiles is [{ Id: var empty }] && empty == Guid.Empty)
        {
            errors["$.entities.profiles[0].id"] = [$"$.entities.profiles[0].id {EmptyId}."];
        }
        else if (export.Entities.Profiles is [var view])
        {
            try
            {
                profile = ProfileRow.From(ProfileInput.Parse(new ProfileInputDocument(view.TaxYear, view.Region, view.Employment, view.Activity, view.Projection), loader, "$.entities.profiles[0]"));
                profile.Id = view.Id;
            }
            catch (InvalidProfileException e)
            {
                foreach (var (path, reasons) in e.Errors)
                {
                    errors[path] = reasons;
                }
            }
        }
        else
        {
            errors["$.entities.profiles"] = [Invariant($"$.entities.profiles holds {export.Entities.Profiles.Count} profiles; an installation keeps exactly one.")];
        }

        var (movements, imports) = (export.Entities.BankTransactions, export.Entities.StatementImports);
        var refused = new SortedDictionary<int, List<(string? Field, string Reason)>>();
        var refusedImports = new SortedDictionary<int, List<(string? Field, string Reason)>>();
        void Refuse(int index, string? field, string reason) => Add(refused, index, field, reason);
        void RefuseImport(int index, string? field, string reason) => Add(refusedImports, index, field, reason);
        var sequences = imports.OfType<ExportedStatementImport>().Select(import => import.Sequence).ToHashSet();

        var rows = new BankTransactionRow?[movements.Count];
        var ids = new Dictionary<Guid, int>();
        var places = new Dictionary<(int ImportSequence, int LineNumber), int>();
        for (var i = 0; i < movements.Count; i++)
        {
            if (movements[i] is not { } movement)
            {
                Refuse(i, null, "is null; it must be a movement");
                continue;
            }

            if (movement.Id == Guid.Empty)
            {
                Refuse(i, "id", EmptyId);
            }

            var amount = Euros(movement.Amount);
            if (amount is null)
            {
                Refuse(i, "amount", "is not an amount in euros with at most two decimals, like \"-12.50\"");
            }

            decimal? balance = null;
            if (movement.Balance is not null && (balance = Euros(movement.Balance)) is null)
            {
                Refuse(i, "balance", "is not an amount in euros with at most two decimals, like \"1987.50\"");
            }

            var plainDescription = StatementDescription.IsValid(movement.Description);
            if (!plainDescription)
            {
                Refuse(i, "description", $"must be text that {StatementDescription.Rule}, as every imported statement line is");
            }

            if (movement.LineNumber < 1)
            {
                Refuse(i, "lineNumber", Invariant($"is {movement.LineNumber}; it must be 1 or more"));
            }

            // A file without statement imports was exported before #88, when only an import that stored a line kept its number,
            // so its movements came from as many imports at most. The bound also keeps the next import's number, one more than
            // the highest, from overflowing; the imports' own bound does that for a later file.
            if (imports.Count == 0 && (movement.ImportSequence < 1 || movement.ImportSequence > movements.Count))
            {
                Refuse(i, "importSequence", Invariant($"is {movement.ImportSequence}; it must be from 1 to {movements.Count}, the number of movements in the file"));
            }
            else if (imports.Count > 0 && !sequences.Contains(movement.ImportSequence))
            {
                Refuse(i, "importSequence", Invariant($"is {movement.ImportSequence}, the sequence of no statement import in {Imports}"));
            }

            if (!ids.TryAdd(movement.Id, i))
            {
                Refuse(i, "id", Invariant($"is the id of {Movements}[{ids[movement.Id]}] too"));
            }

            if (!places.TryAdd((movement.ImportSequence, movement.LineNumber), i))
            {
                Refuse(i, "lineNumber", Invariant($"is line {movement.LineNumber} of import {movement.ImportSequence}, as {Movements}[{places[(movement.ImportSequence, movement.LineNumber)]}] is"));
            }

            if (amount is { } money && (movement.Balance is null || balance is not null) && plainDescription)
            {
                rows[i] = new BankTransactionRow
                {
                    Id = movement.Id,
                    LineKey = movement.LineKey,
                    ImportSequence = movement.ImportSequence,
                    LineNumber = movement.LineNumber,
                    BookingDate = movement.BookingDate,
                    ValueDate = movement.ValueDate,
                    Description = movement.Description,
                    Amount = money,
                    Balance = balance,
                    Class = movement.Class,
                };
            }
        }

        // An import stores every occurrence of a line it has not stored yet, and a file that holds a line's k-th occurrence
        // holds the first k, so the movements printing the same line always carry the keys of occurrences 1 to n. A key that
        // does not fit was changed by hand, and storing it would let a later import store the line twice, or never.
        var printed = Enumerable.Range(0, movements.Count)
            .Where(i => rows[i] is not null)
            .GroupBy(i => (rows[i]!.BookingDate, rows[i]!.ValueDate, rows[i]!.Amount, rows[i]!.Description));
        foreach (var group in printed)
        {
            var line = rows[group.First()]!.ToBankTransaction();
            var expected = LineKeys.Of(Enumerable.Repeat(new StatementLine(1, line), group.Count())).Select(key => key.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var i in group)
            {
                if (!expected.Remove(rows[i]!.LineKey))
                {
                    Refuse(i, "lineKey", "does not match the movement it belongs to");
                }
            }
        }

        // Each import's sequence is one of 1 to the number of imports, and no two share one, so they are exactly those numbers.
        // Its period passes the rule an import's does. Its statement's movements are the ones it stored and every movement of
        // the file inside its period: an import stores only what an earlier one did not, and the file holds what each earlier
        // one stored. A period that leaves one out, or runs far past them or into days still to come, would count days as
        // covered that no statement holds.
        var datesOf = movements.OfType<ExportedBankTransaction>().ToLookup(movement => movement.ImportSequence, movement => movement.BookingDate);
        var allDates = movements.OfType<ExportedBankTransaction>().Select(movement => movement.BookingDate).Order().ToArray();
        var importRows = new StatementImportRow?[imports.Count];
        var bySequence = new Dictionary<int, int>();
        for (var i = 0; i < imports.Count; i++)
        {
            if (imports[i] is not { } import)
            {
                RefuseImport(i, null, "is null; it must be a statement import");
                continue;
            }

            if (import.Sequence < 1 || import.Sequence > imports.Count)
            {
                RefuseImport(i, "sequence", Invariant($"is {import.Sequence}; it must be from 1 to {imports.Count}, the number of statement imports in the file"));
            }
            else if (!bySequence.TryAdd(import.Sequence, i))
            {
                RefuseImport(i, "sequence", Invariant($"is the sequence of {Imports}[{bySequence[import.Sequence]}] too"));
            }

            var dates = datesOf[import.Sequence].Concat(Within(allDates, import.From, import.To)).ToList();
            var refusals = StatementPeriod.Refusals(import.From, import.To, dates.Count == 0 ? null : dates.Min(), dates.Count == 0 ? null : dates.Max(), today);
            foreach (var (end, reason) in refusals)
            {
                RefuseImport(i, end, reason);
            }

            importRows[i] = new StatementImportRow { Sequence = import.Sequence, From = import.From, To = import.To };
        }

        Report(Imports, "statement imports", refusedImports, errors);
        Report(Movements, "movements", refused, errors);

        if (errors.Count > 0)
        {
            throw new InvalidExportException(errors);
        }

        var stored = rows.Select(row => row!).ToList();
        // A file without statement imports, as every file exported before #88, gets one per import its movements name, over
        // the first to the last booking date of what that import stored: the days #73 read from the lines. They are numbered
        // 1 to n in their order and the movements follow, since a file of an installation before #88 may skip a number and
        // the imports' numbers are exactly 1 to n from then on.
        var statements = imports.Count > 0
            ? [.. importRows.Select(row => row!)]
            : stored.GroupBy(row => row.ImportSequence)
                .OrderBy(import => import.Key)
                .Select((import, index) =>
                {
                    foreach (var row in import)
                    {
                        row.ImportSequence = index + 1;
                    }

                    return new StatementImportRow { Sequence = index + 1, From = import.Min(row => row.BookingDate), To = import.Max(row => row.BookingDate) };
                })
                .ToList();
        foreach (var row in stored)
        {
            row.ProfileId = profile!.Id;
        }

        foreach (var statement in statements)
        {
            statement.ProfileId = profile!.Id;
        }

        return new Restored(profile!, statements, stored, ProfileExport.Of(ProfileView.From(profile!.Id, profile.ToProfile()), statements, stored, DateTimeOffset.UnixEpoch).Entities);
    }

    // The dates of sorted between from and to, both included.
    private static ArraySegment<DateOnly> Within(DateOnly[] sorted, DateOnly from, DateOnly to)
    {
        var (start, end) = (FirstWhere(sorted, date => date >= from), FirstWhere(sorted, date => date > to));
        return start < end ? new ArraySegment<DateOnly>(sorted, start, end - start) : [];
    }

    // The index of the first date the test holds for, which holds for every later one too; the length when there is none.
    private static int FirstWhere(DateOnly[] sorted, Func<DateOnly, bool> test)
    {
        var (low, high) = (0, sorted.Length);
        while (low < high)
        {
            var middle = (low + high) / 2;
            (low, high) = test(sorted[middle]) ? (low, middle) : (middle + 1, high);
        }

        return low;
    }

    private static void Add(SortedDictionary<int, List<(string? Field, string Reason)>> refused, int index, string? field, string reason)
    {
        if (!refused.TryGetValue(index, out var reasons))
        {
            refused[index] = reasons = [];
        }

        reasons.Add((field, reason));
    }

    // The refused rows of a kind by their paths, the first MaxRowErrors of them.
    private static void Report(string kind, string rows, SortedDictionary<int, List<(string? Field, string Reason)>> refused, Dictionary<string, string[]> errors)
    {
        foreach (var (index, reasons) in refused.Take(MaxRowErrors))
        {
            foreach (var field in reasons.GroupBy(reason => reason.Field, reason => reason.Reason))
            {
                var path = field.Key is null ? Invariant($"{kind}[{index}]") : Invariant($"{kind}[{index}].{field.Key}");
                errors[path] = [.. field.Select(reason => $"{path} {reason}.")];
            }
        }

        if (refused.Count > MaxRowErrors)
        {
            errors[kind] = [Invariant($"{refused.Count} {rows} are refused; the first {MaxRowErrors} are listed.")];
        }
    }

    // A movement's class (#73) as the export writes it: null, or one of the names ClassifyInput takes. The API's enum converter
    // would also read a number, a member's C# name or a comma-joined list, so the text is checked before it runs. The API reads
    // member names case-insensitively, so "Class" is the same member.
    private static void RefuseClassesNotNamedExactly(JsonObject entities)
    {
        if (entities["bankTransactions"] is not JsonArray movements)
        {
            return;
        }

        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < movements.Count; i++)
        {
            if (movements[i] is not JsonObject movement)
            {
                continue;
            }

            foreach (var (member, value) in movement.Where(member => member.Key.Equals("class", StringComparison.OrdinalIgnoreCase)))
            {
                if (value is null || (value is JsonValue text && text.TryGetValue<string>(out var name) && TransactionClassNames.TryParse(name, out _)))
                {
                    continue;
                }

                var path = Invariant($"{Movements}[{i}].{member}");
                errors[path] = [$"{path} must be null, while the movement has no class, or one of {string.Join(", ", TransactionClassNames.All)}."];
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidExportException(errors);
        }
    }

    // The rule the API holds typed amounts to (ProfileInput): the pattern, and TryParse because .NET's $ also matches before
    // a final "\n".
    private static decimal? Euros(string text) =>
        Signed.IsMatch(text) && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;

    private static InvalidExportException Refused(string path, string reason) => new(new() { [path] = [reason] });
}

// What a valid file stores, and what it holds as the export would write it, to compare with what an installation holds.
public sealed record Restored(ProfileRow Profile, IReadOnlyList<StatementImportRow> StatementImports, IReadOnlyList<BankTransactionRow> BankTransactions, ExportedEntities Entities);

// The answer to a restore: the restored profile's id and how many rows each kind holds, one member per table, named as the
// export's entities are.
public sealed record RestoredExport(Guid ProfileId, EntityCounts Entities);

public sealed record EntityCounts(int Profiles, int StatementImports, int BankTransactions)
{
    public static EntityCounts Of(ExportedEntities entities) => new(entities.Profiles.Count, entities.StatementImports.Count, entities.BankTransactions.Count);
}

public sealed class InvalidExportException(Dictionary<string, string[]> errors) : Exception("The export is not valid.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
