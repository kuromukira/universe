# CODEBASE.md

Read this file first before doing anything in this codebase. It is the working map for the repository, its architecture, and the implementation nuances that are easy to miss from file names alone.

## Project Identity

Universe, published as `UniverseQuery`, is a C#/.NET library for querying Azure Cosmos DB through repository classes and a fluent, type-oriented query builder.

The library is centered on these concepts:

- `Galaxy`: repository abstraction for one Cosmos DB container.
- `GalaxyBasic`: basic create, read, update, delete, and bulk operations.
- `GalaxyProcedure`: stored procedure lifecycle and execution operations.
- `CosmicEntity` / `ICosmicEntity`: base document contract.
- `Orbit<T>`: fluent query builder exposed through `galaxy.Query()`.
- `Cluster` and `Catalyst`: declarative query-condition data structures.
- `Gravity`: operation metadata returned from every public operation, including request units, continuation token, and optional query details.
- `UniverseBuilder`: internal SQL builder and query execution dispatcher.
- `QueryTuner`: adaptive query-statistics collector and recommendation source.

## Repository Map

```text
.
|-- CLAUDE.md
|-- AGENTS.md
|-- CODEBASE.md
|-- README.md
|-- VERSION
|-- docs/
|   |-- ADAPTIVE_QUERY_OPTIMIZATION_DESIGN.md
|   |-- DECLARATIVE_QUERY_SYNTAX.md
|   |-- FLUENT_QUERY_BUILDER.md
|   |-- FULLTEXT_USAGE.md
|   |-- QUERY_EXECUTION_STRATEGIES.md
|   |-- ROADMAP_STATUS.md
|   |-- SQLITE_STATISTICS_STORAGE.md
|   `-- VECTORDISTANCE_USAGE.md
`-- code/
    |-- UniverseQuery.slnx
    |-- Universe/
    |-- Universe.Tests/
    `-- DarkMatter/
```

## Solution Map

```text
code/
|-- UniverseQuery.slnx
|-- Universe/
|   |-- UniverseQuery.csproj
|   |-- GalaxyCore.cs
|   |-- GalaxyBasic.cs
|   |-- Galaxy.cs
|   |-- GalaxyProcedures.cs
|   |-- DocumentCacheOptions.cs
|   |-- UniverseOptions.cs
|   |-- Builder/
|   |   |-- QueryBuilder.cs
|   |   |-- Orbit.cs
|   |   |-- ClusterBuilder.cs
|   |   |-- Caching/
|   |   |-- Options/
|   |   `-- Strategies/
|   |-- Interfaces/
|   |-- Attributes/
|   |-- Extensions/
|   |-- Response/
|   `-- Exception/
|-- Universe.Tests/
|   |-- Builder/
|   |-- Storage/
|   `-- Tuner/
`-- DarkMatter/
    |-- Program.cs
    |-- Examples/
    |-- Models/
    |-- Repository/
    `-- Helpers/
```

## Complete Source Inventory

### `code/Universe`

The `Universe` project is the library package. Anything in this project can become part of the NuGet package surface directly or indirectly, so preserve binary/API compatibility unless the change is intentionally breaking.

```text
code/Universe/
|-- Attributes/
|   `-- PartitionKeyAttribute.cs
|-- Builder/
|   |-- ClusterBuilder.cs
|   |-- Orbit.cs
|   |-- QueryBuilder.cs
|   |-- Options/
|   |   |-- AggregationOption.cs
|   |   |-- ColumnOptions.cs
|   |   |-- JoinOptions.cs
|   |   |-- Parameters.cs
|   |   |-- QueryHints.cs
|   |   |-- QueryOptions.cs
|   |   |-- SortingOptions.cs
|   |   |-- UniverseSerializerOptions.cs
|   |   `-- WhereClause.cs
|   `-- Strategies/
|       |-- DirectQueryStrategy.cs
|       |-- EnhancedContextStrategy.cs
|       |-- GatewayQueryStrategy.cs
|       |-- IQueryExecutionStrategy.cs
|       |-- QueryContext.cs
|       |-- QueryExecutionStatistics.cs
|       |-- QueryStrategySelector.cs
|       |-- QueryTuner.cs
|       |-- QueryTypeDetector.cs
|       |-- VectorSearchStrategy.cs
|       `-- Storage/
|           |-- FileStatisticsStorage.cs
|           |-- InMemoryStatisticsStorage.cs
|           |-- IQueryStatisticsStorage.cs
|           |-- PlatformDetection.cs
|           `-- SqliteStatisticsStorage.cs
|-- Exception/
|   `-- UniverseException.cs
|-- Extensions/
|   |-- HintValueExtensions.cs
|   |-- OrbitExtensions.cs
|   |-- PartitionKeyExtension.cs
|   |-- ProjectionColumnExtractor.cs
|   `-- StrExtensions.cs
|-- Interfaces/
|   |-- ICosmicEntity.cs
|   |-- IGalaxy.cs
|   |-- IGalaxyBasic.cs
|   `-- IGalaxyProcedure.cs
|-- Response/
|   `-- Response.cs
|-- Galaxy.cs
|-- GalaxyBasic.cs
|-- GalaxyCore.cs
|-- GalaxyProcedures.cs
|-- DocumentCacheOptions.cs
|-- UniverseOptions.cs
|-- UniverseQuery.csproj
|-- UniverseQuery.xml
|-- LICENSE
`-- global-usings.cs
```

File responsibilities:

