using System.Text.Json.Nodes;
using GestorIA.Infrastructure.TaxYears;

namespace GestorIA.Engine.Tests;

public class TaxYearFilesAreValid
{
    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();

        foreach (var path in TaxYearConfigFiles.All()) { data.Add(path); }

        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void FilePassesSchemaAndCrossFieldRules(string path)
    {
        var failures = TaxYearConfigValidator.Validate(JsonNode.Parse(File.ReadAllText(path)), Path.GetFileName(path));

        Assert.True(failures.Count == 0, $"{Path.GetFileName(path)} is not a valid tax-year file\n\n" + string.Join("\n", failures));
    }

    [Fact]
    public void AtLeastOneConfigFileWasFound() =>
        Assert.NotEmpty(TaxYearConfigFiles.All());
}
