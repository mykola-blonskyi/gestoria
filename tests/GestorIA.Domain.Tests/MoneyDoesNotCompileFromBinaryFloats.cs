using System.Collections.Immutable;
using GestorIA.Domain.ValueObjects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace GestorIA.Domain.Tests;

public class MoneyDoesNotCompileFromBinaryFloats
{
    [Theory]
    [InlineData("new GestorIA.Domain.ValueObjects.Money(1.5);")]
    [InlineData("new GestorIA.Domain.ValueObjects.Money(1.5f);")]
    public void ConstructingFromABinaryFloatFailsToCompile(string statement)
    {
        var errors = Compile(statement);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ConstructingFromADecimalCompiles()
    {
        var errors = Compile("new GestorIA.Domain.ValueObjects.Money(1.5m);");
        Assert.Empty(errors);
    }

    private static ImmutableArray<Diagnostic> Compile(string statement)
    {
        var source = $$"""
            class Probe
            {
                void Use()
                {
                    {{statement}}
                }
            }
            """;

        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

        var referencePaths = trustedAssemblies
            .Append(typeof(Money).Assembly.Location)
            .Distinct();

        var references = referencePaths.Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }
}
