# `compendium-adapter-qdrant`

Qdrant adapter for the [Compendium](https://github.com/sassy-solutions/compendium) framework. Implements `IVectorStore` from `Compendium.Abstractions.VectorStore` over the [Qdrant](https://qdrant.tech) REST API via a hand-rolled `HttpClient` — no vendor SDK, works against both self-hosted and Qdrant Cloud.

Built from [`template-compendium-adapter-dotnet`](https://github.com/sassy-solutions/template-compendium-adapter-dotnet). Companion to [`compendium-adapter-pgvector`](https://github.com/sassy-solutions/compendium-adapter-pgvector) — same abstraction, same tenant isolation posture, same Result-pattern error handling.

## What's in this package

| Component | Implements | Purpose |
|---|---|---|
| `QdrantVectorStore` | `IVectorStore` | Embedding storage + ANN similarity search, JSON payload metadata, tenant isolation |
| `QdrantOptions` | — | Base URL / API key / index-tuning configuration |
| `TenantIdentifier` | — | Validates tenant ids against a strict alphanumeric+dash+underscore regex before any wire bind |
| `ServiceCollectionExtensions` | — | DI helpers (`AddCompendiumQdrant(...)`) |

## Install

```bash
dotnet add package Compendium.Adapters.Qdrant
```

## Quick start

```csharp
using Compendium.Abstractions.VectorStore;
using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Qdrant.DependencyInjection;

services.AddCompendiumQdrant(o =>
{
    o.BaseUrl = "http://localhost:6333";
    o.ApiKey  = builder.Configuration["Qdrant:ApiKey"];   // optional, required for cloud
});

// IVectorStore is now resolvable from DI.
var store = services.BuildServiceProvider().GetRequiredService<IVectorStore>();
await store.EnsureCollectionAsync("documents", dimension: 1536, DistanceMetric.Cosine);
await store.UpsertAsync("documents", new[]
{
    new VectorRecord("doc-1", embedding, metadata, tenantId: "tenant-1"),
});

var matches = await store.SearchAsync(
    "documents",
    queryEmbedding,
    topK: 5,
    VectorFilter.Eq("category", "support").ForTenant("tenant-1"));
```

A runnable example lives under [`samples/01-rag-roundtrip`](samples/01-rag-roundtrip/Program.cs).

## Configuration options

Bind to the `Compendium:Adapters:Qdrant` section, or pass an inline callback.

| Option | Default | Purpose |
|---|---|---|
| `BaseUrl` | `http://localhost:6333` | Qdrant base URL. For Qdrant Cloud, use the cluster URL. |
| `ApiKey` | _(null)_ | Optional API key sent in the `api-key` header. Required for Qdrant Cloud, optional for self-hosted dev. |
| `CollectionPrefix` | _(empty)_ | Prefix applied to every collection name on the wire (e.g. `dev_`, `staging_`). |
| `DefaultIndex` | `Hnsw` | ANN index built on collection creation (currently only HNSW). |
| `HnswM` | `16` | HNSW graph degree. |
| `HnswEfConstruct` | `128` | HNSW build-time candidate-list size. |
| `WaitForUpsert` | `true` | Whether writes should wait for Qdrant to confirm (`wait=true` query string). |
| `Timeout` | `30s` | Per-request HTTP timeout. |

## Tenancy

Qdrant supports payload-based multi-tenancy. Every record's optional `TenantId` is written as a reserved payload field (`tenant_id`), and every search filter we emit includes a `must` clause on that field.

- **Upsert**: the tenant id is stored alongside metadata; invalid ids (anything outside `[a-zA-Z0-9_-]{1,255}`) are rejected before serialisation.
- **Search**: when a `VectorFilter.ForTenant(...)` scope is supplied, results are restricted to that tenant. With no tenant supplied, the adapter emits a `must_not` clause that excludes any point carrying a tenant id (effectively restricting to "untagged" / shared records).
- **Delete**: id-list deletes with no tenant simply pass the ids; with a tenant, they emit a `must` filter requiring `tenant_id = X AND id ∈ ids`. There is no "delete across tenants" overload.

The `TenantIdentifier.IsValid` helper mirrors the same validator used by [`compendium-adapter-pgvector`](https://github.com/sassy-solutions/compendium-adapter-pgvector) and [`compendium-adapter-postgresql`](https://github.com/sassy-solutions/compendium-adapter-postgresql) — defence-in-depth against tenant-id-driven injection.

## Distance metrics

| `DistanceMetric` | Qdrant `Distance` label |
|---|---|
| `Cosine` | `Cosine` |
| `L2` | `Euclid` |
| `InnerProduct` | `Dot` |

The metric is fixed at `EnsureCollectionAsync` time. Trying to recreate an existing collection with a different size or distance returns a `VectorStore.DimensionMismatch` / `Qdrant.MetricMismatch` failure.

## Self-hosted vs Qdrant Cloud

| Aspect | Self-hosted | Qdrant Cloud |
|---|---|---|
| `BaseUrl` | `http://localhost:6333` (default) | `https://xxxxx.eu-west.aws.cloud.qdrant.io:6333` |
| `ApiKey` | optional (none in dev) | **required** |
| TLS | recommended once you leave loopback | enforced |
| Multi-region | manage manually | built into the cluster |

The same adapter binary serves both — only configuration changes.

## Production checklist

- **TLS / API-key rotation** — never check the api key into source. Rotate via your secret store (we read it via `IOptions<QdrantOptions>`, so any provider works).
- **HNSW tuning** — `HnswM=16` and `HnswEfConstruct=128` are sensible defaults for most RAG workloads. Larger `m` boosts recall at the cost of memory and build time; larger `ef_construct` boosts recall at the cost of build time only.
- **Dimensions per model** — pick the dimension once: changing it requires recreating the collection. Common values: 384 (e5-small, bge-small), 768 (e5-base, sentence-transformers), 1024 (Cohere embed v3), 1536 (OpenAI text-embedding-3-small), 3072 (OpenAI text-embedding-3-large).
- **Sharding** — Qdrant supports sharding inside a cluster; the adapter is transparent to it. Plan capacity around your largest collection's point count + vector dimension × 4 bytes.
- **Backups** — Qdrant ships snapshot APIs; use them on a schedule. The adapter does not expose snapshot helpers (yet).
- **Multi-tenancy** — prefer payload-based tenancy (default). For very large workloads with strict isolation requirements, deploy per-tenant collections and route via separate `QdrantOptions` instances.
- **Pooled `HttpClient`** — the DI extension registers `QdrantVectorStore` via `IHttpClientFactory`, so HTTP connections are pooled across requests by default.

## Versioning

This package is published as `Compendium.Adapters.Qdrant`. Versions are driven by git tags via [MinVer](https://github.com/adamralph/minver) — see [`docs/RELEASE.md`](docs/RELEASE.md). The release tag is set by the orchestrator after merge to `main`.

## Repository conventions

| Aspect | Choice |
|---|---|
| Target | .NET 9, C# 13 |
| HTTP | Hand-rolled `HttpClient` + `System.Text.Json` (snake_case naming policy) |
| Test framework | xUnit 2.9.3 + FluentAssertions 6.12.1 + NSubstitute 5.1.0 |
| HTTP mocking | [`RichardSzalay.MockHttp`](https://github.com/richardszalay/mockhttp) 7.0.0 |
| Integration tests | [Testcontainers](https://dotnet.testcontainers.org) 4.11.0 with `qdrant/qdrant:latest` |
| Coverage gate | ≥ 90 % line coverage on the unit-testable surface; integration suite covers wire-bound paths |
| Result pattern | `Result<T>` from `Compendium.Core` |

## Build & test locally

```bash
# Unit tests — no Docker required.
dotnet test --filter "FullyQualifiedName!~IntegrationTests"

# Integration tests — Docker must be running (Testcontainers pulls qdrant/qdrant:latest).
dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

The integration suite covers behaviour that can only be observed against a live Qdrant backend: collection creation, idempotent ensure, dimension-mismatch detection, upsert/search/delete round-trip, tenant isolation, and collection-not-found behaviour. Skips cleanly when Docker is unavailable via `[RequiresDockerFact]`.

## License

[MIT](LICENSE) — Copyright © 2026 Sassy Solutions.
