# Changelog

All notable changes to `Compendium.Adapters.Qdrant` are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `QdrantVectorStore` implementing `Compendium.Abstractions.VectorStore.IVectorStore` against the Qdrant REST API via a hand-rolled `HttpClient` (no vendor SDK). Works against both self-hosted Qdrant and Qdrant Cloud.
  - `EnsureCollectionAsync` — idempotent `PUT /collections/{name}`. Probes existence via `GET /collections/{name}` first; rejects dimension or distance-metric drift with structured errors (`VectorStore.DimensionMismatch` / `Qdrant.MetricMismatch`). HNSW index configured with caller-controllable `m` / `ef_construct` (defaults 16 / 128). Treats concurrent-create 409s as success.
  - `UpsertAsync` — `PUT /collections/{name}/points?wait=...` with Qdrant's batch upsert shape. Embeds the validated tenant id as a reserved `tenant_id` payload field. Empty input list is a no-op (no HTTP call).
  - `DeleteAsync` — `POST /collections/{name}/points/delete`. Untenanted deletes use the id-list selector; tenanted deletes emit a `must` filter requiring `tenant_id = X AND id ∈ ids`. Cross-tenant deletion is impossible by construction.
  - `SearchAsync` — `POST /collections/{name}/points/search` with `with_payload=true`. Maps `VectorFilter` (Eq/Ne/In/Range/And/Or) into Qdrant's filter wire shape. Without an explicit tenant, queries restrict to records carrying no `tenant_id`.
- `QdrantOptions` — base URL (default `http://localhost:6333`), optional API key (sent as `api-key` header), collection prefix, HNSW tuning (`HnswM` / `HnswEfConstruct`), `WaitForUpsert` toggle, request timeout. Data-annotation-validated, `ValidateOnStart`.
- `ServiceCollectionExtensions.AddCompendiumQdrant(...)` — DI registration. Two overloads: `IConfiguration` binding to `Compendium:Adapters:Qdrant` section, or an inline `Action<QdrantOptions>` callback. Uses `IHttpClientFactory` under the hood so HTTP connections are pooled.
- `TenantIdentifier` — security-hardened tenant id validator (alphanumeric + dash + underscore, ≤ 255 chars). Mirrors `compendium-adapter-pgvector/TenantIdentifier`.
- `DistanceMetricMap` — `DistanceMetric` ↔ Qdrant `Distance` label (`Cosine` / `Euclid` / `Dot`).
- `CollectionNaming` — prefix resolution + path-segment-safe collection-name validator.
- `MetadataSerializer` — round-trips `IReadOnlyDictionary<string, object>` through Qdrant's JSON `payload` field. Strips the reserved `tenant_id` key on read so callers never see the adapter's bookkeeping.
- `VectorFilterTranslator` — translates `VectorFilter` trees into Qdrant's filter wire shape (`must`/`must_not`/`should` with `match`/`range`/nested-filter conditions).
- `QdrantIdConverter` — tolerant id deserializer: Qdrant emits ids as either strings (UUID) or unsigned ints; the converter returns a single `string`.
- `QdrantHttpClient` — typed HTTP client. Injects the `api-key` header, snake_case serialisation, maps non-success codes to structured `Result.Failure` (401/403 → `Qdrant.Unauthorized`, 404 → `Qdrant.NotFound`, 408 → `Qdrant.Timeout`, 409 → `Qdrant.Conflict`, 429 → `Qdrant.Throttled`, 5xx → `Qdrant.ServerError`, `HttpRequestException` → `Qdrant.Network`).
- `samples/01-rag-roundtrip` — minimal runnable program that ensures a collection, upserts five vectors, searches the top three.
- `tests/Unit/Compendium.Adapters.Qdrant.Tests` — 171 unit tests covering options validation, tenant id validator (with the same SQL-injection corpus as `compendium-adapter-pgvector`), distance-metric mapping, collection-name validation, vector-filter translation (incl. type-coercion + composite filters + propagation of child failures), metadata round-trip, id-converter (string + numeric + null token branches), DI registration, and `QdrantVectorStore`'s validation / HTTP / error-mapping surface via `RichardSzalay.MockHttp` + a custom fault-injecting `HttpMessageHandler`.
- `tests/Integration/Compendium.Adapters.Qdrant.IntegrationTests` — Testcontainers-based suite (`qdrant/qdrant:latest`) covering collection creation, idempotent ensure, dimension-mismatch detection, upsert/search/delete round-trip, tenant isolation, and collection-not-found behaviour. Skips cleanly when Docker is unavailable via `[RequiresDockerFact]`.

### Dependencies

- `Compendium.Abstractions.VectorStore` 1.0.1
- `Compendium.Abstractions` 1.0.1
- `Compendium.Core` 1.0.1