- `Attributes/PartitionKeyAttribute.cs`: attribute for marking partition-key properties. It supports default sequence `1`, validates sequence `1` through `3`, and optionally accepts an explicit Cosmos key name.
- `Builder/Caching/DocumentCache.cs`: optional process-local document cache used only when `UniverseOptions.WithDocumentCache(...)` is configured.
- `Builder/ClusterBuilder.cs`: fluent builder for one `Cluster`. It stores catalysts in order and tracks the next intra-cluster `AND` / `OR` connector.
- `Builder/Orbit.cs`: public fluent query builder. It accumulates clusters, selected columns, sort options, aggregates, group-by columns, pagination, joins, hints, and execution mode.
- `Builder/QueryBuilder.cs`: internal SQL-generation and query-dispatch core. Treat as security-sensitive.
- `Builder/Options/AggregationOption.cs`: record struct pairing a column with a `Q.Aggregate` enum.
- `Builder/Options/ColumnOptions.cs`: record struct for selected columns, `DISTINCT`, `TOP`, aggregates, and joins.
- `Builder/Options/JoinOptions.cs`: record for Cosmos array `JOIN` configuration.
- `Builder/Options/Parameters.cs`: `Catalyst` definition and rule validation; also generates parameter names.
- `Builder/Options/QueryHints.cs`: user-facing query hint record and execution strategy enum.
- `Builder/Options/QueryOptions.cs`: `Q` operator, aggregate, page, and where-option definitions plus SQL keyword conversion helpers.
- `Builder/Options/SortingOptions.cs`: sorting option and sort-direction keyword conversion.
- `Builder/Options/UniverseSerializerOptions.cs`: Cosmos serializer implementation backed by `System.Text.Json`.
- `Builder/Options/WhereClause.cs`: `Cluster` record struct.
- `Builder/Strategies/DirectQueryStrategy.cs`: simple-query executor.
- `Builder/Strategies/GatewayQueryStrategy.cs`: conservative fallback executor.
- `Builder/Strategies/VectorSearchStrategy.cs`: vector, full-text score, and hybrid-query executor.
- `Builder/Strategies/EnhancedContextStrategy.cs`: wrapper that supplies merged recommendation/user hints to the selected strategy.
- `Builder/Strategies/IQueryExecutionStrategy.cs`: internal strategy contract.
- `Builder/Strategies/QueryContext.cs`: query type and recommendation DTOs.
- `Builder/Strategies/QueryExecutionStatistics.cs`: recorded performance/statistics model.
- `Builder/Strategies/QueryStrategySelector.cs`: strategy selection and hint merging.
- `Builder/Strategies/QueryTuner.cs`: in-memory statistics window, persistence coordination, and hint recommendation logic.
- `Builder/Strategies/QueryTypeDetector.cs`: shared generated-SQL classifier used by strategy selection and hint-aware list execution.
- `Builder/Strategies/Storage/IQueryStatisticsStorage.cs`: storage abstraction.
- `Builder/Strategies/Storage/InMemoryStatisticsStorage.cs`: volatile statistics storage.
- `Builder/Strategies/Storage/FileStatisticsStorage.cs`: JSON statistics persistence.
- `Builder/Strategies/Storage/SqliteStatisticsStorage.cs`: SQLite statistics persistence with WAL and batched writes.
- `Builder/Strategies/Storage/PlatformDetection.cs`: Azure/local path selection, path validation, and Unix permission tightening.
- `Exception/UniverseException.cs`: library exception.
- `Extensions/HintValueExtensions.cs`: safe conversion of hint values to int/bool.
- `Extensions/OrbitExtensions.cs`: exposes `.Query()` on `IGalaxy<T>`.
- `Extensions/PartitionKeyExtension.cs`: builds Cosmos partition-key paths and runtime values from attributes.
- `Extensions/ProjectionColumnExtractor.cs`: extracts projection property names and honors `[JsonIgnore]`.
- `Extensions/StrExtensions.cs`: string naming helpers, especially lower-camel conversion.
- `GalaxyCore.cs`: Cosmos database/container initialization and shared flags.
- `GalaxyBasic.cs`: basic document operations and bulk transaction batch operations.
- `Galaxy.cs`: advanced query operations and generated-query inspection.
- `GalaxyProcedures.cs`: stored procedure operations.
- `DocumentCacheOptions.cs`: opt-in document-cache configuration and cache ownership.
- `Interfaces/*.cs`: public contracts.
- `Response/Response.cs`: `Gravity` response record.
- `UniverseOptions.cs`: user-facing query-statistics persistence, document-cache, and repository provisioning configuration.
- `global-usings.cs`: shared namespace imports.

### `code/Universe.Tests`

The test project is the fastest feedback loop for library behavior that does not need a live Cosmos account.

```text
code/Universe.Tests/
|-- Batch/
|   |-- BatchBuilderTests.cs
|   |-- BatchExecutionTests.cs
|   `-- LegacyInterfaceCompatibilityTests.cs
|-- Builder/
|   |-- NamingPolicyQueryTests.cs
|   |-- OrbitQueryTests.cs
|   |-- QueryTypeDetectorTests.cs
|   `-- ProjectionSelectTests.cs
|-- Caching/
|   |-- DocumentCacheRepositoryTests.cs
|   `-- DocumentCacheTests.cs
|-- Extensions/
|   `-- PartitionKeyExtensionTests.cs
|-- Helpers/
|   |-- InMemoryCosmosContainer.cs
|   `-- TestStatisticsFactory.cs
|-- Storage/
|   |-- FileStatisticsStorageTests.cs
|   |-- InMemoryStatisticsStorageTests.cs
|   |-- PlatformDetectionTests.cs
|   `-- SqliteStatisticsStorageTests.cs
|-- Tuner/
|   |-- QueryTunerPerformanceTests.cs
|   `-- QueryTunerTests.cs
`-- Universe.Tests.csproj
```

Test intent:

- Query text generation should be stable after normalizing generated parameter names.
- Fluent `Orbit<T>` should mirror declarative `Cluster` / `Catalyst` output.
- Naming policy changes must be reflected in generated SQL.
- Projection extraction should include expected public properties and exclude ignored/computed fields.
- Storage backends should persist, load, clean up, and tolerate corrupt/tampered storage where applicable.
- Tuner tests verify rule-based fallbacks, data-driven recommendations, persistence loading, and query-hash normalization.
- Performance tests are excluded in CI with `--filter "Category!=Performance"`.

### `code/DarkMatter`

`DarkMatter` is the example harness. It is not the package, but it is a useful compatibility and demonstration surface.

```text
code/DarkMatter/
|-- DarkMatter.csproj
|-- Program.cs
|-- Examples/
|   |-- ExampleBase.cs
|   |-- Example1_BasicPagedQuery.cs
|   |-- Example2_AggregatesWithGroupBy.cs
|   |-- Example3_TopAndDistinct.cs
|   |-- Example4_ComplexFiltering.cs
|   |-- Example5_SingleItemOperations.cs
|   |-- Example6_BulkOperations.cs
|   |-- Example7_AdvancedAggregation.cs
|   |-- Example8_SalesAnalysis.cs
|   |-- Example9_VectorSearch.cs
|   |-- Example10_FullTextSearch.cs
|   |-- Example11_HybridVectorFullText.cs
|   |-- Example12_QueryGeneration.cs
|   |-- Example13_QueryOptimization.cs
|   |-- Example14_SQLInjectionProtection.cs
|   |-- Example15_ContainsOperator.cs
|   `-- Example16_ProjectionSelect.cs
|-- Helpers/
|   |-- TestDataGenerator.cs
|   `-- VectorDataGenerator.cs
|-- Models/
|   |-- MyObject.cs
|   |-- MyObjectAggregation.cs
|   `-- MyObjectVector.cs
`-- Repository/
    |-- MyRepo.cs
    `-- MyRepoVector.cs
