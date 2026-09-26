using System.Collections.Immutable;
using GestorIA.Domain.ValueObjects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GestorIA.Engine.Tests;

// Modelled on tests/GestorIA.Domain.Tests/MoneyDoesNotCompileFromBinaryFloats.cs: TraceStep.Output is a closed union
// (TraceValue, #42), so nothing outside CalculationTrace.cs can build a step from a bare decimal or int, or smuggle in a
// new kind by deriving from TraceValue elsewhere, and no null survives once nullable warnings are treated as errors,
// the way the repo itself builds (TreatWarningsAsErrors in Directory.Build.props).
public class TraceStepDoesNotCompileWithoutAKind
{
    [Fact]
    public void ADecimalOutputFailsToCompile()
    {
        var errors = Compile("""new GestorIA.Engine.TraceStep("id", GestorIA.Engine.TraceSection.Actividad, "title", [], "formula", 1.5m, "ref");""");

        Assert.Contains(errors, d => d.Id == "CS1503");
    }

    [Fact]
    public void AnIntOutputFailsToCompile()
    {
        var errors = Compile("""new GestorIA.Engine.TraceStep("id", GestorIA.Engine.TraceSection.Actividad, "title", [], "formula", 12, "ref");""");

        Assert.Contains(errors, d => d.Id == "CS1503");
    }

    [Fact]
    public void ANullOutputFailsToCompileOnceNullableWarningsAreErrors()
    {
        var errors = Compile(
            """new GestorIA.Engine.TraceStep("id", GestorIA.Engine.TraceSection.Actividad, "title", [], "formula", null, "ref");""",
            warningsAsErrors: true);

        Assert.Contains(errors, d => d.Id == "CS8625");
    }

    // A record chaining to the base's synthesized protected copy constructor gets past a private constructor, so this is
    // the shape a new case would take. CS0534 is the closing member left unimplemented, not just "some error".
    [Theory]
    [InlineData("TraceValue", "public override string Display() => \"\";")]
    [InlineData("Retenciones", "")]
    [InlineData("PreviousYear", "")]
    [InlineData("NewActivity", "")]
    public void NoRecordOutsideTheEngineCanAddACaseToAUnion(string union, string members)
    {
        var errors = Compile(
            $$"""
            sealed record Smuggled : GestorIA.Engine.{{union}}
            {
                public Smuggled(GestorIA.Engine.{{union}} original) : base(original) { }
                {{members}}
            }
            """,
            wrapInMethod: false);

        Assert.Contains(errors, d => d.Id == "CS0534");
    }

    [Fact]
    public void AMoneyOutputCompiles()
    {
        var errors = Compile("""new GestorIA.Engine.TraceStep("id", GestorIA.Engine.TraceSection.Actividad, "title", [], "formula", new GestorIA.Engine.TraceValue.Money(new GestorIA.Domain.ValueObjects.Money(1.5m)), "ref");""");

        Assert.Empty(errors);
    }

    private static ImmutableArray<Diagnostic> Compile(string statement, bool wrapInMethod = true, bool warningsAsErrors = false)
    {
        // $$"""...""" is a raw string literal with the interpolation marker changed to {{ }}, so the embedded C# source
        // can use plain single braces without escaping them.
        var source = wrapInMethod
            ? $$"""
                class Probe
                {
                    void Use()
                    {
                        {{statement}}
                    }
                }
                """
            : statement;

        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

        var referencePaths = trustedAssemblies
            .Append(typeof(Money).Assembly.Location)
            .Append(typeof(TraceStep).Assembly.Location)
            .Distinct();

        var references = referencePaths.Select(path => MetadataReference.CreateFromFile(path));

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
        if (warningsAsErrors)
        {
            options = options.WithGeneralDiagnosticOption(ReportDiagnostic.Error);
        }

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            options);

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }
}
