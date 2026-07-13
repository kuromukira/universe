// filepath: d:\github\universe\code\DarkMatter\Program.cs

using Microsoft.Azure.Cosmos;
using DarkMatter.Repository;
using DarkMatter.Examples;
using DarkMatter.Models;
using Universe.Builder.Options;
using Universe.Extensions;
using System.Text.Json;

namespace DarkMatter;

class Program
{
    static async Task Main(string[] args)
    {
        // Database connection details
        string CosmosDbUri = "<FROM AZURE PORTAL>";
        string CosmosDbPrimaryKey = "<FROM AZURE PORTAL>";

        // Initialize CosmosClient with appropriate options
        CosmosClient cosmosClient = new(
            CosmosDbUri,
            CosmosDbPrimaryKey,
            clientOptions: new()
            {
                Serializer = new UniverseSerializer(JsonNamingPolicy.CamelCase),
                AllowBulkExecution = true
            }
        );

        // Create the repository instance
        MyRepo galaxy = new(
            client: cosmosClient,
            database: "test-database",
            container: "my-container",
            partitionKey: typeof(MyObject).BuildPartitionKey()
        );
        MyRepoVector vectorGalaxy = new(
            client: cosmosClient,
            database: "test-database",
            container: "vector-container",
            partitionKey: typeof(MyObjectVector).BuildPartitionKey()
        );

        // Track the total request units spent
        double totalRu = 0.0;

        // Run all examples sequentially
        totalRu += await new Example1_BasicPagedQuery(galaxy).RunAsync();
        totalRu += await new Example2_AggregatesWithGroupBy(galaxy).RunAsync();
        totalRu += await new Example3_TopAndDistinct(galaxy).RunAsync();
        totalRu += await new Example4_ComplexFiltering(galaxy).RunAsync();
        totalRu += await new Example5_SingleItemOperations(galaxy).RunAsync();
        totalRu += await new Example6_BulkOperations(galaxy).RunAsync();
        totalRu += await new Example7_AdvancedAggregation(galaxy).RunAsync();
        totalRu += await new Example8_SalesAnalysis(galaxy).RunAsync();
        totalRu += await new Example9_VectorSearch(vectorGalaxy).RunAsync();
        totalRu += await new Example10_FullTextSearch(galaxy).RunAsync();
        totalRu += await new Example11_HybridVectorFullText(vectorGalaxy).RunAsync();
        totalRu += await new Example12_QueryGeneration(galaxy).RunAsync();
        totalRu += await new Example13_QueryOptimization(vectorGalaxy).RunAsync();
        totalRu += await new Example14_SqlInjectionProtection(galaxy).RunAsync();
        totalRu += await new Example15_ContainsOperator(galaxy).RunAsync();
        totalRu += await new Example16_ProjectionSelect(galaxy).RunAsync();
        totalRu += await new Example17_AtomicOperations(galaxy).RunAsync();

        // Display summary information
        Console.WriteLine($"\nTotal RU spent across all examples: {totalRu}");
        Console.WriteLine("\nExamples complete. Press Enter to exit.");
        Console.ReadLine();
    }
}
