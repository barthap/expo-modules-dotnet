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
      bool allowUnsafe = false,
      IReadOnlyList<MetadataReference>? additionalReferences = null)
  {
    var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
    var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
    var references = additionalReferences is null
        ? ReferenceResolver.GetReferences()
        : ReferenceResolver.GetReferences().Concat(additionalReferences).ToArray();

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

  // Compiles a referenced assembly to a PE image, so its types reach the generator as metadata
  // symbols, as a project reference does in a build.
  public static MetadataReference CompileReference(string source, string assemblyName)
  {
    var compilation = CSharpCompilation.Create(
        assemblyName,
        new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)) },
        ReferenceResolver.GetReferences(),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
    );
    using var stream = new MemoryStream();
    var emitted = compilation.Emit(stream);
    if (!emitted.Success)
    {
      throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));
    }

    return MetadataReference.CreateFromImage(stream.ToArray());
  }

  // Compiles a referenced project as a compilation reference, so its types reach the generator as
  // source symbols of another compilation, as an IDE project reference does.
  public static MetadataReference CompileSourceReference(string source, string assemblyName) =>
      CSharpCompilation.Create(
          assemblyName,
          new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)) },
          ReferenceResolver.GetReferences(),
          new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
      ).ToMetadataReference();
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
