# simple-cosmos
A simpler way of querying a CosmosDb Namespace

## How-to:
1. Your models / cosmos entities should inherit from the interface
```csharp
public class MyCosmosEntity : ICosmosEntity
{
  public string FirstName { get; set; }
  
  public string LastName { get; set; }
  
  // The properties below are implementations from ICosmosEntity
  public string Id { get; set; }
  public DateTime AddedOn { get; set; }
  public DateTime ModifiedOn { get; set; }

  public string PartitionKey => FirstName;
}
```

2. Create a repository like so:
```csharp
public class MyRepository : SimpleCosmos<MyModel>
{
    public MyRepository(Database db, string container, string partitionKey) : base(db, container, partitionKey)
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
        Serializer = new CosmosCustomSerializer() // This is from SimpleCosmos
    }
));
```

4. In your Startup.cs / Main method / Program.cs, configure your CosmosDb repository like so:
```csharp
_ = services.AddScoped<ISimpleCosmos<MyModel>, MyRepository>(service => new(
    db: service.GetRequiredService<CosmosClient>().GetDatabase("cosmos-database"),
    container: "container-name",
    partitionKey: "/partitionKey"
));
```

5. Inject your `ISimpleCosmos<MyModel>` dependency into your classes and enjoy a simpler way to query CosmosDb

#### Example Query:
```csharp
MyModel myModel = await ISimpleCosmos<MyModel>.Get(new QueryParameter("LastName", "last name value", DbType.String));
```
