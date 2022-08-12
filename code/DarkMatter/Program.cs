﻿using Microsoft.Azure.Cosmos;
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
        Serializer = new UniverseSerializer()
    }
);

IGalaxy<MyObject> galaxy = new MyRepo(
    db: cosmosClient.GetDatabase("<DATABASE NAME>"),
    container: "<CONTAINER NAME>",
    partitionKey: "/<PARTITION KEY>"
);
// end of dependency injection

// actual use of UniverseQuery
(Gravity g, IList<MyObject> T) = await galaxy.Paged(
    new Q.Page(50),
    new List<Catalyst>
    {
        new(nameof(MyObject.Links), "<VALUE TO QUERY>", Operator: Q.Operator.In),
        new(nameof(MyObject.Code), "<VALUE TO QUERY>", Where: Q.Where.Or)
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