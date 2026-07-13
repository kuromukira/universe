using DarkMatter.Models;
using Universe;
using Universe.Interfaces;
using Universe.Response;

namespace DarkMatter.Examples;

/// <summary>Demonstrates ETag-protected atomic replace and conditional patch operations.</summary>
public class Example17_AtomicOperations(IGalaxy<MyObject> galaxy) : ExampleBase(galaxy)
{
    public override async Task<double> RunAsync()
    {
        Console.WriteLine("\n=== EXAMPLE 17: Atomic Operations ===\n");

        MyObject item = new()
        {
            Category = "atomic-example",
            Code = Guid.CreateVersion7().ToString(),
            Name = "Atomic example",
            Description = "Created for the transactional batch example.",
            Price = 1,
            Quantity = 1
        };

        (Gravity created, _) = await galaxy.Create(item);
        try
        {
            item.Description = "Replaced with an ETag precondition.";
            AtomicBatchResult<MyObject> replace = await galaxy
                .Atomic(item.Category, item.Code)
                .Replace(item, created.ETag)
                .ExecuteAsync();

            BatchOperationResult<MyObject> replaceOperation = replace.Operations.Single();
            if (!replace.Succeeded || !replaceOperation.Succeeded || string.IsNullOrWhiteSpace(replaceOperation.ETag))
                throw new InvalidOperationException("The ETag-protected replacement failed.");

            string replacementETag = replaceOperation.ETag;
            AtomicBatchResult<MyObject> patch = await galaxy
                .Atomic(item.Category, item.Code)
                .Patch(
                    item.id,
                    operations => operations.Increment(model => model.Quantity, 1),
                    replacementETag,
                    condition => condition.Equal(model => model.Name, "Atomic example"))
                .ExecuteAsync();

            Console.WriteLine($"Replace status: {replace.StatusCode}; conditional patch status: {patch.StatusCode}");
            ruUsed = created.RU + replace.Gravity.RU + patch.Gravity.RU;
            return ruUsed;
        }
        finally
        {
            await galaxy.Remove(item.id, item.Category, item.Code);
        }
    }
}
