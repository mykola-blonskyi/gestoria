using System.Text.Json.Nodes;

namespace GestorIA.Engine.Tests;

public class TaxYearFilesObeyCrossFieldRules
{
    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var path in TaxYearConfigFiles.All())
        {
            data.Add(path);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void FileObeysCrossFieldRules(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        var failures = TaxYearRules.Check(root, Path.GetFileName(path)).ToList();

        Assert.True(failures.Count == 0, $"{Path.GetFileName(path)} breaks cross-field rules\n\n" + string.Join("\n", failures));
    }
}