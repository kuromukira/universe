using System.Data;
using Microsoft.Azure.Cosmos;
using Universe;
using Universe.Interfaces;
using Universe.Options;
using Universe.Options.Query;
using Universe.Response;

string CosmosDbUri = "<GET THIS FROM AZURE>";
string CosmosDbPrimaryKey = "<GET THIS FROM AZURE>";

// Imagine this part here as your dependency injection
CosmosClient cosmosClient = new(
    CosmosDbUri,
    CosmosDbPrimaryKey,
    clientOptions: new()
    {
        Serializer = new UniverseSerializer()
    }
);

IGalaxy<MyObject> galaxy = new MyRepo(
    db: cosmosClient.GetDatabase("<YOUR COSMOS DB NAME>"),
    container: "<CONTAINER NAME>",
    partitionKey: "/<PARTITION KEY>"
);
// end of dependency injection

// actual use of UniverseQuery
(Gravity g, IList<MyObject> T) = await galaxy.Paged(
    new Query.Page(50, null),
    new List<QueryParameter>
    {
        new(nameof(MyObject.Links), "<VALUE TO QUERY>", DbType.String, Operator: Query.Operator.In)
    },
    new List<string>
    {
        nameof(MyObject.id),
        nameof(MyObject.Code),
        nameof(MyObject.Name),
        nameof(MyObject.Description)
    }
);

Console.WriteLine($"RU Spent: {g.RU}");
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
    public MyRepo(Database db, string container, string partitionKey) : base(db, container, partitionKey)
    {
    }
}