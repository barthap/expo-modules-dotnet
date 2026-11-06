# Uint8Array Codecs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Uint8Array` crosses the generated boundary wherever `ArrayBuffer` does, except as a direct typed-event payload. A JavaScript `Uint8Array` decodes as a view over one retained buffer lease with its offset and length and no byte copy; a managed view encodes as a new JavaScript `Uint8Array` over the same bytes. No ABI change (roadmap item 036).

**Architecture:** One managed type, `Uint8Array`, owns one `ArrayBuffer` lease plus an offset and a length and slices every byte access. One scoped codec, `Uint8ArrayCodec`, has the `ArrayBufferCodec` shape: it proves the kind by prototype-chain membership (`Uint8Array.prototype.isPrototypeOf(value)`, the `instanceof` test), so subclass instances are accepted, reads `buffer`, `byteOffset`, and `byteLength`, and encodes through the global constructor. The generator gains one `ScopedCodec` row; nullable, list, dictionary, and record positions compose through 030 and 031 unchanged. The docs close the change last.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator, xUnit v3, Hermes-backed managed tests (`scripts/test-managed.sh`).

**Spec:** `docs/changes/2026-11-06-typed-array-codecs/spec.md` (binding authority; committed at `5aa269d`). Base commit is `babcba8` (the 035 close); `git diff --stat babcba8..HEAD -- packages` prints nothing at plan time. The brief `docs/plans/036-typed-array-codecs.md` is background only; the spec wins.

**Execution:** subagent-driven (superpowers:subagent-driven-development). Model hints: **cheap** (mechanical), **standard** (normal implementation).

Paths in **Files** blocks are relative to `packages/expo-modules-dotnet/managed/packages/` unless they start with `docs/` or `scripts/`. Line numbers are at plan time.

## Deviations from the spec (controller decision)

- **D1. Typed events stay out of scope, so "wherever `ArrayBuffer` is" is too broad.** The 031 event path names `ArrayBuffer` by type: `TryGetEventDelegatePayload` classifies the payload by comparing its name with `ArrayBufferMetadataName` (`ExpoModulesGenerator.EventAnalysis.cs:354-357`), and `GetUnsupportedEventPayload` names it again (`:384`). Per the spec's Generator paragraph, `Uint8Array` payloads stay out of scope. They need no generator code: a `Uint8Array` payload takes the `Codec` kind, and `AsJavaScriptCodecArgument` (`ExpoModulesGenerator.Codecs.cs:89-92`) rejects the `ScopedOnly` descriptor with `no encode-capable codec is available` (`EventAnalysis.cs:228`). The spec's ADDED requirement says "accepted wherever `ArrayBuffer` is", and `ArrayBuffer` is accepted as a direct typed-event payload. Task 4 merges the requirement with ", except as a direct typed-event payload" after that phrase. The controller can strike D1; the merge is then verbatim.
- **D2. Two doc comments outside the spec's file list define "leaf".** The spec makes the view itself the scope leaf. `GeneratedConversionScope.cs:5-9` and `IGeneratedScopedCodec.cs:7-8` say a leaf is a `JavaScriptValue` or an `ArrayBuffer`. Task 1 adds `Uint8Array` to both comments; no code in those files changes.

Plan decisions that fill gaps in the spec (not deviations):

- **Decode validates the three fields with the same message.** The spec says `buffer` must be an `ArrayBuffer`; it is silent about the two numbers. A real `Uint8Array` always passes. An object that passes the kind test but carries its own fields (`Object.create(Uint8Array.prototype)` with own `buffer`, `byteOffset`, and `byteLength`, or a view with a shadowed own `byteOffset`) gets `Value is not a Uint8Array.` when a field has the wrong kind or is not an integer in `[0, int.MaxValue]`, and `ArgumentOutOfRangeException` from the view constructor when the numbers do not fit the buffer. Both fail before any byte access, and the lease is disposed.
- **Async members.** The spec's signature block lists only sync members and says "Async variants mirror `ArrayBuffer`'s". The type gets `ToArrayAsync`, `WithBytesAsync` ×2, and `WithReadOnlyBytesAsync` ×2. `Copy` and `CopyAsync` are not in the spec's list and are omitted.
- **After `Dispose`**, every member except `Dispose` throws `ObjectDisposedException`, including `Buffer`, `ByteOffset`, and `Length`, as `ArrayBuffer.ByteLength` does (`ArrayBuffer.cs:26-33`).
- **Descriptor table row.** Task 4 adds a `Uint8Array` row (`ScopedOnly`, `ScopedResource`) to the descriptor table in `modules-core-boundary.md`; it restates the ADDED requirement.

## Global Constraints

- **Design criteria (review criteria for every task, from the spec):**
  1. Clean code: one managed view type (`ArrayBuffer` lease, offset, length), one codec with the `ArrayBufferCodec` shape, one generator row. No typed-array kind enum.
  2. Performance: the kind proof is three property reads (`Uint8Array`, `prototype`, `isPrototypeOf`) and one call per decode, through wrappers the managed side already has; no cached handle. Decode then reads `buffer` and two numbers and retains the backing buffer; no copy. Encode encodes the buffer as today and calls the constructor once. The constructor is read from the global object per encode: no cached handle.
  3. Correctness: the view's bytes are a slice of the buffer's bytes under the buffer's captured-length check, so a detached or resized buffer fails loudly. The lease is released once at invocation end, as for `ArrayBuffer`.
- New production files (exactly two): `Expo.ModulesCore/Uint8Array.cs`, `Expo.ModulesCore/Codecs/Uint8ArrayCodec.cs`. Edited production files: `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs` (one constant, one branch) and, comment-only (D2), `Expo.ModulesCore/Codecs/GeneratedConversionScope.cs` and `Expo.ModulesCore/Codecs/IGeneratedScopedCodec.cs`. `ArrayBuffer.cs`, `ArrayBufferCodec.cs`, every other codec, `ExpoModulesGenerator.EventAnalysis.cs`, `ExpoModulesGenerator.Emission.cs`, `ExpoCodecDescriptor.cs`, and everything under `Expo.JSI` and `packages/expo-modules-dotnet/native` do not change.
- No ABI or native change (spec decision 1). `git diff --stat babcba8..HEAD -- packages/expo-modules-dotnet/native` and `rg -n 'Uint8Array' packages/expo-modules-dotnet/native` print nothing.
- The kind message is exact: `Value is not a Uint8Array.`, thrown as `InvalidOperationException`. JavaScript sees it inside a longer `Error.message`, so Hermes tests use `Contains`, never equality.
- `Uint8ArrayCodec` does not implement `IJavaScriptValueKindCodec`, and `GetUnionMemberKind` (`ExpoModulesGenerator.Codecs.cs:329-355`) does not learn `Uint8Array` (spec decision 6). `byte[]`, `ArrayBuffer`, `Memory<byte>`, and `ReadOnlyMemory<byte>` keep rejecting a `Uint8Array` (spec decision 5).
- No existing test changes and no output-contract digest moves. No fixture or test in `Expo.ModulesCore.Generator.Tests` mentions `Uint8Array` (`rg -n Uint8Array packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests` prints nothing at plan time). A moved `GeneratorPreserves*` or `GeneratorPins*` digest is a finding: stop and report.
- The generator never derives a policy from codec text. 029's typed-policy scan stays empty (it is empty at plan time):
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- Owned-wrapper rule (`expo-jsi-managed-handle-lifetime` skill): no chained calls that drop an owned `JavaScriptValue`, `JavaScriptObject`, or `JavaScriptFunction`. Every owned intermediate is a `using` local. Refs (`JavaScriptValueRef`, `JavaScriptObjectRef`) are `ref struct`s and are not disposed.
- Counter rules (from 031): exact-once lease assertions use JS-created buffers and `LongLivedArrayBuffersReleased` after `WaitUntilIdle()`. The counter counts native **entries**: each decode of a JS-created buffer creates one entry, so two decodes of one buffer count 2. A `Retain()` copy shares its entry and pins it until disposed. A native-backed buffer (`Allocate`, `CopyFrom`) is not counted; tests never assert a count for one.
- After an `await`, authored code runs off the runtime thread, where a JavaScript-backed buffer allows only asynchronous byte access (`Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs:932-941`). Async fixtures use `ToArrayAsync`.
- A count, message, ID list, or generated fragment that differs from this plan is a finding to report, never a value to adjust until green. Values marked "inferred" come from reading code, not from an existing test.
- No runtime hot-path reflection in generated code. Generated code stays NativeAOT-compatible.
- Style: 2-space indent, file-scoped namespaces, existing naming and doc-comment style. Use `bunx`, never `npx`.
- Privacy: no local absolute paths, usernames, machine names, or hostnames in any committed file or commit message. Never write a literal username into a scan command; write `/[U]sers/` instead. Privacy scan (from the spec):
  `git diff --unified=0 babcba8..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'`
