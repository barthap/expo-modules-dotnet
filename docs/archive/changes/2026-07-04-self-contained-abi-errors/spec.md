# Self-Contained ABI Errors

## Goal

Remove the latent error-message lifetime hazard caused by `expo_jsi_error`
messages pointing into thread-local native storage.

## Scope

This change updates the internal C ABI, native bridge, managed interop structs,
testhost, tests, and current living specs. It does not change public JavaScript
or module-author APIs.

## Accepted Design

`expo_jsi_error` owns nonzero error message storage through the same
copy-then-release pattern used by native string results. Native failure paths
copy the UTF-8 error message into an error-owned buffer and return a release
callback. Managed code copies the message into a C# string and invokes the
release callback exactly once while throwing or inspecting the error.

Successful error structs remain allocation-free: `code = 0`, null message, null
release context, and null release callback.

## Delta Requirements

### MODIFIED Requirement: ABI Results Are Structured

Fallible ABI operations SHALL return self-contained `expo_jsi_error` values on
failure.

#### Scenario: Native error message is consumed after another ABI call

- **GIVEN** a native ABI function returns a nonzero `expo_jsi_error`
- **WHEN** another ABI call is made before managed code reads the first error
  message
- **THEN** the first error message SHALL remain valid until managed code copies
  and releases it

#### Scenario: Managed code consumes a native error

- **GIVEN** managed code receives a nonzero `expo_jsi_error`
- **WHEN** it converts the error into a managed exception or message
- **THEN** it SHALL copy the UTF-8 message
- **AND** it SHALL invoke the native release callback exactly once when present

#### Scenario: Success error result is returned

- **GIVEN** an ABI operation succeeds
- **WHEN** native returns an `expo_jsi_error`
- **THEN** the error SHALL have code zero and no release callback
