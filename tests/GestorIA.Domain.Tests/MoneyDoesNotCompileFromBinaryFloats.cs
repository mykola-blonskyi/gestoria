using System.Collections.Immutable;
using GestorIA.Domain.ValueObjects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace GestorIA.Domain.Tests;

public class MoneyDoesNotCompileFromBinaryFloats
{
    [Theory]
    [InlineData("new GestorIA.Domain.ValueObjects.Money(1.5);", "CS1503")]
    [InlineData("new GestorIA.Domain.ValueObjects.Money(1.5f);", "CS1503")]
    [InlineData("GestorIA.Domain.ValueObjects.Money m = 1.5;", "CS0029")]
    [InlineData("GestorIA.Domain.ValueObjects.Money m = 1.5f;", "CS0029")]
    public void ConstructingOrAssigningFromABinaryFloatFailsToCompile(string statement, string expectedDiagnosticId)
    {
        var errors = Compile(statement);

        // A specific diagnostic ID, not just "some error", so an unrelated compile
        // error (e.g. a typo introduced later) can't make this test pass for the
        // wrong reason. CS1503 is a bad argument conversion (the constructor case),
        // CS0029 is a bad implicit assignment conversion (the "= 1.5" case) -- the
        // one an implicit operator Money(double) would silently make disappear.
        Assert.Contains(errors, d => d.Id == expectedDiagnosticId);
    }

    [Fact]
    public void ConstructingFromADecimalCompiles()
    {
        var errors = Compile("new GestorIA.Domain.ValueObjects.Money(1.5m);");
        Assert.Empty(errors);
    }

    [Fact]
    public void AssigningFromAConstructedMoneyCompiles()
    {
        // Money has no implicit conversion from decimal either, only from itself
        // -- this is the assignment-shape equivalent of ConstructingFromADecimalCompiles.
        var errors = Compile("GestorIA.Domain.ValueObjects.Money m = new GestorIA.Domain.ValueObjects.Money(1.5m);");
        Assert.Empty(errors);
    }

    private static ImmutableArray<Diagnostic> Compile(string statement)
    {
        // $$"""...""" is a raw string literal with the interpolation marker changed
        // to {{ }} (because $$ doubles it), so the embedded C# source below can use
        // plain single braces for its own blocks without escaping them.
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
