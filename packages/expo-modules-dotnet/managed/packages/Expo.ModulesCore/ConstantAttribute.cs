namespace Expo.ModulesCore;

/// <summary>
/// Marks a getter-only <see cref="JSAttribute" /> property as a constant: the first successful
/// JavaScript read runs the getter and stores the value on the JavaScript object as a read-only
/// data property, so later reads on that object do not call C#.
/// </summary>
/// <remarks>
/// A constant needs <c>[JS]</c>, no public or internal setter, and a type that holds no leaf and
/// no shared object. A getter that throws caches nothing and runs again on the next read. A
/// shared-object constant is cached on each instance. A plain getter-only <c>[JS]</c> property
/// without this attribute runs its getter on every read.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ConstantAttribute : Attribute
{
}
