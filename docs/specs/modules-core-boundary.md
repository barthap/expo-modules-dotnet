# Modules Core Boundary

## Purpose

Define the current boundary between low-level `Expo.JSI` wrappers and the
future `Expo.ModulesCore` module DSL and generated-binding package.

## Requirements

### Requirement: ModulesCore Does Not Exist Yet

The repository SHALL treat `Expo.ModulesCore` as future work until the package
is explicitly introduced.

#### Scenario: Current code references module behavior
- **GIVEN** current tests include generated-looking module conversion proof code
- **WHEN** agents inspect package ownership
- **THEN** they SHALL treat that code as temporary proof material, not as
  permanent `Expo.JSI` module architecture

### Requirement: ModulesCore Owns Module DSL

When introduced, `Expo.ModulesCore` SHALL own authored module DSL concepts,
module registry/provider abstractions, generated-binding helpers, and typed
converters above `Expo.JSI`.

#### Scenario: Authored module method is exposed
- **GIVEN** a future module class declares a JavaScript-facing method
- **WHEN** generated binding code registers it
- **THEN** the generated code SHALL decode `JavaScriptArguments`, call the
  authored C# method directly, and encode the return value through `Expo.JSI`
  wrappers

### Requirement: Generated Bindings Avoid Hot-Path Reflection

Generated v2 runtime bindings SHALL avoid runtime hot-path reflection and
dynamic invocation.

#### Scenario: Module provider invokes a method
- **GIVEN** generated provider code handles a JavaScript call
- **WHEN** it invokes the authored module method
- **THEN** it SHALL NOT use `Assembly.GetTypes`, `MethodInfo.Invoke`,
  `Delegate.DynamicInvoke`, `object?[]` as the normal argument container, or
  JSON serialization for ordinary JSI values

### Requirement: Source Generator Comes After Hand-Written Shape

The source generator SHALL be implemented only after the generated-looking
hand-written binding shape is stable.

#### Scenario: New module feature is proposed
- **GIVEN** a feature needs new conversion or dispatch semantics
- **WHEN** the behavior is not already proven by hand-written generated-looking
  code
- **THEN** the proposal SHALL first prove the shape before encoding it in the
  generator
