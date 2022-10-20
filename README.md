# Universe
A simpler way of querying a CosmosDb Namespace

## How-to:
1. Your models / cosmos entities should inherit from the interface
```csharp
public class MyCosmosEntity : ICosmicEntity
{
  public string FirstName { get; set; }
  
  public string LastName { get; set; }
  
  // The properties below are implementations from ICosmicEntity
  public string id { get; set; }
  public DateTime AddedOn { get; set; }
  public DateTime ModifiedOn { get; set; }

  [JsonIgnore]
  public string PartitionKey => FirstName;
}
```

2. Create a repository like so:
```csharp
public class MyRepository : Galaxy<MyModel>
{
    public MyRepository(CosmosClient client, string database, string container, string partitionKey) : base(client, database, container, partitionKey)
    {
    }
}

// If you want to see debug information such as the full Query text executed, use the format below:
public class MyRepository : Galaxy<MyModel>
{
    public MyRepository(CosmosClient client, string database, string container, string partitionKey) : base(client, database, container, partitionKey, true)
    {
    }
}
```

3. In your Startup.cs / Main method / Program.cs, configure the CosmosClient like so:
```csharp
_ = services.AddScoped(_ => new CosmosClient(
    System.Environment.GetEnvironmentVariable("CosmosDbUri"),
    System.Environment.GetEnvironmentVariable("CosmosDbPrimaryKey"),
    clientOptions: new()
    {
        Serializer = new UniverseSerializer() // This is from Universe.Options
        AllowBulkExecution = true // This will tell the underlying code to allow async bulk operations
    }
));
```

4. In your Startup.cs / Main method / Program.cs, configure your CosmosDb repository like so:
```csharp
_ = services.AddScoped<IGalaxy<MyModel>, MyRepository>(service => new(
    client: service.GetRequiredService<CosmosClient>(),
    database: "database-name",
    container: "container-name",
    partitionKey: "/partitionKey"
));
```

5. Inject your `IGalaxy<MyModel>` dependency into your classes and enjoy a simpler way to query CosmosDb

#### Example Query:
```csharp
MyModel myModel = await IGalaxy<MyModel>.Get(new Catalyst("LastName", "last name value"));
```
