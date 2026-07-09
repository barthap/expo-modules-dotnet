namespace Expo.ModulesCore;

public sealed record GeneratedViewDefinition(
    string ModuleName,
    string ComponentName,
    Type ViewType,
    IReadOnlyList<GeneratedViewPropDefinition> Props);