- Commits: conventional subjects (`feat(...)`, `test(...)`, `docs(...)`). Every commit message ends with the line `Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv`. No push, no PR, no GitHub posts. Codex is disabled; do not run it.
- Before each commit: `scripts/format.sh --check --all` passes (if it fails, run `scripts/format.sh`, then check again) and `git diff --check` is clean.
- Finish: `scripts/test-managed.sh` passes with 0 failed and 0 skipped.

## Commands

| Purpose            | Command                                                                                                                                                    |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Generator tests    | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` |
| ModulesCore tests  | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`                     |
| One test           | append `--filter "FullyQualifiedName~<Name>"` to either command                                                                                            |
| Full managed suite | `scripts/test-managed.sh` (1349 passed, 0 skipped at `babcba8`)                                                                                            |

Test-case arithmetic (each `InlineData` row is one case): Task 1 adds 30, Task 2 adds 34, Task 3 adds 14, nothing is deleted. The full suite reads 1379 after Task 1, 1413 after Task 2, and 1427 after Task 3.

## Review Focus

1. **JavaScript passes a subclass of `Uint8Array`**, such as the React Native `Buffer` polyfill. The prototype-chain test accepts it, and it decodes like a plain `Uint8Array` (shared bytes, in-place writes); 038 and 025 callers will meet this first. Tests: Task 1 `DecodeAcceptsAUint8ArraySubclassInstance` (`SUB`, the ES5 prototype form the `buffer` polyfill uses), Task 3 `Uint8ArrayFillWritesInPlaceAndReleasesOnce` (subclass assertion).
2. **A spoofed or shadowed view.** `Object.create(Uint8Array.prototype)` with own `buffer`, `byteOffset`, and `byteLength` passes the kind test and is **accepted**, bounds-checked like any view. An own `byteOffset` past the buffer, a fractional `byteOffset`, or a non-buffer `buffer` fails loudly, never reads outside the buffer, and leaks no lease. `({ constructor: Uint8Array })` is rejected by the kind test. Tests: Task 1 `DecodeAcceptsAPrototypeSpoofWithinItsBuffer`, `DecodeRejectsShadowedViewFields`, and `DecodeRejectsEverythingButAUint8Array` (`({ constructor: Uint8Array })` row).
3. **Zero-length views** (`new Uint8Array(0)`, a view that starts at the buffer's end). Both decode with `Length 0` and encode back; the bounds check is `<=`. Test: Task 1 `EmptyViewsDecodeAndEncode`.
4. **An async method reads the view after `await`.** Async byte access keeps the slice. Tests: Task 1 `ViewSlicesNativeStorageSyncAndAsync`, Task 3 `Uint8ArraySubViewSeesOnlyItsSliceSyncAndAsync` (`hexLater`).
5. **Authored code throws after the view decoded.** The lease releases once and the error reaches JavaScript. Test: Task 3 `Uint8ArrayLeaseReleasesWhenAuthoredCodeThrows`.

---

### Task 1: Uint8Array view and codec

Model: **standard**.

This task must land before Task 2. The generator test host compiles generated code against the real `Expo.ModulesCore` assembly, so Task 2's acceptance rows need these types.

**Files:**

- Create: `Expo.ModulesCore/Uint8Array.cs`, `Expo.ModulesCore/Codecs/Uint8ArrayCodec.cs`
- Modify (comments only, D2): `Expo.ModulesCore/Codecs/GeneratedConversionScope.cs:5-9`, `Expo.ModulesCore/Codecs/IGeneratedScopedCodec.cs:7-8`
- Test (create): `Expo.ModulesCore.Tests/Codecs/Uint8ArrayCodecTests.cs`

**Interfaces:**

- Produces (namespace `Expo.ModulesCore`, author-facing; doc comments on the type and every member, in the style of `ArrayBuffer.cs`, including the single-owner remark of `ArrayBuffer.cs:6-11`):

```csharp
public sealed class Uint8Array : IDisposable
{
  internal Uint8Array(ArrayBuffer buffer, int byteOffset, int length); // takes ownership of buffer on success only
  public static Uint8Array Allocate(int length);           // new(ArrayBuffer.Allocate(length), 0, length)
  public static Uint8Array CopyFrom(ReadOnlySpan<byte> bytes);
  public ArrayBuffer Buffer { get; }
  public int ByteOffset { get; }
  public int Length { get; }
  public Uint8Array Retain();                              // new(Buffer.Retain(), ByteOffset, Length)
  public byte[] ToArray();
  public Task<byte[]> ToArrayAsync(CancellationToken cancellationToken = default);
  public void WithBytes(ArrayBufferBytesAction action);
  public TResult WithBytes<TResult>(ArrayBufferBytesFunc<TResult> action);
  public void WithReadOnlyBytes(ArrayBufferReadOnlyBytesAction action);
  public TResult WithReadOnlyBytes<TResult>(ArrayBufferReadOnlyBytesFunc<TResult> action);
  public Task WithBytesAsync(ArrayBufferBytesAction action, CancellationToken cancellationToken = default);
  public Task<TResult> WithBytesAsync<TResult>(ArrayBufferBytesFunc<TResult> action, CancellationToken cancellationToken = default);
  public Task WithReadOnlyBytesAsync(ArrayBufferReadOnlyBytesAction action, CancellationToken cancellationToken = default);
  public Task<TResult> WithReadOnlyBytesAsync<TResult>(ArrayBufferReadOnlyBytesFunc<TResult> action, CancellationToken cancellationToken = default);
  public void Dispose();                                   // Interlocked.Exchange the lease to null, dispose it; idempotent
}
```

One private field `ArrayBuffer? buffer` plus the two ints. The constructor: `ArgumentNullException.ThrowIfNull(buffer)`; `ArgumentOutOfRangeException.ThrowIfNegative` on both ints; `(long)byteOffset + length > buffer.ByteLength` throws `ArgumentOutOfRangeException` (`nameof(length)`, message "The view extends past the end of its buffer."; this plan's choice). Every byte member delegates to the buffer's member of the same name with `bytes => action(bytes.Slice(byteOffset, length))`, so the buffer's own null, disposed, and captured-length checks run first (`ArrayBuffer.cs:107-141`, `:170-225`; length check `Expo.JSI/JavaScriptArrayBuffer.cs:252-258`). After `Dispose`, every member except `Dispose` throws `ObjectDisposedException` (plan decision). No `Copy`, no `CopyAsync`.

- Produces (namespace `Expo.ModulesCore.Codecs`, `using Expo.JSI;`):

```csharp
public readonly struct Uint8ArrayCodec : IGeneratedScopedCodec<Uint8Array>
{
  public static Uint8Array Decode(JavaScriptValueRef value, JavaScriptRuntime runtime);
  public static Uint8Array Decode(JavaScriptValue value, JavaScriptRuntime runtime) => Decode(value.Ref, runtime);
  public static JavaScriptValue Encode(Uint8Array value, JavaScriptRuntime runtime);   // ArgumentNullException.ThrowIfNull(value)
  public static JavaScriptValue EncodeCopy(ReadOnlySpan<byte> bytes, JavaScriptRuntime runtime); // using var view = Uint8Array.CopyFrom(bytes); return Encode(view, runtime);
  public static Uint8Array Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, GeneratedConversionScope scope); // decode, scope.Register(view), dispose view if Register throws
  public static void Transfer(Uint8Array value, GeneratedConversionScope scope);       // registers the view; null registers nothing
  public static JavaScriptValue EncodeBorrowed(Uint8Array value, JavaScriptRuntime runtime) => Encode(value, runtime);
}
```

Copy the member order and the three scoped members' doc comments from `ArrayBufferCodec.cs:35-69`. The scoped members register the **view** (spec: the view is the leaf); its `Dispose` is idempotent, as `GeneratedConversionScope.Register` requires (`GeneratedConversionScope.cs:21-24`).

Decode, in this order (the one algorithm the signatures leave open). `NotUint8Array()` is a private helper returning `new InvalidOperationException("Value is not a Uint8Array.")`:

1. `if (!value.IsObject) throw NotUint8Array();`. This rejects `ArrayBuffer` (kind `ArrayBuffer`), primitives, nullish values, and functions before any `AsObject()` call. `Uint8Array` values report kind `Object` (`EitherCodecTests.cs:138`, `ResourceScopeModuleTests.cs:1012`).
2. Kind proof, `Uint8Array.prototype.isPrototypeOf(value)`. Every wrapper below is owned and is a `using` local; none is a ref:
   - `using var global = runtime.Global();`: `JavaScriptObject` (`JavaScriptRuntime.cs:220`).
   - `using var constructorValue = global.GetProperty("Uint8Array");`: `JavaScriptValue` (`JavaScriptObject.cs:53`).
   - `using var constructorObject = constructorValue.AsObject();`: `JavaScriptObject` with its own retained handle (`JavaScriptValue.cs:147`); a function passes the native object check because a JSI function is an object.
   - `using var prototypeValue = constructorObject.GetProperty("prototype");`: `JavaScriptValue`.
   - `using var prototypeObject = prototypeValue.AsObject();`: `JavaScriptObject`.
   - `using var isPrototypeOfValue = prototypeObject.GetProperty("isPrototypeOf");`: `JavaScriptValue`.
   - `using var isPrototypeOf = isPrototypeOfValue.AsFunction();`: `JavaScriptFunction` (`JavaScriptValue.cs:165`).
   - `using var candidate = value.Retain();`: `JavaScriptValue` (`JavaScriptValueRef.cs:146-147`).
   - `using var verdict = isPrototypeOf.CallWithThis(prototypeObject, candidate);`: `JavaScriptValue`. The receiver is the `JavaScriptObject` `prototypeObject`, not `prototypeValue`: `CallWithThis` takes `JavaScriptObject thisValue` (`JavaScriptFunction.cs:95-108`) and clones the receiver and the arguments for the call (`:110-111`).
   - `if (!(verdict.IsBool && verdict.AsBool())) throw NotUint8Array();`.
     No `constructor` read and no `StrictEquals`.
3. `var obj = value.AsObject();` (ref, `JavaScriptValueRef.cs:111-115`, no dispose). `var bufferRef = obj.GetProperty("buffer");` (ref) → `if (!bufferRef.IsArrayBuffer) throw NotUint8Array();`.
4. `var byteOffset = ReadIndex(obj.GetProperty("byteOffset"));` and `var byteLength = ReadIndex(obj.GetProperty("byteLength"));`, where `ReadIndex(JavaScriptValueRef v)` throws `NotUint8Array()` unless `v.IsDouble` and the double `d` satisfies `d >= 0 && d <= int.MaxValue && d == Math.Truncate(d)` (NaN fails), then returns `(int)d`. Checking `IsDouble` first avoids the native `Value is not a number.` from `AsDouble`.
5. `var buffer = ArrayBufferCodec.Decode(bufferRef, runtime);` (`ArrayBufferCodec.cs:10-18`; no copy of its body). Then `try { return new Uint8Array(buffer, byteOffset, byteLength); } catch { buffer.Dispose(); throw; }`.

Every check runs before step 5, so a rejected value never creates a lease. The kind proof costs three property reads and one call per decode, with no cached handle (spec decision 1 accepted the cost). The step-4 numbers and step-3 `buffer` come from refs on the value itself, so an accessor that a subclass inherits from `%TypedArray%.prototype` answers them.

Encode (owned locals, all `using`): `bufferValue = value.Buffer.Encode(runtime)` (`ArrayBuffer.cs:227-234`, internal, same assembly); `offset = runtime.CreateNumber(value.ByteOffset)`; `length = runtime.CreateNumber(value.Length)`; `global = runtime.Global()`; `constructorValue = global.GetProperty("Uint8Array")`; `function = constructorValue.AsFunction()` (a new owned `JavaScriptFunction`, `JavaScriptValue.cs:165`). Return `function.CallAsConstructor(bufferValue, offset, length)`. `CallAsConstructor` clones its arguments and disposes the clones (`JavaScriptFunction.cs:143-165`, `:202-231`); the caller still disposes its six locals and owns the result. For a JavaScript-backed view the result's `buffer` is the original JavaScript `ArrayBuffer` (identity), and the result is a new object.

Doc comments (D2): `GeneratedConversionScope.cs:5-9` becomes "A leaf is a `JavaScriptValue`, `ArrayBuffer`, or `Uint8Array` a scoped codec hands to the module ... as those three types are"; `IGeneratedScopedCodec.cs:7-8` becomes "A leaf is a `JavaScriptValue`, an `ArrayBuffer`, or a `Uint8Array`."

Test conventions: namespace `Expo.ModulesCore.Tests.Codecs`; `HermesRuntimeFixture` pattern of `ScopedCodecTests.cs`; JavaScript inputs come from `fixture.Evaluate(...)` inside `fixture.Runtime.Execute`; set globals with `global.SetProperty` as `ScopedCodecTests.cs:117-138` does. `R` below is `fixture.Counters.LongLivedArrayBuffersReleased` read after `fixture.WaitUntilIdle()`. `B16` is the JavaScript `globalThis.buf = new Uint8Array(16).map((_, i) => i).buffer` (bytes 0 to 15). The internal constructor is reachable through `InternalsVisibleTo` (`Expo.ModulesCore.csproj:13-15`).

- [ ] **Step 1: Write the failing tests** in `Uint8ArrayCodecTests.cs`:
  - `DecodeSharesBytesWithJavaScript`: `globalThis.v = new Uint8Array(4); v`; `using var view = Uint8ArrayCodec.Decode(source.Ref, runtime)`: `ByteOffset == 0`, `Length == 4`, `Buffer.ByteLength == 4`. `view.WithBytes(bytes => bytes.Fill(0x42))`; JavaScript `Array.from(v).join(',')` is `"66,66,66,66"`. JavaScript `v[1] = 7`; `view.ToArray()` is `{ 66, 7, 66, 66 }`.
  - `DecodeKeepsSubViewOffsetAndLength`: `B16`, then `globalThis.s = new Uint8Array(buf, 4, 8); s`. `ByteOffset == 4`, `Length == 8`, `Buffer.ByteLength == 16`, `ToArray()` is bytes 4 to 11, `WithReadOnlyBytes(b => b.Length) == 8`. `view.WithBytes(b => b[0] = 99)`; JavaScript `[new Uint8Array(buf)[3], new Uint8Array(buf)[4], new Uint8Array(buf)[12]].join(':')` is `"3:99:12"`.
  - `EmptyViewsDecodeAndEncode` (Review Focus 3): `new Uint8Array(0)` decodes to `ByteOffset 0`, `Length 0`, `ToArray()` empty. With `B16`, `new Uint8Array(buf, 16, 0)` decodes to `ByteOffset 16`, `Length 0`. Encode the second, set it as global `r`: `[r instanceof Uint8Array, r.byteOffset, r.length].join(':')` is `"true:16:0"`. `Uint8Array.CopyFrom(ReadOnlySpan<byte>.Empty)` has `Length 0`.
  - `EncodeBuildsAUint8ArrayOverTheSameBuffer`: `B16` and global `s` as above; decode `s`, `using var encoded = Uint8ArrayCodec.Encode(view, runtime)`, set as global `r`. `[r instanceof Uint8Array, r.constructor === Uint8Array, r.buffer === buf, r.byteOffset, r.length, r === s].join(':')` is `"true:true:true:4:8:false"` (the last value inferred: the constructor makes a new object).
  - `NativeViewsAllocateCopyRetainAndEncode`: `Uint8Array.Allocate(4)`: `ByteOffset 0`, `Length 4`, four zero bytes. `Uint8Array.Allocate(-1)` throws `ArgumentOutOfRangeException`. `var original = Uint8Array.CopyFrom(new byte[] { 1, 2, 3 })`; `using var copy = original.Retain()`; `original.Dispose()`; `copy.ToArray()` is `{ 1, 2, 3 }`, `copy.ByteOffset == 0`, `copy.Length == 3`. Encode `copy`, set as global `r`: `[r instanceof Uint8Array, r.byteOffset, r.length, Array.from(r).join(',')].join(':')` is `"true:0:3:1,2,3"`. `EncodeCopy(new byte[] { 5, 6 }, runtime)` set as `c`: `[c instanceof Uint8Array, c.byteOffset, c.length, c.buffer.byteLength, Array.from(c).join(',')].join(':')` is `"true:0:2:2:5,6"`.
  - `ViewSlicesNativeStorageSyncAndAsync` (async, no runtime frame; Review Focus 4): `var buffer = ArrayBuffer.CopyFrom(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 })`; `using var view = new Uint8Array(buffer, 2, 3)`. `ToArray()` is `{ 2, 3, 4 }`. `view.WithBytes(b => b[0] = 9)`; `buffer.ToArray()[2] == 9`. `await view.ToArrayAsync(ct)` is `{ 9, 3, 4 }`; `await view.WithReadOnlyBytesAsync(b => b.Length, ct) == 3`; `await view.WithBytesAsync(b => b[2] = 8, ct)`; `buffer.ToArray()[4] == 8`. Use `TestContext.Current.CancellationToken`, as `ArrayBufferCodecTests.cs:59-80` does.
  - `ViewConstructorChecksBounds`, theory `(int byteOffset, int length, bool valid)` over `ArrayBuffer.CopyFrom(new byte[4])`: `(0, 4, true)`, `(4, 0, true)`, `(-1, 0, false)`, `(0, -1, false)`, `(2, 3, false)`, `(int.MaxValue, 1, false)`. A valid row constructs a view with those values. An invalid row throws `ArgumentOutOfRangeException`, and the buffer stays usable (`buffer.ByteLength == 4`): the constructor does not take ownership when it throws.
  - `DisposedViewThrowsAndReleasesTheLeaseOnce`: decode `new Uint8Array(4)` with the ordinary `Decode`, inside `Execute`. After `Execute`: `ResetCounters()`, `view.Dispose()` twice (no throw), `WaitUntilIdle()`; `R == 1u`. `Buffer`, `ByteOffset`, `Length`, `ToArray()`, `Retain()`, and `WithBytes(_ => { })` each throw `ObjectDisposedException`.
  - `ScopedDecodeRegistersOneLease`: copy `ScopedCodecTests.ArrayBufferScopedDecodeRegistersOneLease` (`:96-115`) with `new Uint8Array(4)`; inside `Execute`, `decoded.Length == 4`. After `ResetCounters()`, `scope.Dispose()`, `WaitUntilIdle()`: `R == 1u` and `Assert.Throws<ObjectDisposedException>(() => _ = decoded.Length)`.
  - `ScopedDecodeIntoDisposedScopeReleasesTheLease`: pattern of `ScopedCodecTests.cs:39-56`. A scope disposed before the call; `Uint8ArrayCodec.Decode(source.Ref, runtime, scope)` throws `ObjectDisposedException`. After `Execute` and `WaitUntilIdle()`: `R == 1u` (the codec disposed the refused view).
  - `TransferAndEncodeBorrowedKeepTheView`: `Transfer(null!, scope)` does not throw. Decode `new Uint8Array(2)` with the ordinary `Decode`; `Transfer(view, scope)`; `EncodeBorrowed` twice, each result disposed; `view.Length == 2` afterwards. After `ResetCounters()`, `scope.Dispose()`, `WaitUntilIdle()`: `R == 1u`.
  - `DecodeRejectsEverythingButAUint8Array`, theory `(string input)`: `"new ArrayBuffer(4)"`, `"new Int32Array(2)"`, `"new DataView(new ArrayBuffer(4))"`, `"({})"`, `"5"`, `"null"`, `"undefined"`, `"({ constructor: Uint8Array })"` (Review Focus 2; its prototype is `Object.prototype`, so the kind test rejects it). Both ordinary `Decode` overloads and the scoped `Decode` throw `InvalidOperationException` whose message `Assert.Equal`s `Value is not a Uint8Array.`. After `ResetCounters()`, `scope.Dispose()`, `WaitUntilIdle()`: `R == 0u`.
  - `DecodeAcceptsAUint8ArraySubclassInstance` (Review Focus 1; inferred): declare `private const string Sub = "(() => { function Sub() {} Sub.prototype = Object.create(Uint8Array.prototype); Sub.prototype.constructor = Sub; const v = new Uint8Array(4); Object.setPrototypeOf(v, Sub.prototype); return v; })()";` (ES5, the form the `buffer` polyfill uses; no evaluated test script uses `class` syntax today). Evaluate `"globalThis.v = " + Sub + "; v"`; decode with the ordinary `Decode`: `ByteOffset == 0`, `Length == 4`. `view.WithBytes(b => b.Fill(0x42))`; JavaScript `[v instanceof Uint8Array, v.constructor === Uint8Array, Array.from(v).join(',')].join(':')` is `"true:false:66,66,66,66"`.
  - `DecodeAcceptsAPrototypeSpoofWithinItsBuffer` (Review Focus 2; inferred): `SPOOF(n)` is `Object.defineProperties(Object.create(Uint8Array.prototype), { buffer: { value: new Uint8Array([0, 1, 2, 3, 4, 5, 6, 7]).buffer }, byteOffset: { value: 2 }, byteLength: { value: n } })` (own data properties through `defineProperties`, because plain assignment would hit the inherited getter-only accessors). `SPOOF(4)` is accepted: `ByteOffset == 2`, `Length == 4`, `ToArray()` is `{ 2, 3, 4, 5 }`. `SPOOF(9)` throws `ArgumentOutOfRangeException`. `ResetCounters()` first; after the accepted view is disposed and `WaitUntilIdle()`: `R == 2u` (one entry per decode; the rejected decode's lease was disposed by the codec).
  - `DecodeRejectsShadowedViewFields` (Review Focus 2; all rows inferred), theory `(string input, bool outOfRange, uint released)`, each input `Object.defineProperty(new Uint8Array(4), <name>, { value: <v> })`: `('byteOffset', 1.5)` → `false, 0`; `('byteLength', -1)` → `false, 0`; `('buffer', 5)` → `false, 0`; `('byteOffset', 8)` → `true, 1`. `ResetCounters()` first. An `outOfRange` row throws `ArgumentOutOfRangeException`; the others throw `InvalidOperationException` with `Value is not a Uint8Array.`. After `WaitUntilIdle()`: `R == released` (the out-of-range row created a lease and the codec disposed it).

- [ ] **Step 2: Run the tests to verify they fail**

Run: ModulesCore tests with `--filter "FullyQualifiedName~Uint8ArrayCodecTests"`
Expected: build fails with CS0246 (`Uint8Array` or `Uint8ArrayCodec` not found). Put the observed errors in the task report. If the build fails for another reason, stop and report.

- [ ] **Step 3: Create the two files and edit the two doc comments** as the Interfaces block states.

- [ ] **Step 4: Run the Step 2 command.** Expected: PASS, 30 cases. A message or count that differs is a finding.

- [ ] **Step 5: Run both suites.** Run: Generator tests, then ModulesCore tests. Expected: all pass, no generated text changes, full suite count 1379.

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(modules-core): add Uint8Array views and codec" \
  -m "Uint8Array owns one ArrayBuffer lease plus an offset and a length and slices every byte access under the buffer's captured-length check. Uint8ArrayCodec has the ArrayBufferCodec shape: decode proves the kind with Uint8Array.prototype.isPrototypeOf, so subclass instances such as a Buffer polyfill are accepted, reads buffer, byteOffset, and byteLength, and retains the buffer without a copy; encode calls the global constructor over the encoded buffer. The scoped members register the view as the leaf." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 2: Generator resolves Uint8Array

Model: **standard**.

**Files:**

- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs` (constant beside `EitherMetadataName` at `:317`; branch after the `ArrayBuffer` branch at `:652-655`)
- Test: `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs` (new tests after `GeneratorAcceptsNullableArrayBufferOnSharedObjectMembers`, which ends at `:5225`)

**Interfaces:**

- Consumes: Task 1's `Uint8Array` and `Uint8ArrayCodec`.
- Produces: `private const string Uint8ArrayMetadataName = "global::Expo.ModulesCore.Uint8Array";` and, in `GetCodec` right after the `ArrayBuffer` branch, `if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == Uint8ArrayMetadataName) { return ScopedCodec("Uint8ArrayCodec"); }`. Nothing else in the generator changes: `Uint8Array?` takes the generic nullable-reference path (`Codecs.cs:300-308`), lists, dictionaries, and records compose through their descriptors, and `EventAnalysis.cs` stays as it is (D1).

Generated fragments below are the `ArrayBuffer` fragments of existing tests with `ArrayBuffer` replaced by `Uint8Array` (inferred by substitution; sources: `:297`, `:304-305`, `:432`, `:4801-4805`, `:4950-4954`, `:5025-5028`, `:5155-5160`, `:5173-5174`, `:5211-5213`, `:5234`). Declare beside `Nabc` (`:5152`): `private const string Nu8c = "GeneratedScopedNullableReferenceCodec<global::Expo.ModulesCore.Uint8Array, Uint8ArrayCodec>";` and `private const string U8 = "global::Expo.ModulesCore.Uint8Array";`, and build `InlineData` arguments by `const` concatenation.

- [ ] **Step 1: Write the acceptance tests.** Each theory asserts `Assert.Empty(GeneratorDiagnosticIds(result))` and `Assert.Contains(fragment, GeneratedText(result), StringComparison.Ordinal)`. `ModuleSource` rows also assert `AssertNoCompilerDiagnostics(result)`; `SharedObjectMemberSource` rows assert `AssertNoCompilerErrors(result)` and `Assert.DoesNotContain(result.Diagnostics, item => item.Id is "CS8604" or "CS8625")` (as `:5214-5223` does).
  - `GeneratorAcceptsUint8ArrayAtArrayBufferPositions`, theory `(string declarations, string member, string fragment)` over `ModuleSource`; every row also contains `new GeneratedConversionScope()`:
    1. `[JS] public int M(Uint8Array value) => 0;` → `var __expoArg0 = Uint8ArrayCodec.Decode(arguments.GetValue(0), runtime, __expoScope);`
    2. `[JS] public Task<int> M(Uint8Array value) => Task.FromResult(0);` → `var __expoArg0 = Uint8ArrayCodec.Decode(arguments.GetValue(0), jsRuntime, __expoScope);`
    3. `[JS] public Uint8Array M() => Uint8Array.Allocate(1);` → `Uint8ArrayCodec.Transfer(__expoResult, __expoScope);`
    4. `[JS] public Uint8Array M(Uint8Array value) => value;` → `return Uint8ArrayCodec.EncodeBorrowed(__expoResult, runtime);`
    5. `[JS] public Task<Uint8Array> M(Uint8Array value) => Task.FromResult(value);` → `return GeneratedTransferredResult<` + U8 + `, Uint8ArrayCodec>.Resolve(__expoResult, __expoScope);`
    6. `[JS] public Uint8Array Bytes { get; set; } = null!;` → `var __expoValue = Uint8ArrayCodec.Decode(arguments.GetValue(0), runtime, __expoScope);`
    7. the same member → `Uint8ArrayCodec.Transfer(__expoResult, __expoScope);`
    8. `[JS] public int M(IReadOnlyList<Uint8Array> values) => 0;` → `GeneratedScopedListCodec<` + U8 + `, Uint8ArrayCodec>.Decode(arguments.GetValue(0), runtime, __expoScope)`
    9. `[JS] public int M(Dictionary<string, Uint8Array> values) => 0;` → `GeneratedScopedDictionaryCodec<` + U8 + `, Uint8ArrayCodec>.Decode(`
    10. `[JS] public IReadOnlyDictionary<string, Uint8Array> M() => new Dictionary<string, Uint8Array>();` → `GeneratedScopedDictionaryCodec<` + U8 + `, Uint8ArrayCodec>.Transfer(__expoResult, __expoScope);`
    11. declarations `public sealed record Holder(Uint8Array Bytes, string Label);`, `[JS] public Holder M(Holder holder) => holder;` → `Uint8ArrayCodec.Decode(obj.GetProperty("bytes"), runtime, __expoScope)`
    12. same → `Uint8ArrayCodec.Transfer(value.Bytes, __expoScope);`
    13. same → `using var bytes = Uint8ArrayCodec.EncodeBorrowed(value.Bytes, runtime);`
  - `GeneratorAcceptsNullableUint8Array`, theory `(string declarations, string member, string fragment)` over `ModuleSource`; every row also contains `Nu8c`:
    1. `[JS] public int M(Uint8Array? value) => 0;` → `var __expoArg0 = ` + Nu8c + `.Decode(arguments.GetValue(0), runtime, __expoScope);`
    2. `[JS] public int M(Uint8Array? value = null) => 0;` → `arguments.Count <= 0 || arguments.GetValue(0).Kind == global::Expo.JSI.JavaScriptValueKind.Undefined ? null : ` + Nu8c + `.Decode(arguments.GetValue(0), runtime, __expoScope)`
    3. `[JS] public Uint8Array? M() => null;` → Nu8c + `.Transfer(__expoResult, __expoScope);`
    4. `[JS] public Uint8Array? M(Uint8Array? value) => value;` → `return ` + Nu8c + `.EncodeBorrowed(__expoResult, runtime);`
    5. `[JS] public Task<Uint8Array?> M() => Task.FromResult<Uint8Array?>(null);` → `GeneratedTransferredResult<global::Expo.ModulesCore.Uint8Array?, ` + Nu8c + `>.Resolve(__expoResult, __expoScope);`
    6. `[JS] public Uint8Array? Bytes { get; set; }` → `var __expoValue = ` + Nu8c + `.Decode(arguments.GetValue(0), runtime, __expoScope);`
    7. `[JS] public int M(IReadOnlyList<Uint8Array?> values) => 0;` → `GeneratedScopedListCodec<global::Expo.ModulesCore.Uint8Array?, ` + Nu8c + `>.Decode(arguments.GetValue(0), runtime, __expoScope)`
    8. declarations `public sealed record Holder(Uint8Array? Bytes);`, `[JS] public Holder M(Holder holder) => holder;` → Nu8c + `.Decode(obj.GetProperty("bytes"), runtime, __expoScope)`
  - `GeneratorAcceptsUint8ArrayOnSharedObjectMembers`, theory `(string member, string fragment)` over `SharedObjectMemberSource(member)`:
    1. `[JS] public Entry(Uint8Array seed) { }` → `var __expoArg0 = Uint8ArrayCodec.Decode(arguments.GetValue(0), runtime, __expoScope);`
    2. `[JS] public int Measure(Uint8Array value) => 0;` → the same fragment
    3. `[JS] public Task<int> MeasureLater(Uint8Array value) => Task.FromResult(0);` → `var __expoArg0 = Uint8ArrayCodec.Decode(arguments.GetValue(0), jsRuntime, __expoScope);`
    4. `[JS] public Uint8Array? Bytes { get; set; }` → Nu8c + `.Transfer(__expoResult, __expoScope);`

- [ ] **Step 2: Write the rejection tests.** Each row asserts `AssertNoCompilerErrors(result)`, the exact ID list, and that the generated text does not contain the member's quoted JavaScript name.
  - `GeneratorRejectsUint8ArrayWhereArrayBufferIsRejected`, theory `(bool onSharedObject, string member, string expectedId, string jsName, string? messageFragment)` over `SharedObjectMemberSource(member)` or `ModuleSource(string.Empty, member)`. When `messageFragment` is not null, the single diagnostic's message contains it.
    1. `false`, `[JS] public int M(JavaScriptCallback<Uint8Array> callback) => 0;`, `EXPOJSI008`, `m`, null
    2. `false`, `[JS] public int M(JavaScriptCallback<ValueTuple<Uint8Array>, int> callback) => 0;`, `EXPOJSI008`, `m`, null
    3. `true`, `[JS] public void Store(JavaScriptCallback<Uint8Array> callback) { }`, `EXPOJSI023`, `store`, null
    4. `false`, `[JS] public int M(Either<string, Uint8Array> value) => 0;`, `EXPOJSI001`, `m`, `'global::Expo.ModulesCore.Either<string, global::Expo.ModulesCore.Uint8Array>'` (spec decision 6; the one row where `ArrayBuffer` is accepted and `Uint8Array` is not)
    5. `true`, `[JS] public int Pick(Either<string, Uint8Array> value) => 0;`, `EXPOJSI023`, `pick`, `parameter 'value' uses unsupported type 'global::Expo.ModulesCore.Either<string, global::Expo.ModulesCore.Uint8Array>'`

    Rows 1-3 mirror the `ScopedOnly` rule of the callback position (`Codecs.cs:84-92`); the union rows mirror `GeneratorRejectsUnsupportedUnionMembers` (`:5283-5312`) and its shared-object form (`:5314-5319`). All inferred.

  - `GeneratorKeepsUint8ArrayOutOfTypedEvents` (D1; all rows inferred), theory `(string payload, bool onSharedObject, string expectedId)` with member `$"[Event] public partial Func<{payload}, Task> OnBytes {{ get; }}"`: `("Uint8Array", false, "EXPOJSI019")`, `("Uint8Array", true, "EXPOJSI027")`, `("Uint8Array?", false, "EXPOJSI019")`, `("IReadOnlyList<Uint8Array>", false, "EXPOJSI019")`. Assert the single diagnostic's ID, that its message contains `no encode-capable codec is available` (`EventAnalysis.cs:228`), that the text does not contain `"onBytes"`, and `AssertNoCompilerErrors(result)`.

- [ ] **Step 3: Run the new tests to verify state**

Run: Generator tests with `--filter "FullyQualifiedName~Uint8Array"`
Expected: every row of the three `GeneratorAccepts*Uint8Array*` theories fails, because `Uint8Array` has no codec yet and the member reports a diagnostic. Every rejection row passes: an unresolved `Uint8Array` already reports the same IDs and the same event reason (inferred). Put the observed failures in the task report. If a rejection row fails, stop and report.

- [ ] **Step 4: Add the constant and the `GetCodec` branch** as the Interfaces block states.

- [ ] **Step 5: Run the Step 3 command.** Expected: PASS, 34 cases.

- [ ] **Step 6: Run everything.** Run: Generator tests, ModulesCore tests, then `scripts/test-managed.sh`. Expected: all pass, 0 skipped, full suite 1413. Every `GeneratorPreserves*` and `GeneratorPins*` contract test passes unchanged. If any existing test fails, stop: unrelated text or policy changed.

- [ ] **Step 7: Run the 029 typed-policy scan** from Global Constraints. Expected: it prints nothing.

- [ ] **Step 8: Commit**

```bash
git commit -m "feat(generator): resolve Uint8Array as a scoped codec" \
  -m "Uint8Array resolves to the scoped Uint8ArrayCodec next to ArrayBufferCodec, so parameters, returns, async results, properties, shared-object members, Uint8Array?, lists, dictionaries, and record fields compose through the existing descriptors. Callback type arguments, union members, and typed-event payloads stay rejected: the event path classifies ArrayBuffer by name, so typed arrays are out of scope there." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 3: Prove Uint8Array codecs on Hermes

Model: **standard**.

This task adds fixtures and tests only. Production code is complete after Task 2, so these tests are expected to pass on the first run. A failure is a finding: stop and report it with the observed values. Do not change production code or adjust an expectation.

**Files:**

- Modify: `Expo.ModulesCore.Tests/Modules/ResourceScopeModuleFixtures.cs` (append after `ResourceScopeOptionalEntry`, which ends at line 356)
- Test: `Expo.ModulesCore.Tests/Modules/ResourceScopeModuleTests.cs` (insert before the private `ResourceScopeFixture` class at line 1125)

**Interfaces:**

- Consumes: Task 1's types, Task 2's generator support.
- Produces (fixtures, namespace `Expo.ModulesCore.Tests.Modules`; JavaScript names are the lower-camel defaults):

```csharp
public sealed record ViewHolder(Uint8Array Bytes, string Label);

[ExpoSharedObject]
public sealed partial class TypedArrayEntry : SharedObject
{
  private readonly string seed;
  [JS] public TypedArrayEntry(Uint8Array seed)   // this.seed = TypedArrayThingsModule.Describe(seed);
  [JS] public string ReadSeed() => seed;
  [JS] public string Describe(Uint8Array bytes) => TypedArrayThingsModule.Describe(bytes);
}

// Owns TypedArrayEntry, so it does not report EXPOJSI024. No other module may list it.
[ExpoModule("TypedArrayThings", Classes = new[] { typeof(TypedArrayEntry) })]
public sealed partial class TypedArrayThingsModule : Module, IDisposable
{
  private Uint8Array? held;
  private Uint8Array? stored;
  public TypedArrayThingsModule(DotnetRuntimeContext context) : base(context) { }

  // Counts every authored read of a view, so a test proves a failed decode never reached authored code.
  public static int Calls;

  // "{ByteOffset}:{Length}:{uppercase hex}". Calls++. Synchronous, so runtime thread only.
  public static string Describe(Uint8Array bytes)   // Convert.ToHexString(bytes.ToArray())

  [JS] public void Fill(Uint8Array bytes)                              // Calls++; bytes.WithBytes(span => span.Fill(0x42));
  [JS] public string FillOptional(Uint8Array? bytes)                   // null → "null", without Calls++; else Describe(bytes)
  [JS] public int FailAfterRead(Uint8Array bytes)                      // Calls++; throw new InvalidOperationException("view failed");
  [JS] public string Hex(Uint8Array bytes) => Describe(bytes);
  [JS] public async Task<string> HexLater(Uint8Array bytes)            // await Task.Yield(); var copy = await bytes.ToArrayAsync(); Calls++; same format as Describe
  [JS] public Uint8Array Make() => Uint8Array.CopyFrom(new byte[] { 1, 2, 3 });
  [JS] public Uint8Array Echo(Uint8Array bytes) => bytes.Retain();
  [JS] public async Task<Uint8Array> EchoLater(Uint8Array bytes)       // await Task.Yield(); return bytes.Retain();
  [JS] public Uint8Array? Stored { get => stored?.Retain(); set { var copy = value?.Retain(); stored?.Dispose(); stored = copy; } }
  [JS] public string HexAll(IReadOnlyList<Uint8Array> parts) => string.Join("|", parts.Select(Describe));
  [JS] public IReadOnlyList<Uint8Array> EchoList(IReadOnlyList<Uint8Array> views) => views;
  [JS] public Dictionary<string, Uint8Array> EchoMap(Dictionary<string, Uint8Array> views) => views;
  [JS] public ViewHolder EchoHolder(ViewHolder holder) => holder;
  [JS] public void Hold(Uint8Array bytes)                              // held?.Dispose(); held = bytes.Retain();
  [JS] public string ReadHeld() => held is null ? "none" : Describe(held);
  [JS] public void ReleaseHeld()                                       // held?.Dispose(); held = null;
  [JS] public int TakeBuffer(ArrayBuffer buffer) => buffer.ByteLength;
  [JS] public int TakeBytes(byte[] bytes) => bytes.Length;
  public void Dispose()                                                // dispose held and stored
}
```

`Stored` copies the `OptionalBuffer` property pattern (`ResourceScopeModuleFixtures.cs:255-266`); the module registry disposes `IDisposable` modules (`ModuleRegistry.cs:135`).

Common setup: the private `ResourceScopeFixture` (`ResourceScopeModuleTests.cs:1125-1258`) registers the whole generated provider, so `TypedArrayThings` is already installed. Declare `private const string TypedArraySetup = "globalThis.t = globalThis._expoDotnet.modules.TypedArrayThings; globalThis.buf = new Uint8Array(16).map((_, i) => i).buffer; globalThis.s = new Uint8Array(buf, 4, 8); true";`. Each test starts with `using var scope = ResourceScopeFixture.Create(); scope.Run(TypedArraySetup); TypedArrayThingsModule.Calls = 0;`. "Count `n`" below means: `scope.Fixture.ResetCounters()` before the call, `scope.Fixture.WaitUntilIdle()` after it, then `Assert.Equal(n, scope.Fixture.Counters.LongLivedArrayBuffersReleased)`. A setup step that decodes a view calls `scope.Fixture.WaitUntilIdle()` before the first reset, because lease releases land asynchronously. `S8` is the hex of bytes 4 to 11, `0405060708090A0B`. Every count is inferred from the entry rule and the 031/035 tests it mirrors. `NotU8` is `Value is not a Uint8Array.`.

- [ ] **Step 1: Add the fixtures** above.

- [ ] **Step 2: Write the tests** in `ResourceScopeModuleTests`:
  - `Uint8ArrayFillWritesInPlaceAndReleasesOnce` (spec scenario "In-place write"): `globalThis.v = new Uint8Array(4); t.fill(v); Array.from(v).join(',')` is `"66,66,66,66"`, count 1. `t.fill(s); Array.from(new Uint8Array(buf)).join(',')` is `"0,1,2,3,66,66,66,66,66,66,66,66,12,13,14,15"`, count 1. Review Focus 1: with the Task 1 `Sub` expression declared here as a `const string`, `globalThis.w = " + Sub + "; t.fill(w); Array.from(w).join(',')` is `"66,66,66,66"`, count 1 (inferred). `Calls == 3`.
  - `Uint8ArraySubViewSeesOnlyItsSliceSyncAndAsync` (spec scenario "Sub-view"; Review Focus 4): `t.hex(s)` is `"4:8:" + S8`, count 1. `scope.StartOutcome("t.hexLater(s)")`, `await scope.WaitForOutcomeAsync()`; `` `${__outcome.status}:${__outcome.value}` `` is `"fulfilled:4:8:" + S8`, count 1.
  - `Uint8ArrayReturnsAndPropertiesEncodeViews` (spec scenario "Return and property"):
    - `globalThis.r = t.make(); [r instanceof Uint8Array, r.byteOffset, r.length, Array.from(r).join(',')].join(':')` is `"true:0:3:1,2,3"` (no count: native-backed). `t.hex(t.make())` is `"0:3:010203"` (a native-backed view decodes back).
    - `globalThis.e = t.echo(s); [e instanceof Uint8Array, e === s, e.buffer === buf, e.byteOffset, e.length].join(':')` is `"true:false:true:4:8"`, count 1.
    - `scope.StartOutcome("t.echoLater(s)")`, await; `` `${__outcome.status}:${__outcome.value instanceof Uint8Array}:${__outcome.value.buffer === buf}:${__outcome.value.byteOffset}:${__outcome.value.length}` `` is `"fulfilled:true:true:4:8"`, count 1.
    - `String(t.stored)` is `"null"`, count 0. `t.stored = s; const p = t.stored; [p instanceof Uint8Array, p.buffer === buf, p.byteOffset, p.length].join(':')` is `"true:true:4:8"`, count 0 (the stored copy pins the entry, as `NullableBufferPropertyStoresNullOrRetainedCopy`, `:751-774`). `t.stored = null; String(t.stored)` is `"null"`, count 1.
  - `Uint8ArrayParametersRejectWrongShapes` (spec scenario "Wrong shapes are rejected"), theory `(string input, bool nullish)`: `("new ArrayBuffer(4)", false)`, `("new Int32Array(2)", false)`, `("new DataView(new ArrayBuffer(4))", false)`, `("({})", false)`, `("5", false)`, `("null", true)`, `("undefined", true)`. Setup also evaluates `globalThis.e = new t.TypedArrayEntry(s); true` and then calls `scope.Fixture.WaitUntilIdle()`, so the constructor's lease release lands before the counter reset; then set `Calls = 0` and read `var before = scope.Fixture.Runtime.Execute(_ => scope.Context.SharedObjects.Count)`. Evaluate, with count 0: `"const v = () => (" + input + "); const err = f => { try { return 'ok:' + f(); } catch (error) { return error instanceof Error ? error.message : 'not an Error'; } }; [err(() => t.fill(v())), err(() => e.describe(v())), err(() => new t.TypedArrayEntry(v())), err(() => t.fillOptional(v()))].join('|')"`. Split on `|`: 4 parts. The first three contain `NotU8`. The fourth is `"ok:null"` when `nullish`, else it contains `NotU8`. `Calls == 0`; `SharedObjects.Count == before` (inferred).
  - `Uint8ArrayCompositionReleasesEachLeafOnce` (spec scenario "Composition follows ArrayBuffer"):
    - `t.hexAll([new Uint8Array(buf, 0, 2), new Uint8Array(new ArrayBuffer(1))])` is `"0:2:0001|0:1:00"`, count 2.
    - `const l = t.echoList([s, s]); [l.length, l[0].buffer === buf, l[1].byteOffset, l[1].length].join(':')` is `"2:true:4:8"`, count 2 (two decodes of one buffer are two entries).
    - `const m = t.echoMap({ a: s, b: new Uint8Array(2) }); [m.a.buffer === buf, m.a.byteOffset, m.b.length].join(':')` is `"true:4:2"`, count 2.
    - `const h = t.echoHolder({ bytes: s, label: 'x' }); [h.label, h.bytes.buffer === buf, h.bytes.byteOffset, h.bytes.length].join(':')` is `"x:true:4:8"`, count 1.
    - `globalThis.e = new t.TypedArrayEntry(s); [e.readSeed(), e.describe(s)].join('|')` is `"4:8:" + S8 + "|4:8:" + S8`, count 2 (the constructor decode and the method decode are two entries).
  - `RetainedUint8ArrayOutlivesTheCall` (spec "a `Retain()` copy outlives the call"): `t.hold(s); 'held'`, count 0. `s[0] = 99; t.readHeld()` is `"4:8:6305060708090A0B"` (the held copy shares bytes). `t.releaseHeld(); t.readHeld()` is `"none"`, count 1.
  - `ExactBinaryParametersStillRejectUint8Array` (spec decision 5): `t.takeBuffer(new Uint8Array(4))` and `t.takeBytes(new Uint8Array(4))`, each in its own `try`/`catch` returning `error.message`, contain `Value is not a JavaScript ArrayBuffer.` (`Expo.JSI/JavaScriptValueRef.cs:97-100`), count 0. Control: `String(t.takeBuffer(new Uint8Array(4).buffer))` is `"4"`.
  - `Uint8ArrayLeaseReleasesWhenAuthoredCodeThrows` (Review Focus 5): `let failed; try { t.failAfterRead(s); failed = 'no error'; } catch (error) { failed = error.message; } String(failed)` contains `view failed`, count 1, `Calls == 1`.

- [ ] **Step 3: Run the new tests**

Run: ModulesCore tests with `--filter "FullyQualifiedName~ResourceScopeModuleTests"`
Expected: PASS; the new tests add 14 cases. A failing string, count, or message is a finding to report with the observed value.

- [ ] **Step 4: Run everything.** Run: `scripts/test-managed.sh`. Expected: all pass, 0 skipped, 1427 passed. Record the count for Task 4.

- [ ] **Step 5: Commit**

```bash
git commit -m "test(modules-core): prove Uint8Array codecs on Hermes" \
  -m "A fixture module and shared object take Uint8Array in sync, async, nullable, list, dictionary, record, property, and constructor positions. Tests prove that authored writes land in the JavaScript view, that a sub-view sees only its slice in sync and async code, that returned views keep the original buffer with the same offset and length, that wrong shapes fail with one message before authored code runs, that each decoded view releases its lease once, that a retained copy outlives the call, and that ArrayBuffer and byte[] parameters still reject a view." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 4: Close the change

Model: **cheap**.

**Files:**

- Modify: `docs/specs/modules-core-boundary.md`, `docs/specs/ownership-and-scoped-refs.md`, `docs/module-authoring-guide.md`, `docs/roadmap.md`
- Move: `docs/plans/036-typed-array-codecs.md` → `docs/archive/plans/`
- Move: `docs/changes/2026-11-06-typed-array-codecs/` → `docs/archive/changes/`

- [ ] **Step 1: Merge the delta into `docs/specs/modules-core-boundary.md`:**
  - Insert `### Requirement: Uint8Array Is A Scoped Byte View` with its five scenarios, verbatim from the spec's "ADDED Requirement" section, right before `### Requirement: Two-Member Unions Decode By Value Kind` (line 1709), so it follows the binary-codec requirement. D1: in its first sentence, after "wherever `ArrayBuffer` is", insert ", except as a direct typed-event payload"; say so in the task report. If the controller struck D1, merge verbatim.
  - Binary codec table (1660-1665): after the `ArrayBuffer` row (1662), add ``| `Uint8Array` | A view over one retained buffer lease with its offset and length; no byte copy in either direction | The `ArrayBuffer` positions, each also as `Uint8Array?`, except direct typed-event payloads; not a callback type argument or a union member |``.
  - Descriptor table (1531-1542): after the `` `ArrayBuffer` `` row (1535), add ``| `Uint8Array` | `ScopedOnly` | `ScopedResource` |``.

- [ ] **Step 2: Merge into `docs/specs/ownership-and-scoped-refs.md`:**
  - `### Requirement: Binary Borrowing And Ownership` (105-114): after its first sentence, insert "A `Uint8Array` decoded from JavaScript SHALL hold one retained lease on the JavaScript buffer, with no byte copy, and its byte callbacks SHALL borrow only its slice for the duration of the synchronous callback."
  - `### Requirement: Conversion Scope Owns Boundary Wrapper Leaves` (155-172): "every `JavaScriptValue` and `ArrayBuffer` leaf" becomes "every `JavaScriptValue`, `ArrayBuffer`, and `Uint8Array` leaf" (157); "which `JavaScriptValue` and `ArrayBuffer` are" becomes "which `JavaScriptValue`, `ArrayBuffer`, and `Uint8Array` are" (161-162); "Only `JavaScriptValueCodec` and `ArrayBufferCodec` SHALL register leaves." becomes "Only `JavaScriptValueCodec`, `ArrayBufferCodec`, and `Uint8ArrayCodec` SHALL register leaves; a `Uint8Array` leaf holds one buffer lease." (165-166).

- [ ] **Step 3: Update `docs/module-authoring-guide.md`**, the binary-data bullet (143-151). Before "See the", add: "`Uint8Array` is a view over a JavaScript buffer with its offset and length, and nothing is copied: your writes are visible to JavaScript after the call. It works where `ArrayBuffer` does, also as `Uint8Array?`, except as a direct typed-event payload, and any `Uint8Array` instance, including a subclass such as a `Buffer` polyfill, is accepted. `byte[]` and `ArrayBuffer` parameters do not accept a `Uint8Array`; declare `Uint8Array` when JavaScript sends one."

- [ ] **Step 4: Update `docs/roadmap.md`.** Row 036 (line 61): `DONE`, link `[036](archive/plans/036-typed-array-codecs.md)`; keep the column widths aligned. After the `**035**` note (117-124), add: "- **036**: done ([change](archive/changes/2026-11-06-typed-array-codecs/spec.md)): it landed on <date> with a local `scripts/test-managed.sh` run (<N> passed, 0 failed, 0 skipped). `Uint8Array` now crosses the boundary as a view over one buffer lease with no byte copy, wherever `ArrayBuffer` does except as a direct typed-event payload, so 038 can port `bytes()` and 025 can fill a caller's array. Typed arrays needed no ABI entry: the kind proof, the buffer, the offset and length, and the construction are property reads and one constructor call that `Expo.JSI` already had." Take `<N>` from Step 6 and `<date>` from the commit date.

- [ ] **Step 5: Move the brief and the change folder** with `git mv`. Fix every Markdown link to the old paths: `rg -n '036-typed-array-codecs|2026-11-06-typed-array-codecs' docs .agents AGENTS.md`. Every link left points into `archive/`. Plain-text path mentions inside the archived brief, spec, and plan are history and stay.

- [ ] **Step 6: Verify** with the spec's block:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
git diff --stat babcba8..HEAD -- packages/expo-modules-dotnet/native
rg -n 'Uint8Array' packages/expo-modules-dotnet/native
git diff --unified=0 babcba8..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

Expected: all tests pass with 0 skipped and 1427 passed (1349 + 30 + 34 + 14; put the arithmetic in the report). Format is OK and `git diff --check` is clean. The native diff and both scans print nothing.

- [ ] **Step 7: Commit**

```bash
git commit -m "docs(modules-core): close Uint8Array codecs" \
  -m "Merge the accepted delta into the living specs and the authoring guide, archive the brief and the change folder, and mark roadmap item 036 DONE." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```
