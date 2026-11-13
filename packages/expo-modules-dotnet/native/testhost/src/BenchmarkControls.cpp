#include "BenchmarkControls.h"

#include <cstddef>
#include <cstdint>
#include <string>

namespace jsi = facebook::jsi;

namespace {

// Reads land in volatile locals so the compiler cannot drop the traversal.
volatile double sinkNumber = 0;
volatile bool sinkBoolean = false;
volatile std::size_t sinkSize = 0;
volatile std::uint8_t sinkByte = 0;

constexpr const char *kString16 = "abcdefghijklmnop";

[[noreturn]] void fail(jsi::Runtime &runtime, const char *control, const char *expected)
{
  throw jsi::JSError(runtime, std::string(control) + " expects " + expected + ".");
}

const jsi::Value &argument(jsi::Runtime &runtime,
                           const char *control,
                           const jsi::Value *arguments,
                           size_t count,
                           size_t index)
{
  if (index >= count) {
    fail(runtime, control, "more arguments");
  }
  return arguments[index];
}

double readNumber(jsi::Runtime &runtime, const char *control, const jsi::Value &value)
{
  if (!value.isNumber()) {
    fail(runtime, control, "a number");
  }
  const double result = value.getNumber();
  sinkNumber = result;
  return result;
}

bool readBoolean(jsi::Runtime &runtime, const char *control, const jsi::Value &value)
{
  if (!value.isBool()) {
    fail(runtime, control, "a boolean");
  }
  const bool result = value.getBool();
  sinkBoolean = result;
  return result;
}

void readString(jsi::Runtime &runtime, const char *control, const jsi::Value &value)
{
  if (!value.isString()) {
    fail(runtime, control, "a string");
  }
  const std::string text = value.getString(runtime).utf8(runtime);
  sinkSize = text.size();
}

jsi::Object readObject(jsi::Runtime &runtime, const char *control, const jsi::Value &value)
{
  if (!value.isObject()) {
    fail(runtime, control, "an object");
  }
  return value.getObject(runtime);
}

jsi::Array readArray(jsi::Runtime &runtime, const char *control, const jsi::Value &value)
{
  if (!value.isObject()) {
    fail(runtime, control, "an array");
  }
  jsi::Object object = value.getObject(runtime);
  if (!object.isArray(runtime)) {
    fail(runtime, control, "an array");
  }
  return object.getArray(runtime);
}

size_t readCount(jsi::Runtime &runtime, const char *control, const jsi::Value &value)
{
  const double count = readNumber(runtime, control, value);
  if (!(count >= 0) || count > 1e9) {
    fail(runtime, control, "a non-negative count");
  }
  return static_cast<size_t>(count);
}

std::string indexed(const char *prefix, size_t index)
{
  return prefix + std::to_string(index);
}

void readRecord4(jsi::Runtime &runtime, const char *control, const jsi::Value &value)
{
  jsi::Object object = readObject(runtime, control, value);
  for (const char *key : {"a", "b", "c", "d"}) {
    readNumber(runtime, control, object.getProperty(runtime, key));
  }
}

void readRecord12(jsi::Runtime &runtime, const char *control, const jsi::Value &value)
{
  jsi::Object object = readObject(runtime, control, value);
  for (size_t i = 1; i <= 6; ++i) {
    readNumber(runtime, control, object.getProperty(runtime, indexed("n", i).c_str()));
  }
  for (size_t i = 1; i <= 6; ++i) {
    readString(runtime, control, object.getProperty(runtime, indexed("s", i).c_str()));
  }
}

jsi::Object makeRecord4(jsi::Runtime &runtime)
{
  jsi::Object object(runtime);
  object.setProperty(runtime, "a", 1);
  object.setProperty(runtime, "b", 2);
  object.setProperty(runtime, "c", 3);
  object.setProperty(runtime, "d", 4);
  return object;
}

jsi::Object makeRecord12(jsi::Runtime &runtime)
{
  jsi::Object object(runtime);
  for (size_t i = 1; i <= 6; ++i) {
    object.setProperty(runtime, indexed("n", i).c_str(), static_cast<double>(i));
  }
  for (size_t i = 1; i <= 6; ++i) {
    const std::string text = indexed("s", i);
    object.setProperty(runtime, text.c_str(), jsi::String::createFromAscii(runtime, text));
  }
  return object;
}

void readBytes(jsi::Runtime &runtime,
               const char *control,
               jsi::ArrayBuffer &buffer,
               size_t offset,
               size_t length)
{
  const size_t size = buffer.size(runtime);
  if (offset > size || length > size - offset) {
    fail(runtime, control, "a view inside its buffer");
  }
  sinkSize = length;
  if (length > 0) {
    const std::uint8_t *data = buffer.data(runtime);
    sinkByte = data[offset];
    sinkByte = data[offset + length - 1];
  }
}

jsi::Value construct(jsi::Runtime &runtime, const char *constructor, double length)
{
  return runtime.global()
    .getPropertyAsFunction(runtime, constructor)
    .callAsConstructor(runtime, length);
}

template <typename F>
void define(
  jsi::Runtime &runtime, jsi::Object &target, const char *name, unsigned paramCount, F &&body)
{
  target.setProperty(
    runtime,
    name,
    jsi::Function::createFromHostFunction(
      runtime,
      jsi::PropNameID::forAscii(runtime, name),
      paramCount,
      [name, body = std::forward<F>(body)](
        jsi::Runtime &rt, const jsi::Value &, const jsi::Value *args, size_t count) -> jsi::Value {
        return body(rt, name, args, count);
      }));
}

} // namespace

