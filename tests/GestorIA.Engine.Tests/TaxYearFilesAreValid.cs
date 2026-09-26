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
    public void FileLoadsAsTheLoaderWouldReadIt(string path)
    {
        var error = Record.Exception(() => TaxYearConfigParser.Parse(File.ReadAllBytes(path), Path.GetFileName(path)));

        Assert.True(error is null, $"{Path.GetFileName(path)} does not load\n\n{error?.Message}");
    }

    [Fact]
    public void AtLeastOneConfigFileWasFound() =>
        Assert.NotEmpty(TaxYearConfigFiles.All());
}
