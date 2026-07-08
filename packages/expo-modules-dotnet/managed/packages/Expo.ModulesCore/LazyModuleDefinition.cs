using Expo.JSI;

namespace Expo.ModulesCore;

public sealed record LazyModuleDefinition(
    string Name,
    Func<DotnetRuntimeContext, JavaScriptObject, JavaScriptObject> CreateModule);
