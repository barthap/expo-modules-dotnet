namespace Expo.ModulesCore.Tests.ExternalTypes;

// Types declared outside the test project, so union payloads resolve from metadata as they do from
// a referenced package.
public sealed record ExternalRecord(string Name, int Count);

[JSEnum(EnumRepresentation.Number)]
public enum ExternalLevel
{
  Low = 1,
  High = 2,
}
