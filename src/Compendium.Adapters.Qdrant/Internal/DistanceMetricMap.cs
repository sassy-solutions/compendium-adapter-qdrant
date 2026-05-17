// -----------------------------------------------------------------------
// <copyright file="DistanceMetricMap.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore.Models;

namespace Compendium.Adapters.Qdrant.Internal;

/// <summary>
/// Translates <see cref="DistanceMetric"/> values into the Qdrant <c>Distance</c> enum used
/// in collection-creation payloads, and back.
/// </summary>
internal static class DistanceMetricMap
{
    /// <summary>
    /// Returns the Qdrant <c>Distance</c> label for the given <paramref name="metric"/>.
    /// </summary>
    /// <remarks>
    /// Qdrant supports four labels in <c>POST /collections/{name}</c>:
    /// <list type="bullet">
    ///   <item><c>Cosine</c> — cosine similarity (higher is closer).</item>
    ///   <item><c>Euclid</c> — L2 / Euclidean distance (lower is closer).</item>
    ///   <item><c>Dot</c> — inner product (higher is closer).</item>
    ///   <item><c>Manhattan</c> — L1 distance (not exposed by Compendium).</item>
    /// </list>
    /// </remarks>
    public static string Label(DistanceMetric metric) => metric switch
    {
        DistanceMetric.Cosine => "Cosine",
        DistanceMetric.L2 => "Euclid",
        DistanceMetric.InnerProduct => "Dot",
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unsupported distance metric."),
    };

    /// <summary>
    /// Parses the label produced by <see cref="Label"/> back into a <see cref="DistanceMetric"/>.
    /// Returns <c>false</c> for unknown / unsupported labels.
    /// </summary>
    public static bool TryParseLabel(string? label, out DistanceMetric metric)
    {
        switch (label)
        {
            case "Cosine":
                metric = DistanceMetric.Cosine;
                return true;
            case "Euclid":
                metric = DistanceMetric.L2;
                return true;
            case "Dot":
                metric = DistanceMetric.InnerProduct;
                return true;
            default:
                metric = default;
                return false;
        }
    }
}