void installBenchmarkControls(jsi::Runtime &runtime)
{
  jsi::Object controls(runtime);

  define(
    runtime, controls, "noop", 0, [](jsi::Runtime &, const char *, const jsi::Value *, size_t) {
      return jsi::Value::undefined();
    });

  define(runtime,
         controls,
         "addDoubles",
         2,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           const double a = readNumber(rt, name, argument(rt, name, args, count, 0));
           const double b = readNumber(rt, name, argument(rt, name, args, count, 1));
           return jsi::Value(a + b);
         });

  define(runtime,
         controls,
         "echoBoolean",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           return jsi::Value(readBoolean(rt, name, argument(rt, name, args, count, 0)));
         });
  define(runtime,
         controls,
         "echoInt",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           return jsi::Value(readNumber(rt, name, argument(rt, name, args, count, 0)));
         });
  define(runtime,
         controls,
         "echoDouble",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           return jsi::Value(readNumber(rt, name, argument(rt, name, args, count, 0)));
         });

  define(runtime,
         controls,
         "takeString",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           readString(rt, name, argument(rt, name, args, count, 0));
           return jsi::Value::undefined();
         });
  define(runtime,
         controls,
         "makeString",
         0,
         [](jsi::Runtime &rt, const char *, const jsi::Value *, size_t) {
           return jsi::Value(jsi::String::createFromAscii(rt, kString16));
         });

  define(runtime,
         controls,
         "takeRecord4",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           readRecord4(rt, name, argument(rt, name, args, count, 0));
           return jsi::Value::undefined();
         });
  define(runtime,
         controls,
         "makeRecord4",
         0,
         [](jsi::Runtime &rt, const char *, const jsi::Value *, size_t) {
           return jsi::Value(makeRecord4(rt));
         });

  define(runtime,
         controls,
         "takeRecord12",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           readRecord12(rt, name, argument(rt, name, args, count, 0));
           return jsi::Value::undefined();
         });
  define(runtime,
         controls,
         "makeRecord12",
         0,
         [](jsi::Runtime &rt, const char *, const jsi::Value *, size_t) {
           return jsi::Value(makeRecord12(rt));
         });

  define(runtime,
         controls,
         "takeNested48",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           jsi::Object object = readObject(rt, name, argument(rt, name, args, count, 0));
           for (size_t i = 1; i <= 4; ++i) {
             readRecord12(rt, name, object.getProperty(rt, indexed("r", i).c_str()));
           }
           return jsi::Value::undefined();
         });
  define(runtime,
         controls,
         "makeNested48",
         0,
         [](jsi::Runtime &rt, const char *, const jsi::Value *, size_t) {
           jsi::Object object(rt);
           for (size_t i = 1; i <= 4; ++i) {
             object.setProperty(rt, indexed("r", i).c_str(), makeRecord12(rt));
           }
           return jsi::Value(std::move(object));
         });

  define(runtime,
         controls,
         "takeDictionary",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           jsi::Object object = readObject(rt, name, argument(rt, name, args, count, 0));
           jsi::Array keys = object.getPropertyNames(rt);
           const size_t length = keys.size(rt);
           for (size_t i = 0; i < length; ++i) {
             jsi::String key = keys.getValueAtIndex(rt, i).getString(rt);
             std::string text = key.utf8(rt);
             sinkSize = text.size();
             readNumber(rt, name, object.getProperty(rt, key));
           }
           return jsi::Value::undefined();
         });
  define(runtime,
         controls,
         "makeDictionary",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           const size_t length = readCount(rt, name, argument(rt, name, args, count, 0));
           jsi::Object object(rt);
           for (size_t i = 0; i < length; ++i) {
             object.setProperty(rt, indexed("k", i).c_str(), static_cast<double>(i));
           }
           return jsi::Value(std::move(object));
         });

  define(runtime,
         controls,
         "takeNumbers",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           jsi::Array array = readArray(rt, name, argument(rt, name, args, count, 0));
           const size_t length = array.size(rt);
           for (size_t i = 0; i < length; ++i) {
             readNumber(rt, name, array.getValueAtIndex(rt, i));
           }
           return jsi::Value::undefined();
         });
  define(runtime,
         controls,
         "makeNumbers",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           const size_t length = readCount(rt, name, argument(rt, name, args, count, 0));
           jsi::Array array(rt, length);
           for (size_t i = 0; i < length; ++i) {
             array.setValueAtIndex(rt, i, static_cast<double>(i));
           }
           return jsi::Value(std::move(array));
         });

  define(runtime,
         controls,
         "takeStrings",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           jsi::Array array = readArray(rt, name, argument(rt, name, args, count, 0));
           const size_t length = array.size(rt);
           for (size_t i = 0; i < length; ++i) {
             readString(rt, name, array.getValueAtIndex(rt, i));
           }
           return jsi::Value::undefined();
         });
  define(runtime,
         controls,
         "makeStrings",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           const size_t length = readCount(rt, name, argument(rt, name, args, count, 0));
           jsi::Array array(rt, length);
           for (size_t i = 0; i < length; ++i) {
             array.setValueAtIndex(rt, i, jsi::String::createFromAscii(rt, indexed("s", i)));
           }
           return jsi::Value(std::move(array));
         });

  define(runtime,
         controls,
         "takeRecords4",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           jsi::Array array = readArray(rt, name, argument(rt, name, args, count, 0));
           const size_t length = array.size(rt);
           for (size_t i = 0; i < length; ++i) {
             readRecord4(rt, name, array.getValueAtIndex(rt, i));
           }
           return jsi::Value::undefined();
         });
  define(runtime,
         controls,
         "makeRecords4",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           const size_t length = readCount(rt, name, argument(rt, name, args, count, 0));
           jsi::Array array(rt, length);
           for (size_t i = 0; i < length; ++i) {
             array.setValueAtIndex(rt, i, makeRecord4(rt));
           }
           return jsi::Value(std::move(array));
         });

  define(runtime,
         controls,
         "takeMixed",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           jsi::Array array = readArray(rt, name, argument(rt, name, args, count, 0));
           const size_t length = array.size(rt);
           for (size_t i = 0; i < length; ++i) {
             jsi::Value element = array.getValueAtIndex(rt, i);
             if (element.isNumber()) {
               readNumber(rt, name, element);
             } else if (element.isString()) {
               readString(rt, name, element);
             } else {
               readBoolean(rt, name, element);
             }
           }
           return jsi::Value::undefined();
         });
  define(runtime,
         controls,
         "makeMixed",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           const size_t length = readCount(rt, name, argument(rt, name, args, count, 0));
           jsi::Array array(rt, length);
           for (size_t i = 0; i < length; ++i) {
             switch (i % 3) {
             case 0:
               array.setValueAtIndex(rt, i, static_cast<double>(i));
               break;
             case 1:
               array.setValueAtIndex(rt, i, jsi::String::createFromAscii(rt, indexed("s", i)));
               break;
             default:
               array.setValueAtIndex(rt, i, true);
               break;
             }
           }
           return jsi::Value(std::move(array));
         });

  define(runtime,
         controls,
         "takeArrayBuffer",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           jsi::Object object = readObject(rt, name, argument(rt, name, args, count, 0));
           if (!object.isArrayBuffer(rt)) {
             fail(rt, name, "an ArrayBuffer");
           }
           jsi::ArrayBuffer buffer = object.getArrayBuffer(rt);
           readBytes(rt, name, buffer, 0, buffer.size(rt));
           return jsi::Value::undefined();
         });
  define(runtime,
         controls,
         "makeArrayBuffer",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           const size_t length = readCount(rt, name, argument(rt, name, args, count, 0));
           return construct(rt, "ArrayBuffer", static_cast<double>(length));
         });

  define(runtime,
         controls,
         "takeUint8Array",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           jsi::Object view = readObject(rt, name, argument(rt, name, args, count, 0));
           jsi::Value bufferValue = view.getProperty(rt, "buffer");
           if (!bufferValue.isObject() || !bufferValue.getObject(rt).isArrayBuffer(rt)) {
             fail(rt, name, "a Uint8Array");
           }
           jsi::ArrayBuffer buffer = bufferValue.getObject(rt).getArrayBuffer(rt);
           const size_t offset = readCount(rt, name, view.getProperty(rt, "byteOffset"));
           const size_t length = readCount(rt, name, view.getProperty(rt, "byteLength"));
           readBytes(rt, name, buffer, offset, length);
           return jsi::Value::undefined();
         });
  define(runtime,
         controls,
         "makeUint8Array",
         1,
         [](jsi::Runtime &rt, const char *name, const jsi::Value *args, size_t count) {
           const size_t length = readCount(rt, name, argument(rt, name, args, count, 0));
           return construct(rt, "Uint8Array", static_cast<double>(length));
         });

  runtime.global().setProperty(runtime, "__expoBenchNative", controls);
}
