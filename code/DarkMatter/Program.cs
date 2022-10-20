using Microsoft.Azure.Cosmos;
using Universe;
using Universe.Interfaces;
using Universe.Options;
using Universe.Options.Query;
using Universe.Response;

string CosmosDbUri = "<FROM AZURE>";
string CosmosDbPrimaryKey = "<FROM AZURE>";

// Imagine this part here as your dependency injection
CosmosClient cosmosClient = new(
    CosmosDbUri,
    CosmosDbPrimaryKey,
    clientOptions: new()
    {
        Serializer = new UniverseSerializer(),
        AllowBulkExecution = true // This will tell the underlying code to allow async bulk operations
    }
);

IGalaxy<MyObject> galaxy = new MyRepo(
    client: cosmosClient,
    database: "<DATABASE NAME>",
    container: "<CONTAINER NAME>",
    partitionKey: "/<PARTITION KEY>"
);
// end of dependency injection

// actual use of UniverseQuery
(Gravity g, IList<MyObject> T) = await galaxy.Paged(
    page: new Q.Page(50),
    catalysts: new List<Catalyst>
    {
        new(nameof(MyObject.Links), "<VALUE TO QUERY>", Operator: Q.Operator.In),
        new(nameof(MyObject.Code), "<VALUE TO QUERY>", Where: Q.Where.Or)
    },
    columnOptions: new(
        Names: new List<string>
        {
            nameof(MyObject.id),
            nameof(MyObject.Code),
            nameof(MyObject.Name),
            nameof(MyObject.Description)
        }
    ),
    sorting: new List<Sorting.Option>
    {
        new(nameof(MyObject.Name), Sorting.Direction.DESC)
    }
);

Console.WriteLine($"RU Spent: {g.RU}");
Console.WriteLine(g.Query.Text);
foreach (var p in g.Query.Parameters)
    Console.WriteLine($"{p.Item1} = {p.Item2}");
Console.WriteLine($"Result Rows: {T.Count}");

Console.ReadLine();

// Object definitions
class MyObject : ICosmicEntity
{
    // Universe Generated
    public string id { get; set; }
    public DateTime AddedOn { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string PartitionKey => Code;

    public string Code { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public string[] Links { get; set; }
}

class MyRepo : Galaxy<MyObject>
{
#if DEBUG
    public MyRepo(CosmosClient client, string database, string container, string partitionKey) : base(client, database, container, partitionKey, true)
    {
    }
#else
    public MyRepo(CosmosClient client, string database, string container, string partitionKey) : base(client, database, container, partitionKey)
    {
    }
#endif
}