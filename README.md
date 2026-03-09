# Tailbox Merge

High-performance Tailwind CSS class merging for s&box, inspired by tailwind-merge.

[![s&box Package](https://img.shields.io/badge/s&box-Package-blue.svg)](https://sbox.game/agelaste/tailbox_merge)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## About

Tailbox Merge is a professional-grade C# utility designed specifically for the s&box environment. It intelligently merges Tailwind CSS classes by resolving styling conflicts, ensuring that the "last one wins" while understanding the semantic relationships between different utility classes.

Unlike simple string concatenation, Tailbox Merge understands Tailwind's internal logic, allowing for robust component styling and predictable overrides without manual class management.

## Features

- **Intelligent Conflict Resolution**: Correctly handles shorthand vs. longhand conflicts (e.g., `p-4` vs `pt-2`).
- **Semantic Awareness**: Distinguishes between similar prefixes with different meanings (e.g., `text-red-500` for color vs `text-lg` for font size).
- **Variant Support**: Handles Tailwind modifiers (`hover:`, `dark:`, `focus:`, etc.) and the `!` (important) marker with full isolation.
- **Ultra-Performance**: Optimized for s&box with a "Zero-Allocation" approach using `ReadOnlySpan<char>` and pre-sorted prefix trees.
- **Small Footprint**: A standalone library with zero external dependencies.

## Usage

Simply call `TailboxMerge.Merge` with your list of classes:

```csharp
using Tailbox;

// Basic merging
string classes = TailboxMerge.Merge("p-4 px-2", "p-8"); 
// Output: "p-8"

// Variant handling
string variantClasses = TailboxMerge.Merge("bg-red-500 hover:bg-red-600", "bg-blue-500");
// Output: "bg-blue-500 hover:bg-red-600"

// Complex shorthand overrides
string complex = TailboxMerge.Merge("border-2 border-red-500", "border-t-4 border-blue-500");
// Output: "border-2 border-red-500 border-t-4 border-blue-500" 
// (border-t-4 only overrides the top width, maintaining global border-2 and color)
```

## Requirements

- **s&box** environment.
- **Tailbox Compiler**: While standalone, it is designed to work seamlessly with the Tailbox ecosystem.

## Performance Note

Tailbox Merge is built specifically for game development performance:
- No LINQ in the hot path.
- Minimal GC pressure (no `Split` or `Concat` during parsing).
- Thread-safe and high-concurrency ready.

## License

Tailbox Merge is licensed under the [MIT](LICENSE) license. 
Feel free to [contribute on GitHub](https://github.com/Jeremy84100/tailbox_merge) to help improve this library!
