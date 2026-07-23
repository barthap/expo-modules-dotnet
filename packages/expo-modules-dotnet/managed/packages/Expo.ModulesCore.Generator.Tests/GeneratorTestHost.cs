using Expo.ModulesCore.Generator;
using Expo.JSI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Security.Cryptography;
using System.Text;

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
            .ToArray(),
        runResult.Diagnostics
    );
  }
}

internal sealed record GeneratorRunResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<GeneratedSource> GeneratedSources,
    IReadOnlyList<Diagnostic>? GeneratorDiagnostics = null);

internal sealed record GeneratedSource(string HintName, string Text);

internal static class GeneratorOutputContract
{
  public static IReadOnlyList<string> GetOutputContract(this GeneratorRunResult result) =>
      result.GeneratedSources
          .OrderBy(source => source.HintName, StringComparer.Ordinal)
          .Select(GetSourceContract)
          .ToArray();

  public static string GetSourceContract(GeneratedSource source) =>
      $"{source.HintName}:{GetDigest(NormalizeLineEndings(source.Text))}";

  public static IReadOnlyList<string> GetDiagnosticContract(this GeneratorRunResult result) =>
      (result.GeneratorDiagnostics ?? result.Diagnostics)
          .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
          .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
          .Select(GetDiagnosticContractEntry)
          .ToArray();

  private static string GetDigest(string value) =>
      Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

  private static string NormalizeLineEndings(string value) =>
      value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

  private static string GetDiagnosticContractEntry(Diagnostic diagnostic)
  {
    var lineSpan = diagnostic.Location.GetLineSpan();
    return string.Join(
        "|",
        diagnostic.Id,
        diagnostic.Severity,
        diagnostic.GetMessage(),
        lineSpan.StartLinePosition.Line,
        lineSpan.StartLinePosition.Character,
        lineSpan.EndLinePosition.Line,
        lineSpan.EndLinePosition.Character
    );
  }
}

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
