using System.Reflection;

namespace GestorIA.Api;

// `dotnet build` starts the app as GetDocument.Insider to write openapi/v1.json (GestorIA.Api.csproj). That run has no key and
// no database configured, and must not need either.
internal static class OpenApiGeneration
{
    public static bool IsRunning => Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
