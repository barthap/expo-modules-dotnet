## Expo.JSI Ownership Mental Model

Do not think of `JavaScriptValue` / `JavaScriptObject` / `JavaScriptArray` as
"owning the JavaScript object."

Think of them as **owned receipts**.

They own a native bridge handle that keeps a usable JSI value/object reference
alive for C#. Because that receipt is a real native resource, it must eventually
be disposed.

Disposing the C# wrapper closes C#'s bridge handle. It does not necessarily
delete the JavaScript object. JavaScript may still keep that object alive.

## Owned Wrappers

`JavaScriptValue`, `JavaScriptObject`, and `JavaScriptArray` are owned wrappers.

Rule:

> If an API returns an owned wrapper, the caller must dispose it.

Examples:

```csharp
using var value = runtime.CreateString("hello");
using var obj = value.AsObject();
using var prop = obj.GetProperty("name");
```

Owned APIs return values that may escape the current expression, method, or
callback, as long as you keep the wrapper alive and dispose it later.

## Scoped Refs

`JavaScriptValueRef`, `JavaScriptObjectRef`, and `JavaScriptArrayRef` are
borrowed views.

They are for temporary traversal while already inside an active JavaScript
runtime call/frame.

They are not disposed.
They are not stored.
They are not captured.
They are not returned.

Rule:

> A ref is only for "I am looking at this value right now."

Example:

```csharp
var name = value.Ref.AsObject().GetProperty("name").AsString();
```

No `Dispose` is needed here because the ref does not own anything.

## Moving From Borrowed To Owned

`Retain()` is the door from borrowed world back to owned world.

Use it when a value obtained through refs must outlive the current traversal,
callback, or runtime frame.

Example:

```csharp
using var ownedName = value.Ref
  .AsObject()
  .GetProperty("name")
  .Retain();
```

After `Retain()`, you have an owned `JavaScriptValue`, so normal dispose rules
apply again.

## Decision Rule

Ask:

> Do I need this value after the current expression/block/callback returns?

If no, use refs:

```csharp
var text = value.Ref.AsObject().GetProperty("name").AsString();
```

If yes, retain or use an owned API:

```csharp
using var textValue = value.Ref.AsObject().GetProperty("name").Retain();
```

## API Shape

Owned receiver returns owned result:

```csharp
JavaScriptValue.AsObject()      // returns JavaScriptObject
JavaScriptObject.GetProperty()  // returns JavaScriptValue
```

The caller disposes the result.

Ref receiver returns ref result:

```csharp
JavaScriptValueRef.AsObject()       // returns JavaScriptObjectRef
JavaScriptObjectRef.GetProperty()   // returns JavaScriptValueRef
```

No dispose. The result is borrowed and scoped.

## The Whole Model

Owned wrapper:

> I own this C# bridge handle to a JavaScript value.

Scoped ref:

> I am temporarily looking through someone else's handle/frame.

That is the core ownership model. Everything else is API ergonomics around those
two states.
