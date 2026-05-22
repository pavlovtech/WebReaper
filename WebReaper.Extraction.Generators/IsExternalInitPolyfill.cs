// netstandard2.0 doesn't define System.Runtime.CompilerServices.IsExternalInit,
// which C# 9+ records (and init-only setters) require. Polyfill it.
// Standard idiom across the .NET source-generator ecosystem.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
