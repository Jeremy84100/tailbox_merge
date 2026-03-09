using System;
using System.Collections.Generic;
using System.Linq;

namespace Tailbox
{
    /// <summary>
    /// A high-performance, zero-allocation (mostly) C# port of tailwind-merge optimized for s&box.
    /// Uses Span<char> and HashCode logic to avoid GC pressure.
    /// </summary>
    public static class TailboxMerge
    {
        private const char ImportantModifier = '!';

        private static readonly string[] _prefixesSorted;

        static TailboxMerge()
        {
            // Pre-sort prefixes by length descending once to avoid LINQ at runtime
            _prefixesSorted = _prefixToGroupId.Keys.OrderByDescending( p => p.Length ).ToArray();
        }

        // Maps class prefixes to their Group ID.
        private static readonly Dictionary<string, string> _prefixToGroupId = new()
        {
            { "p-", "p" }, { "px-", "px" }, { "py-", "py" }, { "pt-", "pt" }, { "pr-", "pr" }, { "pb-", "pb" }, { "pl-", "pl" },
            { "m-", "m" }, { "mx-", "mx" }, { "my-", "my" }, { "mt-", "mt" }, { "mr-", "mr" }, { "mb-", "mb" }, { "ml-", "ml" },
            { "w-", "w" }, { "h-", "h" }, { "size-", "size" },
            { "bg-", "bg-color" }, { "bg-opacity-", "bg-opacity" },
            { "text-", "text-color" }, { "text-opacity-", "text-opacity" },
            { "border-", "border-color" }, { "border-t-", "border-t" }, { "border-r-", "border-r" }, { "border-b-", "border-b" }, { "border-l-", "border-l" }, { "border-x-", "border-x" }, { "border-y-", "border-y" },
            { "rounded-", "rounded" }, { "rounded-t-", "rounded-t" }, { "rounded-r-", "rounded-r" }, { "rounded-b-", "rounded-b" }, { "rounded-l-", "rounded-l" },
            { "flex-", "flex" }, { "grow-", "grow" }, { "shrink-", "shrink" }, { "basis-", "basis" },
            { "items-", "items" }, { "justify-", "justify" }, { "gap-", "gap" }, { "gap-x-", "gap-x" }, { "gap-y-", "gap-y" },
            { "font-", "font-weight" }, { "tracking-", "tracking" }, { "leading-", "leading" },
            { "opacity-", "opacity" }, { "shadow-", "shadow" }, { "outline-", "outline" }, { "ring-", "ring" },
            { "z-", "z" }, { "cursor-", "cursor" }, { "select-", "select" },
            { "inset-", "inset" }, { "top-", "top" }, { "bottom-", "bottom" }, { "left-", "left" }, { "right-", "right" },
            { "grid-cols-", "grid-cols" }, { "grid-rows-", "grid-rows" }, { "col-", "col-span" }, { "row-", "row-span" },
            { "overflow-", "overflow" }, { "object-", "object" }, { "whitespace-", "whitespace" },
            { "aspect-", "aspect" }, { "columns-", "columns" }, { "order-", "order" },
            { "stroke-", "stroke-color" }, { "fill-", "fill-color" },
            { "transition-", "transition" }, { "duration-", "duration" }, { "ease-", "ease" }, { "delay-", "delay" }, { "animate-", "animate" },
            { "scale-", "scale" }, { "rotate-", "rotate" }, { "translate-", "translate" }, { "skew-", "skew" }
        };

        // Classes that map to specific groups (no prefix).
        private static readonly Dictionary<string, string> _exactToGroupId = new()
        {
            { "text-xs", "font-size" }, { "text-sm", "font-size" }, { "text-base", "font-size" }, { "text-lg", "font-size" }, 
            { "text-xl", "font-size" }, { "text-2xl", "font-size" }, { "text-3xl", "font-size" }, { "text-4xl", "font-size" },
            { "flex", "display" }, { "inline-flex", "display" }, { "grid", "display" }, { "inline-grid", "display" }, 
            { "block", "display" }, { "inline-block", "display" }, { "hidden", "display" },
            { "flex-row", "flex-direction" }, { "flex-row-reverse", "flex-direction" }, { "flex-col", "flex-direction" }, { "flex-col-reverse", "flex-direction" },
            { "flex-wrap", "flex-wrap" }, { "flex-nowrap", "flex-wrap" },
            { "text-left", "text-align" }, { "text-center", "text-align" }, { "text-right", "text-align" }, { "text-justify", "text-align" },
            { "italic", "font-style" }, { "not-italic", "font-style" },
            { "underline", "text-decoration" }, { "line-through", "text-decoration" }, { "no-underline", "text-decoration" }
        };

        // Defines shorthand relationships: GroupID -> ConflictGroupIDs
        private static readonly Dictionary<string, string[]> _conflicts = new()
        {
            { "p", new[] { "px", "py", "pt", "pr", "pb", "pl" } },
            { "px", new[] { "pr", "pl" } },
            { "py", new[] { "pt", "pb" } },
            { "m", new[] { "mx", "my", "mt", "mr", "mb", "ml" } },
            { "mx", new[] { "mr", "ml" } },
            { "my", new[] { "mt", "mb" } },
            { "size", new[] { "w", "h" } },
            { "rounded", new[] { "rounded-t", "rounded-r", "rounded-b", "rounded-l" } },
            { "inset", new[] { "top", "bottom", "left", "right" } },
            { "border-width", new[] { "border-x-w", "border-y-w", "border-t-w", "border-r-w", "border-b-w", "border-l-w" } },
            { "border-color", new[] { "border-t", "border-r", "border-b", "border-l", "border-x", "border-y" } },
            { "border-x", new[] { "border-r", "border-l" } },
            { "border-y", new[] { "border-t", "border-b" } },
            { "gap", new[] { "gap-x", "gap-y" } },
            { "font-size", new[] { "leading" } }
        };

