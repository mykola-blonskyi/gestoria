using System.Reflection;
using System.Text.Json.Serialization;

namespace GestorIA.Domain.Models;

// What a bank line is (SPEC-004 §2), the classes v1 uses. Unclear is not a member: a line is unclear when nothing classifies
// it. HomeExpense waits for the home-office share, which nothing models yet.
public enum TransactionClass
{
    [JsonStringEnumMemberName("activityIncome")]
    ActivityIncome,
    [JsonStringEnumMemberName("deductibleExpense")]
    DeductibleExpense,
    [JsonStringEnumMemberName("socialSecurity")]
    SocialSecurity,
    [JsonStringEnumMemberName("aeatPayment")]
    AeatPayment,
    [JsonStringEnumMemberName("employmentIncome")]
    EmploymentIncome,
    [JsonStringEnumMemberName("savingsIncome")]
    SavingsIncome,
    [JsonStringEnumMemberName("ownTransfer")]
    OwnTransfer,
    [JsonStringEnumMemberName("personal")]
    Personal,
}

// The names above as the API, the rules file and the trace write them. Read from the attributes, so there is one list. The
// serializer's own enum reader is not used for input: it also takes numbers and comma-joined names ("1", "personal, aeatPayment").
public static class TransactionClassNames
{
    private static readonly IReadOnlyDictionary<TransactionClass, string> Names = Enum.GetValues<TransactionClass>().ToDictionary(
        value => value,
        value => typeof(TransactionClass).GetField(value.ToString())!.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()!.Name);

    private static readonly IReadOnlyDictionary<string, TransactionClass> ByName = Names.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public static IEnumerable<string> All => Names.Values;

    public static string Name(this TransactionClass value) => Names[value];

    public static bool TryParse(string name, out TransactionClass value) => ByName.TryGetValue(name, out value);
}
