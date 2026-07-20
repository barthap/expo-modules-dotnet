using Expo.ModulesCore.Generator;
using Expo.JSI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Expo.ModulesCore.Generator.Tests;

internal static class GeneratorTestHost
{
  public static GeneratorRunResult Run(
      string source,
      string assemblyName = "Expo.TestModules",
      bool allowUnsafe = false)
  {
    var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
    var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
    var references = ReferenceResolver.GetReferences();

    var compilation = CSharpCompilation.Create(
        assemblyName,
        new[] { syntaxTree },
        references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(allowUnsafe)
    );

    var generator = new ExpoModulesGenerator();
    GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
    driver = driver.RunGeneratorsAndUpdateCompilation(
        compilation,
        out var outputCompilation,
        out var generatorDiagnostics
    );

    var runResult = driver.GetRunResult().Results.Single();
    return new GeneratorRunResult(
        outputCompilation.GetDiagnostics().Concat(generatorDiagnostics).ToArray(),
        runResult.GeneratedSources
            .Select(sourceResult => new GeneratedSource(
                sourceResult.HintName,
                sourceResult.SourceText.ToString()
            ))
            .ToArray()
    );
  }
}

internal sealed record GeneratorRunResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<GeneratedSource> GeneratedSources);

internal sealed record GeneratedSource(string HintName, string Text);

file static class ReferenceResolver
{
  public static IReadOnlyList<MetadataReference> GetReferences()
  {
    var trustedPlatformAssemblies =
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ??
        Array.Empty<string>();

    var references = trustedPlatformAssemblies
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToList();

    references.Add(MetadataReference.CreateFromFile(typeof(ExpoModuleAttribute).Assembly.Location));
    references.Add(MetadataReference.CreateFromFile(typeof(JavaScriptRuntime).Assembly.Location));
    return references;
  }
}
