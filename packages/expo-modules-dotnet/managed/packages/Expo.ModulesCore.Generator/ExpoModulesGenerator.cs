using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Expo.ModulesCore.Generator;

[Generator(LanguageNames.CSharp)]
public sealed partial class ExpoModulesGenerator : IIncrementalGenerator
{
  private const string ExpoModuleAttributeMetadataName = "Expo.ModulesCore.ExpoModuleAttribute";
  private const string ExpoSharedObjectAttributeMetadataName = "Expo.ModulesCore.ExpoSharedObjectAttribute";
  private const string SharedObjectMetadataName = "Expo.ModulesCore.SharedObject";
  private const string SharedRefMetadataName = "Expo.ModulesCore.SharedRef<T>";
  private const string EventsAttributeMetadataName = "Expo.ModulesCore.EventsAttribute";
  private const string EventAttributeMetadataName = "Expo.ModulesCore.EventAttribute";
  private const string OnCreateAttributeMetadataName = "Expo.ModulesCore.OnCreateAttribute";
  private const string OnDestroyAttributeMetadataName = "Expo.ModulesCore.OnDestroyAttribute";
  private const string OnStartObservingAttributeMetadataName = "Expo.ModulesCore.OnStartObservingAttribute";
  private const string OnStopObservingAttributeMetadataName = "Expo.ModulesCore.OnStopObservingAttribute";
  private const string JSEnumAttributeMetadataName = "Expo.ModulesCore.JSEnumAttribute";
  private const string JSAttributeMetadataName = "Expo.ModulesCore.JSAttribute";
  private const string DotnetRuntimeContextMetadataName = "Expo.ModulesCore.DotnetRuntimeContext";
  private const string JavaScriptValueMetadataName = "global::Expo.JSI.JavaScriptValue";
  private const string ArrayBufferMetadataName = "global::Expo.ModulesCore.ArrayBuffer";

  public void Initialize(IncrementalGeneratorInitializationContext context)
  {
    var modules = context.SyntaxProvider.ForAttributeWithMetadataName(
        ExpoModuleAttributeMetadataName,
        static (node, _) => node is ClassDeclarationSyntax,
        static (syntaxContext, cancellationToken) =>
            CreateModuleModel(syntaxContext, cancellationToken)
    );

    var sharedObjects = context.SyntaxProvider.ForAttributeWithMetadataName(
        ExpoSharedObjectAttributeMetadataName,
        static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
        static (syntaxContext, cancellationToken) =>
            CreateSharedObjectModel(syntaxContext, cancellationToken)
    );

    var orphanEvents = context.SyntaxProvider.ForAttributeWithMetadataName(
        EventAttributeMetadataName,
        static (node, _) => node is PropertyDeclarationSyntax,
        static (syntaxContext, cancellationToken) =>
            CreateOrphanEventModel(syntaxContext, cancellationToken)
    );

    context.RegisterSourceOutput(
        orphanEvents.Where(model => model is not null),
        static (sourceContext, model) => EmitOrphanEvent(sourceContext, model!)
    );

    var compilationModulesAndSharedObjects = context.CompilationProvider
        .Combine(modules.Collect())
        .Combine(sharedObjects.Collect());

    context.RegisterSourceOutput(
        compilationModulesAndSharedObjects,
        static (sourceContext, value) =>
        {
          var assemblyName = value.Left.Left.AssemblyName ?? "ExpoModules";
          EmitProvider(
              sourceContext,
              assemblyName,
              value.Left.Right.Where(module => module is not null).Select(module => module!),
              value.Right.Where(sharedObject => sharedObject is not null).Select(sharedObject => sharedObject!)
          );
        }
    );
  }

}