        public static string Merge( params string[] inputs )
        {
            if ( inputs == null || inputs.Length == 0 ) return string.Empty;

            var finalClasses = new List<string>();
            var conflictHashes = new HashSet<int>();

            // Process inputs backwards
            for ( int i = inputs.Length - 1; i >= 0; i-- )
            {
                var input = inputs[i];
                if ( string.IsNullOrWhiteSpace( input ) ) continue;

                // Manual split to avoid allocations
                int end = input.Length;
                while ( end > 0 )
                {
                    int lastSpace = input.LastIndexOf( ' ', end - 1 );
                    int start = lastSpace == -1 ? 0 : lastSpace + 1;
                    int length = end - start;

                    if ( length > 0 )
                    {
                        var className = input.Substring( start, length );
                        var parsed = ParseFast( className );

                        if ( parsed.GroupId == null )
                        {
                            if ( !finalClasses.Contains( className ) )
                                finalClasses.Add( className );
                        }
                        else
                        {
                            // Unique identifier for this class's conflict potential
                            int classId = HashCode.Combine( parsed.ModifiersHash, parsed.GroupId, parsed.IsImportant );

                            if ( !conflictHashes.Contains( classId ) )
                            {
                                finalClasses.Add( className );
                                conflictHashes.Add( classId );

                                // Handle shorthands/associated conflicts
                                if ( _conflicts.TryGetValue( parsed.GroupId, out var conflictingGroups ) )
                                {
                                    foreach ( var group in conflictingGroups )
                                    {
                                        conflictHashes.Add( HashCode.Combine( parsed.ModifiersHash, group, parsed.IsImportant ) );
                                    }
                                }
                            }
                        }
                    }

                    end = lastSpace == -1 ? 0 : lastSpace;
                }
            }

            finalClasses.Reverse();
            return string.Join( " ", finalClasses );
        }

        private struct ParsedResult
        {
            public string GroupId;
            public int ModifiersHash;
            public bool IsImportant;
        }

        private static ParsedResult ParseFast( string className )
        {
            ReadOnlySpan<char> span = className.AsSpan();
            
            bool isImportant = false;
            if ( span.Length > 0 && (span[0] == ImportantModifier || span[^1] == ImportantModifier) )
            {
                isImportant = true;
                span = span.Trim( ImportantModifier );
            }

            int lastColon = span.LastIndexOf( ':' );
            int modifiersHash = 0;
            ReadOnlySpan<char> baseName = span;

            if ( lastColon != -1 )
            {
                modifiersHash = string.GetHashCode( span.Slice( 0, lastColon + 1 ) );
                baseName = span.Slice( lastColon + 1 );
            }

            // 0. Handle arbitrary properties [prop:val]
            if ( baseName.Length > 2 && baseName[0] == '[' && baseName[^1] == ']' )
            {
                int colonIndex = baseName.IndexOf( ':' );
                if ( colonIndex != -1 )
                {
                    var propName = baseName.Slice( 1, colonIndex - 1 ).ToString();
                    return new ParsedResult { GroupId = "arbitrary-" + propName, ModifiersHash = modifiersHash, IsImportant = isImportant };
                }
            }

            string baseNameStr = baseName.ToString();

            // 1. Try exact match
            if ( _exactToGroupId.TryGetValue( baseNameStr, out var groupId ) )
            {
                return new ParsedResult { GroupId = groupId, ModifiersHash = modifiersHash, IsImportant = isImportant };
            }

            // 2. Try prefix match using the pre-sorted array
            foreach ( var prefix in _prefixesSorted )
            {
                if ( baseName.StartsWith( prefix.AsSpan(), StringComparison.Ordinal ) )
                {
                    var actualGroupId = _prefixToGroupId[prefix];

                    // SPECIAL LOGIC: Distinguish between color and width for border/text/etc.
                    // If it belongs to a 'color' group but looks like a value/length, switch group.
                    if ( actualGroupId == "border-color" || actualGroupId == "text-color" )
                    {
                        ReadOnlySpan<char> value = baseName.Slice( prefix.Length );
                        if ( IsValueNotColor( value ) )
                        {
                            actualGroupId = actualGroupId == "border-color" ? "border-width" : "font-size";
                        }
                    }

                    return new ParsedResult { GroupId = actualGroupId, ModifiersHash = modifiersHash, IsImportant = isImportant };
                }
            }

            return new ParsedResult { GroupId = null, ModifiersHash = modifiersHash, IsImportant = isImportant };
        }

        /// <summary>
        /// A very fast check to see if a tailwind value is likely a unit/length instead of a color.
        /// (e.g. '2', 'px', '[2px]' vs 'red-500', 'white', '[#ff0000]')
        /// </summary>
        private static bool IsValueNotColor( ReadOnlySpan<char> value )
        {
            if ( value.Length == 0 ) return false;
            if ( char.IsDigit( value[0] ) ) return true; // Starts with digit? 2, 4, 0.5... -> Width
            if ( value.Equals( "px".AsSpan(), StringComparison.OrdinalIgnoreCase ) ) return true; // 'px' -> Width
            if ( value[0] == '[' ) 
            {
                // Arbitrary value. Color usually has # or nuance digits. 
                // We simplify: if it contains 'px', 'rem', 'em', '%', it's a length.
                for( int i = 0; i < value.Length; i++ ) 
                {
                    if ( value[i] == 'p' || value[i] == 'r' || char.IsDigit(value[i]) ) return true; 
                }
            }
            return false;
        }
    }
}