```

Nuances:

- `Program.cs` contains placeholder Cosmos URI/key values. Do not commit real secrets there.
- `MyRepo` uses `UniverseOptions.WithFilePersistence()` and `recordQueries: true`, intentionally surfacing queries for examples.
- Example output prints query text and parameters. This is useful for docs and demos, but production guidance should keep `recordQueries` off.
- Vector/full-text examples depend on Cosmos DB account features and proper indexing/container setup.
- `Example14_SQLInjectionProtection.cs` is the living demonstration of the expected injection defenses.

### Root And Documentation Files

- `CODEBASE.md`: this first-read map.
- `AGENTS.md`: agent instructions for Codex and other coding agents.
- `CLAUDE.md`: Claude-specific instructions; should stay aligned with `AGENTS.md`.
- `README.md`: package-facing quickstart and API overview.
- `VERSION`: repository/package version marker.
- `LICENSE`: root license.
- `docs/*.md`: feature-specific guides. Keep docs updated when public API or behavior changes.

### `.github`

```text
.github/
|-- CODEOWNERS
|-- dependabot.yml
|-- trafico.yml
`-- workflows/
    |-- dotnet-ci.yml
    |-- nuget-publish.yml
    |-- semgrep--pr-ci.yml
    `-- semgrep-ci.yml
```

Nuances:

- `dotnet-ci.yml` runs on every push and has restore, build, and test jobs.
- CI uses `.NET 10.x` preview via `actions/setup-dotnet`.
- CI test command excludes tests tagged `Category=Performance`.
- `nuget-publish.yml` runs on pushes to `prod` and `beta`, builds, packs `code/Universe/UniverseQuery.csproj`, and pushes to NuGet with `NUGET_KEY`.
- Semgrep runs on PRs and on pushes to `dev` and `prod`.
- `dependabot.yml` currently points NuGet updates at `./functions/`, which does not match this repo's active `code/Universe` and `code/Universe.Tests` project layout.
- `CODEOWNERS` includes several paths from a different layout (`/functions/`, `/app_portal/`, `/app_store/`, `/web_admin/`); review before relying on it for ownership enforcement.

## Build And Test

Primary commands:

```bash
cd code
dotnet restore
dotnet build
dotnet test
```

Release/package commands:

```bash
cd code
dotnet build -c Release
cd Universe
dotnet pack -c Release
```

Example harness:

```bash
cd code/DarkMatter
dotnet run
```

`DarkMatter` requires real Cosmos DB values in `Program.cs` before it can run against a live account.

## Target Runtime And Packages

The main library currently targets:

- .NET `net10.0`
- C# `14.0`
- Nullable disabled
- `Microsoft.Azure.Cosmos` `3.61.0`
- `Microsoft.Data.Sqlite` `10.0.9`
- `Newtonsoft.Json` `13.0.4`
- `SQLitePCLRaw.bundle_e_sqlite3` `3.0.3`
- `SQLitePCLRaw.core` `3.0.3`

`Universe.Tests` is an xUnit test project targeting `net10.0`. The tests are local unit tests for query generation, storage, and tuning behavior; they do not require Cosmos DB unless a new test explicitly adds that dependency.

## Public API Map

Primary consumer entry points:

- Derive from `Galaxy<T>` for full query support.
- Derive from `GalaxyBasic<T>` for basic CRUD only.
- Derive from `GalaxyProcedure` for stored procedure support.
- Inject/use `IGalaxy<T>` where advanced queries are needed.
- Inject/use `IGalaxyBasic<T>` where only basic CRUD is needed.
- Call `galaxy.Query()` to use the fluent `Orbit<T>` API.
- Use `Cluster` / `Catalyst` directly for declarative query construction.
- Configure serializer behavior with `new UniverseSerializer(...)` on `CosmosClientOptions.Serializer`.
- Configure tuning persistence with `UniverseOptions.WithFilePersistence(...)` or `UniverseOptions.WithSqlitePersistence(...)`.
- Enable optional process-local document caching with `UniverseOptions.WithDocumentCache(...)`.

Important public contracts:

- `IGalaxyBasic<T>.Create(T)` returns `(Gravity g, string t)`, where `t` is the id.
- `IGalaxyBasic<T>.Create(IReadOnlyList<T>)` returns aggregate `Gravity` for the bulk operation.
- `IGalaxyBasic<T>.Modify(T)` uses `ReplaceItemAsync`, not patch semantics.
- `IGalaxyBasic<T>.Modify(IReadOnlyList<T>)` uses transactional batches grouped by partition key.
- `IGalaxyBasic<T>.Remove(...)` deletes by id and partition key.
- `IGalaxyBasic<T>.Get(...)` reads by id and partition key.
- `IGalaxy<T>.Get(...)` runs a query and returns the first result or default.
- `IGalaxy<T>.List(...)` executes full query shape and can use hints.
- `IGalaxy<T>.Paged(...)` uses continuation tokens.
- `IGalaxy<T>.GenerateQuery(...)` returns query text and parameters without execution.
- `IGalaxy<T>.GetQueryRecommendations(...)` currently ignores the `queryPattern` argument and uses `queryType`.
- `IGalaxyProcedure` methods map directly to Cosmos stored procedure script APIs.

## Runtime Flow

1. Consumer code creates a repository by deriving from `Galaxy<T>` or `GalaxyBasic<T>`.
2. `GalaxyCore` validates database and container names, creates the database/container if missing, captures serializer naming policy, and stores the Cosmos `Container`.
3. Basic CRUD calls go through `GalaxyBasic<T>`.
4. Advanced list/get/paged calls go through `Galaxy<T>`.
5. Query inputs are converted into a `QueryDefinition` by `UniverseBuilder.CreateQuery`.
6. Query execution uses a strategy selected by `QueryStrategySelector`.
7. Execution returns `Gravity` plus the requested model/list/projection result.
8. Query execution statistics are recorded by `QueryTuner` and optionally persisted through `UniverseOptions`.
9. If document caching is explicitly enabled, point reads and single-document query reads check the process-local cache before executing Cosmos requests.

## Detailed Operation Flows

### Repository Construction

`GalaxyCore` constructor behavior:

1. Rejects empty container name.
2. Rejects empty database name.
3. Calls `CreateDatabaseIfNotExistsAsync(database).GetAwaiter().GetResult()`.
4. Creates `ContainerProperties` from the container name and partition-key paths.
5. Stores `_recordQuery`.
6. Reads `AllowBulkExecution` from `CosmosClientOptions`.
7. If the serializer is `UniverseSerializer`, captures its `NamingPolicy`.
8. Calls `CreateContainerIfNotExistsAsync(containerProps).GetAwaiter().GetResult()`.

Nuances:

- Construction performs synchronous blocking waits on async Cosmos calls.
- Repository creation has side effects: database/container creation can happen during DI construction.
- Container partition-key paths come from the repository constructor argument, usually `typeof(T).BuildPartitionKey()`.
- Serializer naming policy is captured once during repository construction.

### Create

`GalaxyBasic<T>.Create(T)` behavior:

1. Assigns a version 7 GUID if `model.id` is blank.
2. Sets `AddedOn = DateTime.UtcNow`.
3. Calls `CreateItemAsync` with `model.BuildPartitionKey()`.
4. Sets `EnableContentResponseOnWrite = false`.
5. Returns request charge and id.
6. Converts conflict into `{T} already exists.`
7. Wraps non-conflict Cosmos errors as `UniverseException` with status code.

### Bulk Create

`GalaxyBasic<T>.Create(IReadOnlyList<T>)` behavior:

1. Requires Cosmos client `AllowBulkExecution`.
2. Serializes the whole model list and rejects payloads over 2 MB.
3. Rejects more than 100 items.
4. Groups models by runtime partition key.
5. Creates one transactional batch per partition key.
6. Assigns ids and `AddedOn`.
7. Uses `EnableContentResponseOnWrite = false`.
8. Executes all batches and sums request charge.

Nuances:

- Despite the `AllowBulkExecution` guard, implementation uses transactional batches grouped by partition key.
- Batch failures are surfaced as `UniverseException`.
- The 2 MB check is on the serialized full list, not per partition group.

### Modify

`GalaxyBasic<T>.Modify(T)` behavior:

1. Sets `ModifiedOn = DateTime.UtcNow`.
2. Calls `ReplaceItemAsync(model, model.id, model.BuildPartitionKey())`.
3. Returns request charge and returned resource.
4. Converts not found into `{T} does not exist.`

Bulk modify mirrors bulk create with `ReplaceItem` operations and the same `AllowBulkExecution`, 2 MB, and 100-item limits.

### Remove

`Remove(id, string partitionKey)` uses a single string `PartitionKey`.

`Remove(id, params string[] partitionKey)` uses `PartitionKeyBuilder` and rejects null/empty partition key arrays and null/blank values.

Both set `EnableContentResponseOnWrite = false` and convert not found into `{T} does not exist.`

### Direct Id Get

`Get(id, string partitionKey)` and `Get(id, params string[] partitionKey)` call `ReadItemAsync` and return request charge plus resource. Not found is converted into `{T} does not exist.`

When `UniverseOptions.WithDocumentCache(...)` is configured, direct id reads are cached by hashed database/container/type/id/partition-key scope. Cache hits return `Gravity.RU = 0`. Without `DocumentCache`, the code path only performs a null check and behaves as before.

### Query Get/List/Paged

`Galaxy<T>.Get(...)`:

- Builds a query with optional selected columns.
- Executes through `QBuilder.GetOneFromQuery`.
- Internally forces `MaxItemCount = 1` in the `QueryContext`.
- Returns first result or `default`.
- When document caching is enabled, generated SQL and ordered parameter values are normalized into a hashed cache key before execution. Random catalyst ids in parameter names are normalized so equivalent query shapes can hit the same cache entry.

`Galaxy<T>.List(...)`:

- Builds full query shape.
- Uses `QBuilder.GetListFromQuery`.
- With hints, constructs `QueryContext` using inferred query type and `hints.ToContextHints()`.

`Galaxy<T>.Paged(...)`:

- Builds query.
- Calls `GetItemQueryIterator` directly instead of going through strategy selector.
- Supplies `MaxItemCount = page.Size`.
- Supplies the continuation token if non-blank.
- Reads pages until it gets a non-empty page, then returns that continuation token and breaks, except for `GROUP BY` queries.
- Returns generated query details only when `_recordQuery` is true.

Nuances:

- Paged queries do not currently record `QueryTuner` statistics.
- Paged queries do not apply `QueryHints`.
- `GROUP BY` has special continuation behavior because the loop does not break when query text contains `GROUP BY`.

### Generate Query

`GenerateQuery` always returns `Gravity` containing:

- `RU = 0`
- Empty continuation token
- Query text
- Query parameters

This bypasses the `recordQueries` flag by design, because query inspection is the method's purpose.

## Core Library Files

- `GalaxyCore.cs`: shared Cosmos container initialization, `recordQueries` flag handling, bulk option detection, serializer naming policy capture.
- `GalaxyBasic.cs`: create, bulk create, modify, bulk modify, remove, and direct id/partition-key reads.
- `Galaxy.cs`: advanced query execution for get/list/paged, generated query inspection, and query recommendations.
- `GalaxyProcedures.cs`: Cosmos stored procedure execute/create/read/replace/delete/list support.
- `UniverseOptions.cs`: optional persistent query-statistics storage configuration.
- `Response/Response.cs`: `Gravity` response record.
- `Exception/UniverseException.cs`: library-specific exception type.
- `global-usings.cs`: shared imports for the library.

## Builder Internals

### `UniverseBuilder.CreateQuery`

High-level phases:

1. `SanitizeInputs(...)`
2. Build selected columns.
3. Add aggregates and group columns when needed.
4. Add vector-distance score projections.
5. Build base `SELECT ... FROM c`.
6. Apply join shape when `ColumnOptions.Join` is present.
7. Validate clusters and catalyst rules.
8. Build grouped `WHERE` clauses.
9. Build scalar `ORDER BY` or rank ordering.
10. Build `GROUP BY`.
11. Create `QueryDefinition`.
12. Bind parameters.

Projection behavior:

- Default selection is `*`.
- `ColumnOptions.Names` replaces `*` with formatted properties.
- `Top > 0` prepends `TOP {Top}`.
- `IsDistinct` with selected columns prepends `DISTINCT`.
- Aggregates require `ColumnOptions.Names`.
- Aggregate aliases are generated from aggregate column names.
- Vector-distance catalysts append score expressions such as `VectorDistance(... ) AS {Column}Score`.

Join behavior:

- Base query becomes `SELECT ... FROM c JOIN {alias} IN c["arrayPath"]`.
- Join columns are formatted against the join alias.
- Join aggregate columns are formatted against the join alias.
- Join columns used with aggregates are added to group columns.

Where behavior:

- Each non-empty cluster becomes a parenthesized group.
- The cluster's `Where` value connects clusters.
- Inside a cluster, the first catalyst's `Where` value is ignored; subsequent catalyst `Where` values connect conditions.
- Rank-only clusters (`VectorDistance` / `FTScore`) do not add a `WHERE` clause.
- `Defined` / `NotDefined` operators do not bind a value.

Ranking behavior:

- A single `VectorDistance` catalyst produces `ORDER BY VectorDistance(...)`.
- A single `FTScore` catalyst produces `ORDER BY RANK FullTextScore(...)`.
- Multiple rank catalysts produce `ORDER BY RANK RRF(...)`.
- Weighted RRF accepts one `Sorting.Direction.WEIGHTED` option containing numeric weights.
- Scalar sorting is rejected when rank catalysts are present.

Parameter binding:

- Normal catalysts bind `@{catalyst.ParameterName()}`.
- `FTScore` binds each term as `@{ParameterName}_ft{i}`.
- `Catalyst.ParameterName()` removes non-word/digit characters from the column and appends a GUID-derived catalyst id.

### Identifier Formatting And Validation

Identifier handling has two pieces:

- `ValidateIdentifier(...)` validates the raw identifier before query text is built.
- `FormatProperty(alias, column)` splits the column on dots and renders each non-empty segment as `["segment"]`.

Validation currently rejects:

- Null/blank identifiers.
- Identifiers consisting only of dots.
- Semicolons.
- `--`
- `/*`
- `*/`
- Double quotes.
- Closing brackets.
- Length greater than 255.
- Control characters.

Nuance:

- Opening brackets are not independently rejected because a closing bracket is required to break out of bracket notation. The protection relies on rejecting closing brackets and double quotes.
- Dotted paths support nested JSON access; empty path segments are skipped by `FormatProperty`.
- Naming policy conversion is applied per path segment.

## Query Builder Map

`Builder/QueryBuilder.cs` is the most security-sensitive file in the repository.

It is responsible for:

- Validating all query identifiers before SQL text is built.
- Building `SELECT`, `JOIN`, `WHERE`, `ORDER BY`, `ORDER BY RANK`, and `GROUP BY`.
- Formatting property paths as Cosmos bracket paths, such as `c["metadata"]["sku"]`.
- Applying the configured JSON naming policy to query field names.
- Building parameterized `QueryDefinition` instances.
- Expanding `FTScore` string arrays into separate parameters.
- Appending vector-distance score aliases into the selected columns.
- Dispatching queries to the selected execution strategy.

Important constraints:

- Values must stay parameterized with `QueryDefinition.WithParameter`.
- Identifiers are not parameters in Cosmos SQL, so they must be validated and bracket-formatted.
- `ValidateIdentifier` rejects empty identifiers, only-dot identifiers, SQL comment/statement separators, double quotes, closing brackets, excessive length, and control characters.
- Weighted RRF sort values are validated as numeric comma-separated content before being inserted into SQL.
- `GenerateQuery` intentionally returns query text and parameters without executing the query.

## Query APIs

There are two equivalent query styles.

Declarative API:

- `Cluster`: a group of `Catalyst` filters joined by `AND` or `OR`.
- `Catalyst`: one filter condition containing column, value, where connector, operator, and alias.
- `ColumnOptions`: selected columns, distinct flag, top value, aggregates, and optional join.
- `Sorting.Option`: sorting or RRF weight options.
- `QueryHints`: strategy and request-option hints.

Fluent API:

- `Orbit<T>` is created with `galaxy.Query()`.
- `ClusterBuilder` exposes the operator helpers used inside `.Cluster(c => ...)`.
- `Select<TProjection>()` extracts public projection properties, excluding `[JsonIgnore]`.
- `GetAsync()` applies only filters and selected columns. It intentionally ignores `Top`, `Distinct`, `Aggregate`, `GroupBy`, sorting, joins, paging, and hints.
- `ToListAsync()` applies the full list query shape.
- `Paged()` and `WithHints()` are mutually exclusive in the fluent API.

Fluent API method behavior:

- `Cluster(Action<ClusterBuilder>)`: adds one cluster and consumes the pending cluster connector.
- `Or()` / `And()`: set the connector for the next cluster.
- `Select(params string[])`: adds explicit selected columns.
- `Select<TProjection>()`: adds public projection properties.
- `Distinct()`: sets distinct flag.
- `Top(int)`: rejects negative values.
- `OrderBy(...)`: adds sort option. The `alias` argument is stored on `Sorting.Option`, but current query building formats sort columns with alias `c`.
- `OrderByDescending(...)`: adds descending sort option.
- `WithWeights(...)`: adds weighted RRF option.
- `Aggregate(...)`: adds aggregate expression.
- `GroupBy(...)`: adds group columns.
- `Paged(...)`: stores page size and optional continuation token.
- `Join(...)`: stores a single join definition.
- `WithHints(...)`: stores query hints for non-paged list execution.
- `Build()`: produces clusters, nullable `ColumnOptions`, nullable sorting, and nullable groups.

## Supported Operators

`Q.Operator` supports:

- Equality and comparisons: `Eq`, `NotEq`, `Gt`, `Gte`, `Lt`, `Lte`
- Array/document checks: `In`, `NotIn`, `Contains`, `NotContains`, `Len`
- Pattern matching: `Like`, `NotLike`
- Defined checks: `Defined`, `NotDefined`
- Vector search: `VectorDistance`
- Full-text search: `FTContains`, `NotFTContains`, `FTContainsAll`, `NotFTContainsAll`, `FTContainsAny`, `NotFTContainsAny`, `FTScore`

`VectorDistance` and `FTScore` require `ColumnOptions.Top > 0`.

Operator SQL mapping:

- `Eq`: `{property} = @param`
- `NotEq`: `{property} != @param`
- `Gt`: `{property} > @param`
- `Gte`: `{property} >= @param`
- `Lt`: `{property} < @param`
- `Lte`: `{property} <= @param`
- `In`: `ARRAY_CONTAINS({property}, @param)`
- `NotIn`: `NOT ARRAY_CONTAINS({property}, @param)`
- `Contains`: `ARRAY_CONTAINS(@param, {property})`
- `NotContains`: `NOT ARRAY_CONTAINS(@param, {property})`
- `Len`: `ARRAY_LENGTH({property}) = @param`
- `Like`: `{property} LIKE @param`
- `NotLike`: `{property} NOT LIKE @param`
- `Defined`: `IS_DEFINED({property})`
- `NotDefined`: `NOT IS_DEFINED({property})`
- `FTContains`: `FullTextContains({property}, @param)`
- `NotFTContains`: `NOT FullTextContains({property}, @param)`
- `FTContainsAll`: `FullTextContainsAll({property}, @param)`
- `NotFTContainsAll`: `NOT FullTextContainsAll({property}, @param)`
- `FTContainsAny`: `FullTextContainsAny({property}, @param)`
- `NotFTContainsAny`: `NOT FullTextContainsAny({property}, @param)`
- `FTScore`: `FullTextScore({property}, @param...)` in rank ordering.
- `VectorDistance`: `VectorDistance({property}, @param)` in projection/ranking.

Catalyst rule validation:

- `Column` is required.
- `Defined` / `NotDefined` must not receive a value.
- `Like` / `NotLike` require a non-blank value containing `%`.
- `Contains` / `NotContains` require a non-string `IEnumerable`.
- `VectorDistance` requires a non-empty `float[]`, max length 4096, with no `NaN` or infinity values.
- `FTContains` / `NotFTContains` require a string.
- Full-text array operators and `FTScore` require a non-empty `string[]` with no blank terms.
- All other operators require a value.

## Execution Strategies

`Builder/Strategies/` contains the query execution pipeline.

- `DirectQueryStrategy`: default path for simple queries, optimistic direct execution enabled, CPU-core-limited concurrency by default.
- `GatewayQueryStrategy`: fallback path for complex queries, conservative concurrency and small continuation tokens.
- `VectorSearchStrategy`: preferred for vector and hybrid queries, optimistic direct execution disabled.
- `EnhancedContextStrategy`: wraps the chosen strategy with merged recommendation/user hints.
- `QueryStrategySelector`: chooses the highest-priority strategy that can handle the query, honoring `ForceStrategy` when valid.
- `QueryContext`: carries query type, max item count, and hints.

Query type inference is string-based against the generated SQL text. Keep this in mind when adding new query keywords or operators.

Strategy defaults:

- Direct: `MaxItemCount` defaults to `Q.Limits.MaxItems` (`1000`), `EnableOptimisticDirectExecution = true`, `MaxConcurrency = Environment.ProcessorCount`.
- Gateway: `MaxItemCount` defaults to `Q.Limits.MaxItems`, `EnableOptimisticDirectExecution = false`, `MaxConcurrency = 1`, `ResponseContinuationTokenLimitInKb = 1`.
- Vector: `EnableOptimisticDirectExecution = false`, `MaxConcurrency = Environment.ProcessorCount`; `MaxItemCount` is applied only when hints include it.

Statistics recording:

- Strategy execution records both success and failure.
- Recorded failures include request charge accumulated before the exception and zero result count.
- All strategies return query text/parameters only if `recordQueries` is true.
- Strategy exceptions are rethrown after recording.

Recommendation behavior:

- `QueryStrategySelector` always asks `QueryTuner` for recommendations for the inferred `QueryType`.
- User hints override recommended hints.
- A forced strategy is used only when the named strategy exists and can handle the query.
- `GatewayQueryStrategy` can handle any query and is the fallback.

## Query Tuning And Statistics

`QueryTuner` records:

- Query hash
- Query type
- Request units
- Execution time
- Result count
- Success flag
- Timestamp
- Strategy used
- Hints used

The query hash is based on normalized query structure, not raw parameter values. Parameter names include generated catalyst ids, so `QueryTuner` normalizes `@\w+` to keep equivalent query shapes together.

Storage backends:

- `InMemoryStatisticsStorage`: default when no storage is configured.
- `FileStatisticsStorage`: JSON file persistence.
- `SqliteStatisticsStorage`: SQLite persistence with WAL mode, batched writes, and cleanup.

Path handling:

- Defaults are platform-aware.
- Azure App Service/Functions use local temp storage by default.
- Custom paths must never come from untrusted input.
- Unix-like systems attempt owner-only permissions on storage paths.

Rule-based recommendations:

- Vector, full-text, and hybrid searches suggest `MaxItemCount = 50` and `MaxBufferedItemCount = 50`.
- Aggregation suggests `MaxItemCount = 500` and `MaxBufferedItemCount = 1000`.
- Complex queries suggest `MaxConcurrency = 1` and `ResponseContinuationTokenLimitInKb = 1`.
- Simple and join queries get no rule-based hints.

Data-driven recommendations:

- Require at least 10 relevant samples in the last 24 hours.
- Hint configurations need at least 3 samples to be considered.
- Strategy recommendations need at least 5 samples.
- Hint recommendations prefer lower average RU, then higher success rate.
- Strategy recommendations prefer higher success rate, then lower average RU.

Storage behavior:

- `FileStatisticsStorage` writes at most the 1000 most recent entries.
- `FileStatisticsStorage` logs and returns empty data if the JSON file is corrupt.
- `SqliteStatisticsStorage` queues writes, flushes on batch size or timer, and retries failed flushes up to 3 times.
- `SqliteStatisticsStorage.Dispose()` attempts a final flush before closing commands and connection.
- `PlatformDetection.IsAzureEnvironment()` checks `WEBSITE_INSTANCE_ID` and Functions environment variables.
- `ValidateStoragePath` normalizes with `Path.GetFullPath`, rejects null bytes, and requires a rooted result.

## Optional Document Cache

Document caching is an opt-in add-on configured through `UniverseOptions.WithDocumentCache(...)`. A default `UniverseOptions` instance leaves `DocumentCache` null, and repository read paths do not allocate cache entries, serialize query parameters, clone documents, or build cache keys unless the option is set.

Cached operations:

- `IGalaxyBasic<T>.Get(id, partitionKey...)`
- `IGalaxy<T>.Get(...)`
- `IGalaxy<T>.Get<TS>(...)`

Uncached operations:

- `List`
- `Paged`
- `GenerateQuery`
- Query recommendations
- Stored procedure operations

Invalidation behavior:

- Successful create/modify/remove operations clear same-repository single-document query cache entries.
- Successful create/modify operations update known point-read entries.
- Successful remove operations delete known point-read entries.
- Bulk create and bulk modify update known submitted point entries and clear same-repository query entries after all batches succeed.
- External writers are not observed; they rely on cache time-to-live expiration.

Cache keys are SHA-256 hashes built from operation kind, database, container, source entity type, result type, and operation inputs. Raw ids, partition-key values, query text, and parameter values are not retained as readable dictionary keys. Cached values are cloned by default with `System.Text.Json` settings aligned with `UniverseSerializer`; clone failures skip or evict the affected cache entry.

## Data Model And Serialization

`ICosmicEntity` requires:

- `id`
- `AddedOn`
- `ModifiedOn`
- `CountAggregate`

`CosmicEntity` initializes `id` with a version 7 GUID and sets date fields in UTC.

`UniverseSerializer` wraps `System.Text.Json` through `JsonObjectSerializer` and defaults to:

- Optional naming policy
- Case-insensitive property names
- Ignore null values when writing
- Skip unmapped members
- Ignore read-only fields and properties

If a serializer naming policy is configured on `CosmosClientOptions`, query field names are converted with the same policy when SQL is generated.

## Partition Keys

Partition key conventions live in `Attributes/PartitionKeyAttribute.cs` and `Extensions/PartitionKeyExtension.cs`.

Rules:

- Entity types must inherit from `CosmicEntity` for `typeof(T).BuildPartitionKey()`.
- One to three partition-key properties are supported.
- Duplicate sequence values are rejected.
- Partition keys are ordered by sequence.
- The default partition-key path name is `property.Name.ToLowerCamelCase()`.
- `PartitionKeyAttribute` can override the path name with `keyName`.
- Runtime partition-key values cannot be null.

Partition-key methods:

- `Type.BuildPartitionKey()` returns path strings such as `/tenantId`.
- `ICosmicEntity.BuildPartitionKey()` returns a Cosmos `PartitionKey` for the entity instance.
- `ICosmicEntity.PartitionKeys()` returns ordered string values, useful for APIs that accept `params string[]`.

Nuances:

- `Type.BuildPartitionKey()` requires the type to inherit from `CosmicEntity`, not merely implement `ICosmicEntity`.
- Runtime value extraction works from `ICosmicEntity` and reflects on the concrete entity type.
- Default path names use `ToLowerCamelCase()`, so serialized naming policy and partition-key path names can diverge if a custom serializer naming policy is used without explicit `keyName`.

## Stored Procedures

`GalaxyProcedure` exposes stored procedure operations directly through Cosmos DB SDK script APIs.

Security nuance:

- `CreateSProc` and `ReplaceSProc` accept stored procedure bodies as raw JavaScript strings.
- The library checks execution parameters for JSON serializability, but it does not sandbox or validate stored procedure body content.
- Treat stored procedure body inputs as trusted administrative inputs only.

## Security Posture

The primary security guarantee is query injection protection.

Current protections:

- Query values are parameterized.
- Identifiers are validated before SQL construction.
- Identifier segments are rendered as bracketed Cosmos property access.
- FTScore values are parameterized even in rank ordering.
- RRF weights are validated as numeric content.
- Optional document-cache dictionary keys are hashed instead of storing raw ids, partition keys, query text, or parameter values as readable key strings.
- Query statistics store query hashes and performance metadata, not raw parameter values.
- `recordQueries` defaults to `false`.

Important risks and caveats:

- `recordQueries: true` includes full query text and parameter values in `Gravity`.
- `GenerateQuery` always returns full query text and parameters.
- README and examples print query parameters for debugging; do not copy that pattern into production logging.
- Persistent statistics paths can create files/directories; never derive those paths from user input.
- Stored procedure bodies are raw code strings and should remain privileged/admin-only inputs.

Security-sensitive review checklist:

- Any new string inserted into SQL text must either be enum-derived, validated as an identifier, or strictly parsed to a safe primitive form.
- Any new user value must be bound through `QueryDefinition.WithParameter`.
- Any new operator that expands multiple values should follow the `FTScore` pattern of separate parameter names.
- Any new identifier field in an option record should be added to `SanitizeInputs`.
- Any new debug/log output should be checked for query parameters, continuation tokens, Cosmos keys, and stored procedure bodies.
- Any new file path option should use `PlatformDetection.ValidateStoragePath` and document that it must not be user-controlled.
- Any new persistent local file should set restrictive permissions where possible.
- Any new stored procedure capability should assume body content is trusted admin code, not user input.

## Examples Map

`DarkMatter` is the runnable example project.

- `Program.cs`: wires Cosmos client, repositories, and sequential example execution.
- `Repository/MyRepo.cs`: sample repository using file persistence and `recordQueries: true`.
- `Repository/MyRepoVector.cs`: sample vector repository.
- `Models/`: sample document models.
- `Helpers/`: test/vector data generation.
- `Examples/Example1_BasicPagedQuery.cs`: paging.
- `Examples/Example2_AggregatesWithGroupBy.cs`: aggregates.
- `Examples/Example3_TopAndDistinct.cs`: top and distinct.
- `Examples/Example4_ComplexFiltering.cs`: complex filters.
- `Examples/Example5_SingleItemOperations.cs`: create/get/modify/remove.
- `Examples/Example6_BulkOperations.cs`: bulk create/modify.
- `Examples/Example7_AdvancedAggregation.cs`: aggregation model projection.
- `Examples/Example8_SalesAnalysis.cs`: date/range examples.
- `Examples/Example9_VectorSearch.cs`: vector search.
- `Examples/Example10_FullTextSearch.cs`: full-text search.
- `Examples/Example11_HybridVectorFullText.cs`: hybrid vector/full-text RRF.
- `Examples/Example12_QueryGeneration.cs`: generated SQL without execution.
- `Examples/Example13_QueryOptimization.cs`: strategies, hints, storage examples.
- `Examples/Example14_SQLInjectionProtection.cs`: injection protection demonstration.
- `Examples/Example15_ContainsOperator.cs`: contains/not-contains.
- `Examples/Example16_ProjectionSelect.cs`: projection select.

Example sequencing:

`Program.cs` currently runs the example set sequentially and sums RU. This gives the examples a rough integration-test shape, but it is not deterministic and depends on live Cosmos state, feature availability, and seeded sample data.

## Tests Map

`Universe.Tests` currently focuses on local, deterministic behavior.

- `Builder/OrbitQueryTests.cs`: fluent API mirrors declarative API.
- `Builder/NamingPolicyQueryTests.cs`: naming policy effects on SQL columns and aliases.
- `Builder/ProjectionSelectTests.cs`: type-based projection column extraction.
- `Builder/QueryTypeDetectorTests.cs`: generated-query classification for aggregation, full-text, vector, and hybrid strategy selection.
- `Extensions/PartitionKeyExtensionTests.cs`: partition-key path and runtime value ordering, explicit key names, duplicate sequence validation, and max-level validation.
- `Storage/FileStatisticsStorageTests.cs`: JSON statistics storage behavior.
- `Storage/InMemoryStatisticsStorageTests.cs`: in-memory storage behavior.
- `Storage/PlatformDetectionTests.cs`: path/platform detection behavior.
- `Storage/SqliteStatisticsStorageTests.cs`: SQLite persistence behavior.
- `Caching/DocumentCacheTests.cs`: cache key hashing, TTL, eviction, cloning, and query-key normalization behavior.
- `Caching/DocumentCacheRepositoryTests.cs`: opt-in repository cache behavior using fake Cosmos SDK abstractions.
- `Batch/BatchExecutionTests.cs`: in-memory atomic/bulk execution, rollback, ETags, chunking, concurrency, partial success, cancellation, and cache behavior.
- `Batch/LegacyInterfaceCompatibilityTests.cs`: default-interface compatibility for implementations built around the pre-batch `IGalaxyBasic<T>` contract.
- `Tuner/QueryTunerTests.cs`: recommendation and persistence behavior.
- `Tuner/QueryTunerPerformanceTests.cs`: tuner performance checks.
- `Helpers/TestStatisticsFactory.cs`: shared test statistics builders.

When adding tests:

- Query SQL tests should normalize generated parameter names unless the parameter naming itself is under test.
- Prefer unit-level query-generation tests over live Cosmos tests for query builder changes.
- Add malicious input cases when touching `QueryBuilder`, option records, operators, sorting, joins, groups, projections, or aliases.
- Storage tests that create files should use temporary or unique paths and clean up SQLite sidecar files (`-wal`, `-shm`) too.
- If a performance test is added, mark it so CI's `Category!=Performance` filter behaves intentionally.

## Documentation Map

- `README.md`: public package setup and API overview.
- `docs/FLUENT_QUERY_BUILDER.md`: fluent `Orbit` API reference.
- `docs/DECLARATIVE_QUERY_SYNTAX.md`: `Cluster` / `Catalyst` API reference.
- `docs/VECTORDISTANCE_USAGE.md`: vector search guide.
- `docs/FULLTEXT_USAGE.md`: full-text search guide.
- `docs/QUERY_EXECUTION_STRATEGIES.md`: strategy and tuning overview.
- `docs/ROADMAP_STATUS.md`: source-verified feature roadmap status and remaining gaps.
- `docs/SQLITE_STATISTICS_STORAGE.md`: SQLite persistence configuration.
- `docs/ADAPTIVE_QUERY_OPTIMIZATION_DESIGN.md`: design notes for adaptive optimization.

Documentation consistency:

- Public README examples should not imply `recordQueries: true` is a production default.
- Query examples should stay aligned with generated SQL behavior in tests.
- Feature docs should mention Cosmos DB feature prerequisites when relevant, especially vector and full-text search.
- If `UniverseQuery.csproj` package metadata changes, verify README installation/version references.

## Development Conventions

- Keep public API changes deliberate; this is a NuGet package.
- Prefer existing domain names and astronomy terminology.
- Keep query generation changes covered by unit tests.
- Treat `QueryBuilder.cs` as security-sensitive.
- Do not convert parameterized values into interpolated SQL.
- If adding identifier-bearing options, route them through `ValidateIdentifier` before formatting.
- If adding raw SQL-like syntax, add explicit tests for injection attempts.
- If changing naming policy behavior, update naming policy and projection tests.
- If changing storage behavior, include file and SQLite tests as applicable.
- Use `UniverseException` for library validation errors.
- Cosmos SDK exceptions are often allowed through or wrapped only with status-level context; preserve that pattern unless intentionally changing error behavior.
- `dotnet format` can touch broad files; prefer scoped formatting when possible.

## Git, CI, And Review

Repository workflow from existing instructions:

- Main branch: `dev`
- Production branch: `prod`
- Current working branch may vary; inspect with `git branch --show-current`.
- GitHub Actions build the .NET solution on push.
- NuGet publishing is handled by release workflow.
- Semgrep scans run on PRs and commits.
- After every commit or before pushing, run local CodeRabbit review when available:

```bash
coderabbit review
```

## Common Gotchas

- `recordQueries` is convenient but can expose sensitive parameter values.
- Document caching is disabled by default; enabling it creates process-local state and external writers are visible after TTL expiration, not through distributed invalidation.
- `DarkMatter` examples intentionally use debug-style logging and should not define production logging standards.
- `Paged()` stops after the first non-empty page except for `GROUP BY`, where continuation token behavior is different.
- Query hints are not accepted with fluent paged queries.
- `GetAsync()` does not apply most list-shaping options.
- `ColumnOptions.Top` is required for rank/vector operators.
- Cosmos DB column names are case-sensitive.
- `PartitionKey` attribute sequence matters for multi-level partition keys.
- File/SQLite persistence defaults differ on Azure versus local runtime.
- Stored procedure methods are administrative power tools, not user-facing script execution endpoints.
