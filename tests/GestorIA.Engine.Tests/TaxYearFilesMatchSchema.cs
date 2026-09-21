using Json.Schema;
using System.Text.Json;

namespace GestorIA.Engine.Tests;

public class TaxYearFilesMatchSchema
{
    private static readonly EvaluationOptions Options = new() { OutputFormat = OutputFormat.List };

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();

        foreach (var path in TaxYearConfigFiles.All()) { data.Add(path); }

        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void FileMatchesSchema(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var results = TaxYearConfigFiles.Schema().Evaluate(doc.RootElement, Options);

        Assert.True(results.IsValid, $"{Path.GetFileName(path)} failed schema.json\n\n"
            + string.Join("\n", results.Describe()));
    }

    [Fact]
    public void AtLeastOneConfigFileWasFound() =>
        Assert.NotEmpty(TaxYearConfigFiles.All());
}