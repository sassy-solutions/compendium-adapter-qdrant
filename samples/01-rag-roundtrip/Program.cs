// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------
//
// Sample 01 — RAG round-trip
// =========================
// Demonstrates the minimal happy path of Compendium.Adapters.Qdrant:
//   1. ensure a 3-dimensional collection (cosine distance, HNSW index),
//   2. upsert five hand-crafted vectors,
//   3. search for the three nearest neighbours to a query vector,
//   4. print the matches.
//
// Connection convention:
//   export QDRANT_URL="http://localhost:6333"
//   export QDRANT_API_KEY="..."   # optional — required for Qdrant Cloud
//
// Run a local Qdrant:
//   docker run --rm -p 6333:6333 qdrant/qdrant:latest
// then:
//   QDRANT_URL=http://localhost:6333 dotnet run --project samples/01-rag-roundtrip

using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Qdrant;
using Compendium.Adapters.Qdrant.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var baseUrl = Environment.GetEnvironmentVariable("QDRANT_URL") ?? "http://localhost:6333";
var apiKey = Environment.GetEnvironmentVariable("QDRANT_API_KEY");

var options = Options.Create(new QdrantOptions
{
    BaseUrl = baseUrl,
    ApiKey = apiKey,
    CollectionPrefix = "sample_",
});

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
var logger = loggerFactory.CreateLogger<QdrantVectorStore>();

using var http = new HttpClient();
var store = new QdrantVectorStore(http, options, logger);

const string collection = "rag_roundtrip";

// 1. Ensure the collection exists.
var ensure = await store.EnsureCollectionAsync(collection, dimension: 3, DistanceMetric.Cosine);
if (ensure.IsFailure)
{
    Console.Error.WriteLine($"EnsureCollection failed: {ensure.Error.Code} — {ensure.Error.Message}");
    return 1;
}

// 2. Upsert five vectors. Qdrant requires point ids to be either a UUID or
//    an unsigned integer; UUIDs are the safest choice for sample code.
var records = new List<VectorRecord>
{
    new(Guid.NewGuid().ToString(), new float[] { 1f, 0f, 0f },     new Dictionary<string, object> { ["title"] = "alpha" }),
    new(Guid.NewGuid().ToString(), new float[] { 0f, 1f, 0f },     new Dictionary<string, object> { ["title"] = "beta" }),
    new(Guid.NewGuid().ToString(), new float[] { 0f, 0f, 1f },     new Dictionary<string, object> { ["title"] = "gamma" }),
    new(Guid.NewGuid().ToString(), new float[] { 1f, 1f, 0f },     new Dictionary<string, object> { ["title"] = "ne-x" }),
    new(Guid.NewGuid().ToString(), new float[] { 0.5f, 0.5f, 0.5f }, new Dictionary<string, object> { ["title"] = "origin" }),
};

var upsert = await store.UpsertAsync(collection, records);
if (upsert.IsFailure)
{
    Console.Error.WriteLine($"Upsert failed: {upsert.Error.Code} — {upsert.Error.Message}");
    return 1;
}

// 3. Search for the three closest to a query that leans toward alpha.
var query = new float[] { 0.9f, 0.1f, 0f };
var search = await store.SearchAsync(collection, query, topK: 3);
if (search.IsFailure)
{
    Console.Error.WriteLine($"Search failed: {search.Error.Code} — {search.Error.Message}");
    return 1;
}

// 4. Print results.
Console.WriteLine("Top 3 nearest neighbours:");
foreach (var match in search.Value!)
{
    var title = match.Metadata.TryGetValue("title", out var t) ? t : "(no title)";
    Console.WriteLine($"  id={match.Id,-40} score={match.Score,8:F4}  title={title}");
}

return 0;
