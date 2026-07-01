# Dotnet Module Targets Delta Spec

## Goal

Managed Expo module providers must register into a caller-supplied JavaScript
modules object instead of hardcoding `globalThis.expo.modules`. The default
managed-only namespace for local proofs and tests is `globalThis._expoDotnet.modules`.

## Scope

- Applies to `Expo.ModulesCore` module registration helpers and Roslyn-generated
  provider output.
- Applies to repo-local Hermes proof code and managed tests that currently
  assert `globalThis.expo.modules`.
- Does not add an adapter that mirrors C# modules into `globalThis.expo.modules`.
- Does not implement the future host-object or lazy-object registry.

## Delta Requirements

### Requirement: Generated Providers Use Caller-Supplied Module Targets

Generated module providers SHALL expose registration that takes both a
`JavaScriptRuntime` and the JavaScript modules object to populate.

#### Scenario: Generated provider registers modules
- **GIVEN** a generated provider for a library-local module assembly
- **WHEN** app or proof code calls `Register(runtime, modules)`
- **THEN** generated code SHALL define module objects under the supplied
  `modules` object
- **AND** generated code SHALL NOT discover or create `globalThis.expo.modules`
  internally

### Requirement: Default Dotnet Namespace Avoids Expo Globals

`Expo.ModulesCore` SHALL provide a helper that creates or returns
`globalThis._expoDotnet.modules`.

#### Scenario: Managed proof needs a modules object
- **GIVEN** a runtime without an existing `_expoDotnet` object
- **WHEN** managed setup asks for the default dotnet modules object
- **THEN** the helper SHALL create `_expoDotnet.modules`
- **AND** it SHALL NOT create or mutate `globalThis.expo`

#### Scenario: Dotnet namespace already exists
- **GIVEN** `_expoDotnet.modules` already exists as a JavaScript object
- **WHEN** managed setup asks for the default dotnet modules object
- **THEN** the helper SHALL reuse that object

### Requirement: Plain Object Registration Remains A Temporary Target

This slice SHALL keep module registration based on ordinary `JavaScriptObject`
targets. A future host-object or lazy-object registry MAY replace the target
implementation without restoring generated global path knowledge.
