using System.Text.Json.Nodes;

namespace GestorIA.Infrastructure.TaxYears;

internal static class JsonValues
{
    // The schema admits values C# cannot hold: 12.0 where an int is read, 1e40 where a decimal is. That is the file's
    // fault, so it becomes an ArgumentException the parser reports as an invalid file, unlike a defect in this code.
    internal static T Read<T>(this JsonNode node)
    {
        try
        {
            return node.GetValue<T>();
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            throw new ArgumentException($"{node.GetPath()} is {node.ToJsonString()}, which cannot be read as {typeof(T).Name}.", e);
        }
    }
}
