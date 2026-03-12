using Sandbox;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Tailbox
{
    public enum ThemeType { Neutral, Light, Dark }
    public static class TailboxMerge
    {
        private const char ImportantModifier = '!';

        // Pre-computed hashes for O(1) property comparison
        private static readonly int _borderColorHash = GetStringHash( "border-color" );
        private static readonly int _textColorHash = GetStringHash( "text-color" );
        private static readonly int _outlineHash = GetStringHash( "outline" );
        private static readonly int _ringHash = GetStringHash( "ring" );
        private static readonly int _borderWidthHash = GetStringHash( "border-width" );
        private static readonly int _fontSizeHash = GetStringHash( "font-size" );
        private static readonly int _outlineWidthHash = GetStringHash( "outline-width" );
        private static readonly int _ringWidthHash = GetStringHash( "ring-width" );
        private static readonly int _outlineColorHash = GetStringHash( "outline-color" );
        private static readonly int _ringColorHash = GetStringHash( "ring-color" );
        private static readonly int _arbitraryBaseHash = GetStringHash( "arbitrary" );

        // O(1) Lookup Trie using integer hashes instead of strings
        private class TrieNode
        {
            public readonly TrieNode[] Children = new TrieNode[128];
            public int PrefixGroupIdHash = 0;
            public int ExactGroupIdHash = 0;
        }

        private static readonly TrieNode _trieRoot = new();

        // Internal conflict mappings using integer hashes
        private static readonly Dictionary<int, int[]> _conflictHashesMap = new();

        // Reusable thread-local buffers to eliminate allocations during merge operations
        [ThreadStatic] private static List<ClassSpan> _tsFinalClasses;
        [ThreadStatic] private static HashSet<long> _tsConflictHashes;
        [ThreadStatic] private static HashSet<int> _tsExactSeen;
        [ThreadStatic] private static StringBuilder _tsBuilder;
        [ThreadStatic] private static string[] _tsInputBuffer;

        static TailboxMerge()
        {
            var prefixToGroupId = new Dictionary<string, string>()
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

            var exactToGroupId = new Dictionary<string, string>()
            {
                { "text-xs", "font-size" }, { "text-sm", "font-size" }, { "text-base", "font-size" }, { "text-lg", "font-size" }, 
                { "text-xl", "font-size" }, { "text-2xl", "font-size" }, { "text-3xl", "font-size" }, { "text-4xl", "font-size" },
                { "flex", "display" }, { "inline-flex", "display" }, { "grid", "display" }, { "inline-grid", "display" }, 
                { "block", "display" }, { "inline-block", "display" }, { "hidden", "display" },
                { "flex-row", "flex-direction" }, { "flex-row-reverse", "flex-direction" }, { "flex-col", "flex-direction" }, { "flex-col-reverse", "flex-direction" },
                { "flex-wrap", "flex-wrap" }, { "flex-nowrap", "flex-nowrap" },
                { "text-left", "text-align" }, { "text-center", "text-align" }, { "text-right", "text-align" }, { "text-justify", "text-align" },
                { "italic", "font-style" }, { "not-italic", "font-style" },
                { "underline", "text-decoration" }, { "line-through", "text-decoration" }, { "no-underline", "text-decoration" }
            };

            var stringConflicts = new Dictionary<string, string[]>()
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

            // Initialize Trie with property groupings
            foreach ( var kvp in prefixToGroupId )
            {
                var node = GetOrCreateNode( kvp.Key );
                node.PrefixGroupIdHash = GetStringHash( kvp.Value );
            }
            foreach ( var kvp in exactToGroupId )
            {
                var node = GetOrCreateNode( kvp.Key );
                node.ExactGroupIdHash = GetStringHash( kvp.Value );
            }

            // Map string-based conflicts to integer hashes for faster lookup
            foreach ( var kvp in stringConflicts )
            {
                int groupHash = GetStringHash( kvp.Key );
                int[] conflictHashes = new int[kvp.Value.Length];
                for ( int i = 0; i < kvp.Value.Length; i++ )
                {
                    conflictHashes[i] = GetStringHash( kvp.Value[i] );
                }
                _conflictHashesMap[groupHash] = conflictHashes;
            }
        }

        private static TrieNode GetOrCreateNode( string key )
        {
            var current = _trieRoot;
            foreach ( char c in key )
            {
                if ( c >= 128 ) continue; // Safe bound for ASCII
                if ( current.Children[c] == null )
                {
                    current.Children[c] = new TrieNode();
                }
                current = current.Children[c];
            }
            return current;
        }

        private static void EnsureBuffers()
        {
            if ( _tsFinalClasses == null )
            {
                _tsFinalClasses = new List<ClassSpan>( 64 );
                _tsConflictHashes = new HashSet<long>( 64 );
                _tsExactSeen = new HashSet<int>( 64 );
                _tsBuilder = new StringBuilder( 256 );
                _tsInputBuffer = new string[16]; // Input buffer for params optimization
            }
            else
            {
                _tsFinalClasses.Clear();
                _tsConflictHashes.Clear();
                _tsExactSeen.Clear();
                _tsBuilder.Clear();
                Array.Clear( _tsInputBuffer, 0, _tsInputBuffer.Length );
            }
        }

        // Standard overloads to minimize params[] allocations
        public static string Merge( string a ) 
        {
            EnsureBuffers();
            _tsInputBuffer[0] = a;
            return MergeCore( _tsInputBuffer, 1 );
        }

        public static string Merge( string a, string b ) 
        {
            EnsureBuffers();
            _tsInputBuffer[0] = a;
            _tsInputBuffer[1] = b;
            return MergeCore( _tsInputBuffer, 2 );
        }

        public static string Merge( string a, string b, string c ) 
        {
            EnsureBuffers();
            _tsInputBuffer[0] = a;
            _tsInputBuffer[1] = b;
            _tsInputBuffer[2] = c;
            return MergeCore( _tsInputBuffer, 3 );
        }

        public static string Merge( params string[] inputs ) 
        {
            EnsureBuffers();
            return MergeCore( inputs, inputs.Length );
        }

        private static string MergeCore( string[] inputs, int count )
        {
            if ( count == 0 ) return string.Empty;

            for ( int i = count - 1; i >= 0; i-- )
            {
                var input = inputs[i];
                if ( string.IsNullOrWhiteSpace( input ) ) continue;

                int end = input.Length;
                while ( end > 0 )
                {
                    while ( end > 0 && char.IsWhiteSpace( input[end - 1] ) ) end--;
                    if ( end == 0 ) break;

                    int start = end - 1;
                    while ( start > 0 && !char.IsWhiteSpace( input[start - 1] ) ) start--;

                    int length = end - start;
                    ReadOnlySpan<char> classSpan = input.AsSpan( start, length );
                    
                    var parsed = ParseFast( classSpan );
                    bool shouldKeep = false;

                    if ( parsed.GroupIdHash == 0 )
                    {
                        int spanHash = GetStringHash( classSpan );
                        if ( _tsExactSeen.Add( spanHash ) )
                        {
                            shouldKeep = true;
                        }
                    }
                    else
                    {
                        // Resolve conflicts using integer-based hash comparisons
                        long classId = GenerateHash( parsed.Theme, parsed.ModifiersHash, parsed.GroupIdHash, parsed.IsImportant );

                        if ( !_tsConflictHashes.Contains( classId ) )
                        {
                            shouldKeep = true;
                            
                            if ( parsed.Theme == ThemeType.Neutral )
                            {
                                _tsConflictHashes.Add( GenerateHash( ThemeType.Neutral, parsed.ModifiersHash, parsed.GroupIdHash, parsed.IsImportant ) );
                                
                                if ( _conflictHashesMap.TryGetValue( parsed.GroupIdHash, out var conflictingGroups ) )
                                {
                                    foreach ( var groupHash in conflictingGroups )
                                    {
                                        _tsConflictHashes.Add( GenerateHash( ThemeType.Neutral, parsed.ModifiersHash, groupHash, parsed.IsImportant ) );
                                    }
                                }
                            }
                            else
                            {
                                _tsConflictHashes.Add( classId );
                                
                                if ( _conflictHashesMap.TryGetValue( parsed.GroupIdHash, out var conflictingGroups ) )
                                {
                                    foreach ( var groupHash in conflictingGroups )
                                    {
                                        _tsConflictHashes.Add( GenerateHash( parsed.Theme, parsed.ModifiersHash, groupHash, parsed.IsImportant ) );
                                    }
                                }
                            }
                        }
                    }

                    if ( shouldKeep )
                    {
                        // Store reference to class position without allocating substrings
                        _tsFinalClasses.Add( new ClassSpan( i, start, length ) );
                    }

                    end = start;
                }
            }

            if ( _tsFinalClasses.Count == 0 ) return string.Empty;

            // Loop backwards because we stored them backwards
            for ( int j = _tsFinalClasses.Count - 1; j >= 0; j-- )
            {
                var cs = _tsFinalClasses[j];
                _tsBuilder.Append( inputs[cs.InputIndex].AsSpan( cs.Start, cs.Length ) );
                
                if ( j > 0 )
                {
                    _tsBuilder.Append( ' ' );
                }
            }

            return _tsBuilder.ToString();
        }

        private static long GenerateHash( ThemeType theme, int modifiersHash, int groupIdHash, bool isImportant )
        {
            unchecked
            {
                long hash = 17;
                hash = hash * 31 + groupIdHash;
                hash = hash * 31 + modifiersHash;
                hash = hash * 31 + (int)theme;
                hash = hash * 31 + (isImportant ? 1 : 0);
                return hash;
            }
        }

        [StructLayout( LayoutKind.Sequential )]
        private readonly struct ClassSpan
        {
            public readonly int InputIndex;
            public readonly int Start;
            public readonly int Length;

            public ClassSpan( int inputIndex, int start, int length )
            {
                InputIndex = inputIndex;
                Start = start;
                Length = length;
            }
        }

        [StructLayout( LayoutKind.Sequential )]
        private readonly struct ParsedResult
        {
            public readonly int GroupIdHash; // <--- The holy grail: Stringless mapping!
            public readonly int ModifiersHash;
            public readonly ThemeType Theme;
            public readonly bool IsImportant;

            public ParsedResult( int groupIdHash, int modifiersHash, ThemeType theme, bool isImportant )
            {
                GroupIdHash = groupIdHash;
                ModifiersHash = modifiersHash;
                Theme = theme;
                IsImportant = isImportant;
            }
        }

        private static ParsedResult ParseFast( ReadOnlySpan<char> span )
        {
            // Extract modifiers FIRST, before checking !important
            int lastColon = FindLastColonSkipBrackets( span );

            ThemeType theme = ThemeType.Neutral;
            ReadOnlySpan<char> baseClass = span;
            int modifiersHash = 0;

            if ( lastColon != -1 )
            {
                var modifiersSpan = span.Slice( 0, lastColon + 1 );
                baseClass = span.Slice( lastColon + 1 );
                modifiersHash = GetModifiersHash( modifiersSpan, ref theme );
            }

            // NOW we check for !important on the baseClass
            bool isImportant = false;
            if ( baseClass.Length > 0 && baseClass[0] == ImportantModifier )
            {
                isImportant = true;
                baseClass = baseClass.Slice( 1 );
            }
            else if ( baseClass.Length > 0 && baseClass[^1] == ImportantModifier )
            {
                isImportant = true;
                baseClass = baseClass.Slice( 0, baseClass.Length - 1 );
            }

            if ( baseClass.Length > 2 && baseClass[0] == '[' && baseClass[^1] == ']' )
            {
                int colonIndex = baseClass.IndexOf( ':' );
                if ( colonIndex != -1 )
                {
                    int propHash = GetStringHash( baseClass.Slice( 1, colonIndex - 1 ) );
                    // XOR logic combines integers smoothly without boxing
                    int arbitraryGroupIdHash = _arbitraryBaseHash ^ propHash;
                    return new ParsedResult( arbitraryGroupIdHash, modifiersHash, theme, isImportant );
                }
            }

            // O(1) prefix and exact match search using Trie navigation
            var currentNode = _trieRoot;
            int bestPrefixGroupIdHash = 0;
            int bestPrefixLength = 0;
            int exactGroupIdHash = 0;

            for ( int i = 0; i < baseClass.Length; i++ )
            {
                char c = baseClass[i];
                if ( c >= 128 || currentNode.Children[c] == null )
                {
                    exactGroupIdHash = 0;
                    break;
                }
                
                currentNode = currentNode.Children[c];
                
                if ( currentNode.PrefixGroupIdHash != 0 )
                {
                    bestPrefixGroupIdHash = currentNode.PrefixGroupIdHash;
                    bestPrefixLength = i + 1;
                }
                
                if ( i == baseClass.Length - 1 && currentNode.ExactGroupIdHash != 0 )
                {
                    exactGroupIdHash = currentNode.ExactGroupIdHash;
                }
            }

            if ( exactGroupIdHash != 0 )
            {
                return new ParsedResult( exactGroupIdHash, modifiersHash, theme, isImportant );
            }

            if ( bestPrefixGroupIdHash != 0 )
            {
                // SPECIAL LOGIC: Check numerical hashes instead of strings!
                if ( bestPrefixGroupIdHash == _borderColorHash || bestPrefixGroupIdHash == _textColorHash || 
                     bestPrefixGroupIdHash == _outlineHash || bestPrefixGroupIdHash == _ringHash )
                {
                    ReadOnlySpan<char> value = baseClass.Slice( bestPrefixLength );
                    if ( IsValueNotColor( value ) )
                    {
                        if ( bestPrefixGroupIdHash == _borderColorHash ) bestPrefixGroupIdHash = _borderWidthHash;
                        else if ( bestPrefixGroupIdHash == _textColorHash ) bestPrefixGroupIdHash = _fontSizeHash;
                        else if ( bestPrefixGroupIdHash == _outlineHash ) bestPrefixGroupIdHash = _outlineWidthHash;
                        else if ( bestPrefixGroupIdHash == _ringHash ) bestPrefixGroupIdHash = _ringWidthHash;
                    }
                    else if ( bestPrefixGroupIdHash == _outlineHash ) bestPrefixGroupIdHash = _outlineColorHash;
                    else if ( bestPrefixGroupIdHash == _ringHash ) bestPrefixGroupIdHash = _ringColorHash;
                }
                return new ParsedResult( bestPrefixGroupIdHash, modifiersHash, theme, isImportant );
            }

            return new ParsedResult( 0, modifiersHash, theme, isImportant );
        }

        private static int GetModifiersHash( ReadOnlySpan<char> span, ref ThemeType theme )
        {
            unchecked
            {
                int hash = 0;
                int start = 0;

                while ( start < span.Length )
                {
                    int nextColon = FindNextColonSkipBrackets( span.Slice( start ) );
                    if ( nextColon == -1 ) break;

                    var modifier = span.Slice( start, nextColon );
                    
                    if ( modifier.SequenceEqual( "light".AsSpan() ) ) theme = ThemeType.Light;
                    else if ( modifier.SequenceEqual( "dark".AsSpan() ) ) theme = ThemeType.Dark;
                    else
                    {
                        int modHash = GetStringHash( modifier );
                        
                        // Apply mixing to minimize collisions for nested modifiers
                        modHash ^= modHash >> 16;
                        modHash *= -2048144789;
                        modHash ^= modHash >> 13;
                        modHash *= -1028477387;
                        modHash ^= modHash >> 16;
                        
                        hash ^= modHash;
                    }

                    start += nextColon + 1;
                }

                return hash;
            }
        }

        private static int FindLastColonSkipBrackets( ReadOnlySpan<char> span )
        {
            int bracketLevel = 0;
            for ( int i = span.Length - 1; i >= 0; i-- )
            {
                char c = span[i];
                if ( c == ']' ) bracketLevel++;
                else if ( c == '[' ) bracketLevel--;
                else if ( c == ':' && bracketLevel == 0 ) return i;
            }
            return -1;
        }

        private static int FindNextColonSkipBrackets( ReadOnlySpan<char> span )
        {
            int bracketLevel = 0;
            for ( int i = 0; i < span.Length; i++ )
            {
                char c = span[i];
                if ( c == '[' ) bracketLevel++;
                else if ( c == ']' ) bracketLevel--;
                else if ( c == ':' && bracketLevel == 0 ) return i;
            }
            return -1;
        }

        private static int GetStringHash( string str )
        {
            unchecked
            {
                int hash = 17;
                for ( int i = 0; i < str.Length; i++ )
                {
                    hash = hash * 31 + str[i];
                }
                return hash;
            }
        }

        private static int GetStringHash( ReadOnlySpan<char> span )
        {
            unchecked
            {
                int hash = 17;
                for ( int i = 0; i < span.Length; i++ )
                {
                    hash = hash * 31 + span[i];
                }
                return hash;
            }
        }

        private static bool IsValueNotColor( ReadOnlySpan<char> value )
        {
            if ( value.Length == 0 ) return false;
            if ( char.IsDigit( value[0] ) ) return true;
            if ( value.StartsWith( "px".AsSpan(), StringComparison.OrdinalIgnoreCase ) ) return true;
            if ( value[0] == '[' ) 
            {
                if ( value.EndsWith( "%]".AsSpan() ) || value.EndsWith( "px]".AsSpan() ) || value.EndsWith( "rem]".AsSpan() ) || 
                     value.EndsWith( "em]".AsSpan() ) || value.EndsWith( "vw]".AsSpan() ) || value.EndsWith( "vh]".AsSpan() ) )
                {
                    return true;
                }
                
                if ( value.IndexOf( "var(".AsSpan() ) != -1 || value.IndexOf( "--".AsSpan() ) != -1 )
                {
                    return true;
                }
            }
            return false;
        }
    }
}