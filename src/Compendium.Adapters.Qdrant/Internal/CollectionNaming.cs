// -----------------------------------------------------------------------
// <copyright file="CollectionNaming.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.RegularExpressions;
using Compendium.Adapters.Qdrant.Options;

namespace Compendium.Adapters.Qdrant.Internal;

/// <summary>
/// Helpers for deriving and validating Qdrant collection names from the
/// logical names used by callers.
/// </summary>
internal static partial class CollectionNaming
{
    // Qdrant accepts a wide range of collection names but we keep our own posture
    // tight: alphanumeric + dash + underscore. This matches the pgvector adapter
    // and is the safest superset for path-segment usage.
    [GeneratedRegex(@"^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CollectionRegex();

    /// <summary>
    /// Maximum allowed length of a (prefixed) collection name on the wire.
    /// </summary>
    public const int MaxLength = 255;

    /// <summary>
    /// Returns whether the supplied collection name passes the safe-character regex
    /// and length cap.
    /// </summary>
    public static bool IsValid(string? collection)
    {
        if (string.IsNullOrEmpty(collection))
        {
            return false;
        }

        if (collection.Length > MaxLength)
        {
            return false;
        }

        return CollectionRegex().IsMatch(collection);
    }

    /// <summary>
    /// Returns the resolved collection name (configured prefix + caller-supplied collection).
    /// </summary>
    public static string Resolve(QdrantOptions options, string collection)
    {
        ArgumentNullException.ThrowIfNull(options);
        return (options.CollectionPrefix ?? string.Empty) + collection;
    }
}
