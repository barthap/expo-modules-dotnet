// Forced into every MSVC compile of the Hermes build by scripts/build-hermes-windows.ps1.
// Hermes V1 uses `__builtin_expect`, which MSVC does not have. The builtin only gives the
// compiler a branch hint, so the value of the expression is a correct replacement.
#if defined(_MSC_VER) && !defined(__clang__) && !defined(__builtin_expect)
#define __builtin_expect(expression, expected) (expression)
#endif
