namespace GestorIA.Api.Profiles;

// Everything the installation stores about a profile, in one file the user keeps outside the machine (SPEC-013, #74), and
// what #75's restore reads back. SPEC-009 §2.1 documents the format. Entities is a map of entity kinds, each a list, so a new
// kind is a new member and not a new format.
public sealed record ProfileExport(string Format, int FormatVersion, string Classification, DateTimeOffset ExportedAt, ExportedEntities Entities)
{
    public const string FormatName = "gestoria.export";

    // Raised when a change would make an existing export read differently: a kind or a field removed, renamed or changed in
    // meaning. Adding a kind, or an optional field to one, keeps the version.
    public const int CurrentVersion = 1;

    // The file is labelled as what it holds, so it is recognised as such wherever it ends up.
    public const string PersonalFinancialData = "personal-financial-data";

    public static ProfileExport Of(ProfileView profile, DateTimeOffset exportedAt) =>
        new(FormatName, CurrentVersion, PersonalFinancialData, exportedAt, new ExportedEntities([profile]));
}

// One member per stored entity kind, each holding the rows that belong to the exported profile.
public sealed record ExportedEntities(IReadOnlyList<ProfileView> Profiles);
