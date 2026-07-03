# Nullish and Void Module Semantics Delta Spec

## Summary

`Expo.JSI` and `Expo.ModulesCore` shall distinguish JavaScript `undefined` and
`null` at value creation, dispatch, argument decoding, and generated function
returns.

## Requirements

### Requirement: Managed JSI creates nullish values

`JavaScriptRuntime` shall expose owned value creation for JavaScript
`undefined` and `null`.

#### Scenario: Undefined value is created
- **WHEN** managed code creates an undefined value
- **THEN** the resulting value kind shall be `Undefined`
- **AND** the value shall be nullish

#### Scenario: Null value is created
- **WHEN** managed code creates a null value
- **THEN** the resulting value kind shall be `Null`
- **AND** the value shall be nullish

### Requirement: Generated module functions preserve void and nullable semantics

Generated sync functions shall map C# void and nullable values to JavaScript
nullish values without using ad hoc sentinel objects.

#### Scenario: Void sync function returns undefined
- **GIVEN** an authored `[JS] void Foo()`
- **WHEN** JavaScript calls `Foo`
- **THEN** the host function shall return JavaScript `undefined`

#### Scenario: Nullable argument accepts nullish values
- **GIVEN** an authored `[JS] void Foo(double? value)`
- **WHEN** JavaScript passes `null` or explicit `undefined`
- **THEN** the generated dispatch shall pass C# `null`

#### Scenario: Nullable argument default handles omission and undefined
- **GIVEN** an authored `[JS] void Foo(double? value = 42.0)`
- **WHEN** JavaScript omits the argument or passes explicit `undefined`
- **THEN** the generated dispatch shall pass the C# default value
- **AND** explicit JavaScript `null` shall still pass C# `null`

#### Scenario: Nullable return maps null to JavaScript null
- **GIVEN** an authored `[JS] double? Foo()`
- **WHEN** the method returns C# `null`
- **THEN** the generated dispatch shall return JavaScript `null`
