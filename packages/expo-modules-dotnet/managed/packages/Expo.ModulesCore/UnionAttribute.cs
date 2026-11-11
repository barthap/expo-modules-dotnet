namespace Expo.ModulesCore;

/// <summary>Marks an <c>abstract record</c> whose nested <c>sealed record</c> cases cross as one of several JavaScript shapes.</summary>
/// <remarks>
/// Each case derives from the union directly and has one positional parameter, the payload. The
/// generated codec tries the cases in declaration order, and the first case whose payload decodes
/// wins. Declare the union in the project that uses it.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class UnionAttribute : Attribute
{
}
