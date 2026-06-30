using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Expo.ModulesCore.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class ExpoModulesGenerator : IIncrementalGenerator
{
  private const string ExpoModuleAttributeMetadataName = "Expo.ModulesCore.ExpoModuleAttribute";
  private const string JSAttributeMetadataName = "Expo.ModulesCore.JSAttribute";

  public void Initialize(IncrementalGeneratorInitializationContext context)
  {
    var modules = context.SyntaxProvider.ForAttributeWithMetadataName(
        ExpoModuleAttributeMetadataName,
        static (node, _) => node is ClassDeclarationSyntax,
        static (syntaxContext, cancellationToken) =>
            CreateModuleModel(syntaxContext, cancellationToken)
    );

    var compilationAndModules = context.CompilationProvider.Combine(modules.Collect());

    context.RegisterSourceOutput(
        compilationAndModules,
        static (sourceContext, value) =>
        {
          var assemblyName = value.Left.AssemblyName ?? "ExpoModules";
          EmitProvider(
              sourceContext,
              assemblyName,
              value.Right.Where(module => module is not null).Select(module => module!)
          );
        }
    );
  }

  private static ExpoModuleModel? CreateModuleModel(
      GeneratorAttributeSyntaxContext context,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();

    if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
    {
      return null;
    }

    var moduleName = typeSymbol.Name.EndsWith("Module", StringComparison.Ordinal)
        ? typeSymbol.Name.Substring(0, typeSymbol.Name.Length - "Module".Length)
        : typeSymbol.Name;

    foreach (var attribute in context.Attributes)
    {
      if (attribute.ConstructorArguments.Length == 1 &&
          attribute.ConstructorArguments[0].Value is string explicitName)
      {
        moduleName = explicitName;
      }
    }

    var diagnostics = new List<ExpoDiagnosticModel>();
    var functions = GetFunctions(typeSymbol, diagnostics);

    return new ExpoModuleModel(
        typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        moduleName,
        new EquatableArray<ExpoFunctionModel>(functions),
        new EquatableArray<ExpoDiagnosticModel>(diagnostics)
    );
  }

  private static IEnumerable<ExpoFunctionModel> GetFunctions(
      INamedTypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics)
  {
    foreach (var member in typeSymbol.GetMembers().OfType<IMethodSymbol>())
    {
      if (member.MethodKind != MethodKind.Ordinary || member.IsStatic || member.IsGenericMethod)
      {
        continue;
      }

      var jsAttribute = member.GetAttributes().FirstOrDefault(attribute =>
          attribute.AttributeClass?.ToDisplayString() == JSAttributeMetadataName);
      if (jsAttribute is null)
      {
        continue;
      }

      var javaScriptName = member.Name;
      if (jsAttribute.ConstructorArguments.Length == 1 &&
          jsAttribute.ConstructorArguments[0].Value is string explicitName)
      {
        javaScriptName = explicitName;
      }

      var returnCodec = GetCodecExpression(member.ReturnType);
      if (returnCodec is null)
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnsupportedReturnType.Id,
            member.ReturnType.Locations.FirstOrDefault(),
            new EquatableArray<string>(
                new[]
                {
                    member.Name,
                    GetDiagnosticTypeName(member.ReturnType),
                }
            )
        ));
        continue;
      }

      var parameters = new List<ExpoParameterModel>();
      foreach (var parameter in member.Parameters)
      {
        var parameterCodec = GetCodecExpression(parameter.Type);
        if (parameterCodec is null)
        {
          diagnostics.Add(new ExpoDiagnosticModel(
              ExpoModulesDiagnostics.UnsupportedParameterType.Id,
              parameter.Locations.FirstOrDefault(),
              new EquatableArray<string>(
                  new[]
                  {
                      parameter.Name,
                      member.Name,
                      GetDiagnosticTypeName(parameter.Type),
                  }
              )
          ));
          continue;
        }

        parameters.Add(new ExpoParameterModel(
            parameter.Name,
            parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            parameterCodec
        ));
      }

      if (parameters.Count != member.Parameters.Length)
      {
        continue;
      }

      yield return new ExpoFunctionModel(
          member.Name,
          javaScriptName,
          member.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
          returnCodec,
          new EquatableArray<ExpoParameterModel>(parameters)
      );
    }
  }

  private static void EmitProvider(
      SourceProductionContext context,
      string assemblyName,
      IEnumerable<ExpoModuleModel> modules)
  {
    var moduleModels = modules.ToArray();
    foreach (var diagnostic in moduleModels.SelectMany(module => module.Diagnostics.Values))
    {
      context.ReportDiagnostic(ToDiagnostic(diagnostic));
    }

    var providerTypeName = $"ExpoModulesProvider_{SanitizeIdentifier(assemblyName)}";
    var builder = new StringBuilder();
    builder.AppendLine("// <auto-generated/>");
    builder.AppendLine("#nullable enable");
    builder.AppendLine("using Expo.ModulesCore;");
    builder.AppendLine("using Expo.ModulesCore.Codecs;");
    builder.AppendLine();
    builder.AppendLine("namespace Expo.ModulesCore.Generated;");
    builder.AppendLine();
    builder.AppendLine($"public static class {providerTypeName}");
    builder.AppendLine("{");
    builder.AppendLine("  public static void Register(global::Expo.JSI.JavaScriptRuntime runtime)");
    builder.AppendLine("  {");
    builder.AppendLine("    global::System.ArgumentNullException.ThrowIfNull(runtime);");
    foreach (var module in moduleModels)
    {
      var moduleVariable = $"module_{SanitizeIdentifier(module.ModuleName)}";
      builder.AppendLine($"    using var {moduleVariable} = ModuleRegistry.DefineModule(runtime, \"{EscapeString(module.ModuleName)}\");");
      foreach (var function in module.Functions.Values)
      {
        builder.AppendLine("    GeneratedFunction.DefineSync(");
        builder.AppendLine("        runtime,");
        builder.AppendLine($"        {moduleVariable},");
        builder.AppendLine($"        \"{EscapeString(function.JavaScriptName)}\",");
        builder.AppendLine($"        {function.Parameters.Values.Count},");
        builder.AppendLine($"        {GetHostFunctionName(module, function)},");
        builder.AppendLine($"        new {module.FullyQualifiedTypeName}()");
        builder.AppendLine("    );");
      }
    }
    builder.AppendLine("  }");
    foreach (var module in moduleModels)
    {
      foreach (var function in module.Functions.Values)
      {
        EmitHostFunction(builder, module, function);
      }
    }
    builder.AppendLine("}");

    context.AddSource($"{providerTypeName}.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
  }

  private static Diagnostic ToDiagnostic(ExpoDiagnosticModel model)
  {
    var descriptor = model.DescriptorId switch
    {
      "EXPOJSI001" => ExpoModulesDiagnostics.UnsupportedParameterType,
      "EXPOJSI002" => ExpoModulesDiagnostics.UnsupportedReturnType,
      "EXPOJSI003" => ExpoModulesDiagnostics.UnsupportedModuleConstructor,
      _ => throw new InvalidOperationException($"Unknown diagnostic descriptor: {model.DescriptorId}"),
    };
    return Diagnostic.Create(descriptor, model.Location, model.Arguments.Values.Cast<object>().ToArray());
  }

  private static void EmitHostFunction(
      StringBuilder builder,
      ExpoModuleModel module,
      ExpoFunctionModel function)
  {
    builder.AppendLine();
    builder.AppendLine($"  private static global::Expo.JSI.JavaScriptValue {GetHostFunctionName(module, function)}(");
    builder.AppendLine("      global::Expo.JSI.JavaScriptRuntime runtime,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptValueRef thisValue,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptArguments arguments,");
    builder.AppendLine("      object context)");
    builder.AppendLine("  {");
    builder.AppendLine($"    GeneratedFunction.RequireArgumentCount(\"{EscapeString(module.ModuleName)}.{EscapeString(function.JavaScriptName)}\", arguments, {function.Parameters.Values.Count});");
    builder.AppendLine();
    builder.AppendLine($"    var module = ({module.FullyQualifiedTypeName})context;");

    for (var index = 0; index < function.Parameters.Values.Count; index++)
    {
      var parameter = function.Parameters.Values[index];
      builder.AppendLine($"    var {parameter.Name} = {GetDecodeExpression(parameter.CodecExpression, index)};");
    }

    var argumentList = string.Join(", ", function.Parameters.Values.Select(parameter => parameter.Name));
    builder.AppendLine($"    return {function.ReturnCodecExpression}.Encode(module.{function.MethodName}({argumentList}), runtime);");
    builder.AppendLine("  }");
  }

  private static string SanitizeIdentifier(string value)
  {
    var builder = new StringBuilder(value.Length);
    foreach (var character in value)
    {
      builder.Append(char.IsLetterOrDigit(character) ? character : '_');
    }
    return builder.Length == 0 ? "ExpoModules" : builder.ToString();
  }

  private static string GetHostFunctionName(ExpoModuleModel module, ExpoFunctionModel function) =>
      $"{SanitizeIdentifier(module.ModuleName)}_{SanitizeIdentifier(function.JavaScriptName)}_HostFunction";

  private static string GetDecodeExpression(string codecExpression, int index)
  {
    var methodName = codecExpression.StartsWith("JavaScriptArrayCodec<", StringComparison.Ordinal)
        ? "DecodeToArray"
        : "Decode";
    return $"{codecExpression}.{methodName}(arguments.GetValue({index}), runtime)";
  }

  private static string? GetCodecExpression(ITypeSymbol typeSymbol)
  {
    return typeSymbol.SpecialType switch
    {
      SpecialType.System_Boolean => "BoolCodec",
      SpecialType.System_Double => "DoubleCodec",
      SpecialType.System_String => "StringCodec",
      _ => TryGetReadOnlyListCodec(typeSymbol),
    };
  }

  private static string GetDiagnosticTypeName(ITypeSymbol typeSymbol)
  {
    return typeSymbol.SpecialType switch
    {
      SpecialType.System_Boolean => "System.Boolean",
      SpecialType.System_Double => "System.Double",
      SpecialType.System_String => "System.String",
      SpecialType.System_Decimal => "System.Decimal",
      _ => typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
    };
  }

  private static string? TryGetReadOnlyListCodec(ITypeSymbol typeSymbol)
  {
    if (typeSymbol is not INamedTypeSymbol namedType ||
        namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) !=
        "global::System.Collections.Generic.IReadOnlyList<T>")
    {
      return null;
    }

    var elementType = namedType.TypeArguments.Single();
    var elementCodec = GetCodecExpression(elementType);
    if (elementCodec is null)
    {
      return null;
    }

    var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    return $"JavaScriptArrayCodec<{elementTypeName}, {elementCodec}>";
  }

  private static string EscapeString(string value) =>
      value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
