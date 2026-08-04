# Universe Roadmap Status

This is the current source-verified roadmap status for the `3.0.0` through `3.7.0` feature targets.

## Partially Complete

### 1.1 Parameterized Stored Procedures Support

Status: partially complete.

Implemented:

- Stored procedure execution accepts `params object[]` inputs.
- Parameters are checked for JSON serializability before execution.
- Stored procedure lifecycle operations exist for create, read, replace, delete, and list.

Remaining gap:

- Dedicated deployment/versioning utilities are not implemented. Stored procedure bodies are still raw trusted administrative JavaScript strings.

## Verified Complete

### 1.2 Aggregation Pipeline Support

Status: complete.

Implemented:

- `COUNT`, `SUM`, `AVG`, `MIN`, and `MAX` are available through `Q.Aggregate`.
- Aggregates are supported through declarative `ColumnOptions` and fluent `Orbit.Aggregate(...)`.
- Aggregate identifiers are validated and bracket-formatted before SQL generation.

### 1.3 Basic Transaction Batch Support

Status: complete.

Implemented:

- Bulk create and bulk modify group documents by partition key.
- Each partition-key group executes through Cosmos `TransactionalBatch`.
- Batch size and payload size limits are enforced before execution.

### 1.4 Multi-Level Partitioning

Status: complete.

Implemented:

- `[PartitionKey]` supports one to three partition-key levels.
- Partition-key path generation and runtime value generation are sequence ordered.
- Duplicate sequence values and more than three levels are rejected.
- Explicit partition-key path names are honored through `PartitionKeyAttribute(int sequence, string keyName = null)`.

### 1.5 Vector Search Capabilities

Status: complete.

Implemented:

- `Q.Operator.VectorDistance` generates Cosmos `VectorDistance(...)` SQL.
- Vector ranking requires `TOP` and enforces vector-size and finite-number validation.
- Vector queries use the vector execution strategy.

### 1.6 Full Text Search Capabilities

Status: complete.

Implemented:

- `FullTextContains`, `FullTextContainsAll`, `FullTextContainsAny`, and negated variants are supported.
- `FullTextScore` supports relevance ranking.
- Hybrid vector/full-text ranking uses RRF when multiple rank catalysts are present.

### 2.1 Query Execution Strategy

Status: complete.

Implemented:

- Direct, gateway, and vector strategies are available.
- Query hints can force supported strategies and tune request options.
- Query execution statistics track RU, duration, result count, success, strategy, and hints.
- Query tuning recommendations can be rule-based or data-driven from historical samples.
- File and SQLite statistics persistence are available through `UniverseOptions`.

### 2.2 Optional Document Cache And Invalidation Layer

Status: complete.

Implemented:

- Document caching is disabled by default and enabled explicitly through `UniverseOptions.WithDocumentCache(...)`.
- Direct id/partition-key reads and single-document query reads can be cached.
- Cache keys are hashed and scoped by database, container, source type, result type, and operation kind.
- Cached values are cloned by default to prevent caller mutation from corrupting cache state.
- Successful create, modify, remove, bulk create, and bulk modify operations invalidate same-repository single-document query cache entries.
- Point-read cache entries are updated or removed for local single-document writes where the affected id and partition key are known.
- Tests use fake Cosmos SDK abstractions and do not require a live Cosmos DB account.

Caveat:

- The cache is process-local. External writers are handled by the configured time-to-live, not distributed invalidation.

## Not Complete

### 2.3 RU Budget Management

Status: not implemented.

Remaining scope:

- RU quota management.
- Rate limiting and automatic throttling for bulk operations.
- RU analytics and recommendations beyond the current per-operation `Gravity.RU` and query statistics.

Preferred future direction:

- Design this as a global coordinator rather than a per-repository quick patch.

### 3.1 Enhanced Bulk Operation Support

Status: complete in `3.5.0-preview.2`.

Implemented:

- `Bulk()` groups operations by logical partition, automatically chunks them to Cosmos transactional-batch limits, and runs up to four partition pipelines concurrently by default.
- Per-partition chunks preserve operation order and use `TransactionalBatch`; cross-partition work deliberately reports structured partial success instead of compensating writes.
- Create, replace, patch, and delete operations return ordered per-operation status, ETags, and request-charge allocation.
- Replace, patch, and delete support optimistic concurrency through ETags.

### 3.2 Atomic Operations

Status: complete in `3.5.0-preview.2`.

Implemented:

- `Atomic(partitionKeys)` executes mixed create, replace, patch, and delete operations as one same-partition ACID transaction.
- Typed patch builders support set, add, replace, remove, and increment operations with Cosmos's per-item patch limit enforced before execution.
- Typed conditions support scalar comparisons with explicit `And()` / `Or()` chaining, serializer-aware property names, and no raw predicate-string API.

Deferred until Cosmos distributed transactions reach general availability:

- Cross-partition distributed transactions and any speculative abstraction over the preview-only Cosmos feature. Cross-partition `Bulk()` intentionally remains non-atomic and performs no automatic compensation.

### 4.1 Change Feed Processing

Status: not implemented.

Remaining scope:

- Change feed processing abstraction.
- Resume token or checkpoint management.
- Event-driven handlers for document changes.
- Long-running processor support.

### 4.2 Enhanced Debugging

Status: not implemented.

Remaining scope:

- Query plan visualization.
- RU cost estimation before execution.
- Expanded performance analytics and optimization suggestions.

## Verification

Transactional behavior is verified with a deterministic in-memory Cosmos harness. It covers atomic commit and rollback, ETag failures, chunking, bounded concurrency, partial success, cancellation, request options, and cache invalidation without creating or connecting to Cosmos DB resources.

Current local verification:

```bash
cd code
dotnet test -c Release --no-restore --filter "Category!=Performance"
dotnet pack Universe/UniverseQuery.csproj -c Release --no-restore
```

Release packing validates the public package API against `UniverseQuery` `3.4.1`.
