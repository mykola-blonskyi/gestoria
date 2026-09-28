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

    // As many movements as a refused file lists, like a refused statement's lines (BbvaCsvStatementParser).
    private const int MaxMovementErrors = 20;

    private const string Movements = "$.entities.bankTransactions";

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

    public static Restored Read(byte[] body, JsonSerializerOptions apiOptions, TaxYearConfigLoader loader)
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

        return Check(export, loader);
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

    private static Restored Check(ProfileExport export, TaxYearConfigLoader loader)
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

        var movements = export.Entities.BankTransactions;
        var refused = new SortedDictionary<int, List<(string? Field, string Reason)>>();
        void Refuse(int index, string? field, string reason)
        {
            if (!refused.TryGetValue(index, out var reasons))
            {
                refused[index] = reasons = [];
            }

            reasons.Add((field, reason));
        }

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

            // An import that stores anything stores at least one line, so the file's movements came from as many imports at
            // most. The bound also keeps the next import's number, one more than the highest, from overflowing.
            if (movement.ImportSequence < 1 || movement.ImportSequence > movements.Count)
            {
                Refuse(i, "importSequence", Invariant($"is {movement.ImportSequence}; it must be from 1 to {movements.Count}, the number of movements in the file"));
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

        foreach (var (index, reasons) in refused.Take(MaxMovementErrors))
        {
            foreach (var field in reasons.GroupBy(reason => reason.Field, reason => reason.Reason))
            {
                var path = field.Key is null ? Invariant($"{Movements}[{index}]") : Invariant($"{Movements}[{index}].{field.Key}");
                errors[path] = [.. field.Select(reason => $"{path} {reason}.")];
            }
        }

        if (refused.Count > MaxMovementErrors)
        {
            errors[Movements] = [Invariant($"{refused.Count} movements are refused; the first {MaxMovementErrors} are listed.")];
        }

        if (errors.Count > 0)
        {
            throw new InvalidExportException(errors);
        }

        var stored = rows.Select(row => row!).ToList();
        foreach (var row in stored)
        {
            row.ProfileId = profile!.Id;
        }

        var ordered = stored.OrderBy(t => t.BookingDate).ThenBy(t => t.ImportSequence).ThenBy(t => t.LineNumber);
        return new Restored(profile!, stored, ProfileExport.Of(ProfileView.From(profile!.Id, profile.ToProfile()), ordered, DateTimeOffset.UnixEpoch).Entities);
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
public sealed record Restored(ProfileRow Profile, IReadOnlyList<BankTransactionRow> BankTransactions, ExportedEntities Entities);

// The answer to a restore: the restored profile's id and how many rows each kind holds, one member per table, named as the
// export's entities are.
public sealed record RestoredExport(Guid ProfileId, EntityCounts Entities);

public sealed record EntityCounts(int Profiles, int BankTransactions)
{
    public static EntityCounts Of(ExportedEntities entities) => new(entities.Profiles.Count, entities.BankTransactions.Count);
}

public sealed class InvalidExportException(Dictionary<string, string[]> errors) : Exception("The export is not valid.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
