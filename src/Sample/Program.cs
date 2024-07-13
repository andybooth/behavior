using Behavior;
using Microsoft.FeatureManagement;
using System.Collections.Concurrent;
using System.Security.Principal;

var builder = WebApplication.CreateSlimBuilder(args);
var app = builder.Build();
var api = app.MapGroup("/application");

api.MapPost("/{applicationId}", async (string applicationId, Application application, IPrincipal principal, ILogger logger, IFeatureManager featureManager) =>
{
    var context = new BehaviorContext(logger)
    {
        Principal = principal,
        Operation = nameof(SubmitApplication),
        Resource = applicationId
    };

    var result = await new BehaviorRunner()
        .ExecuteAsync(context, [new SubmitApplication(featureManager)]);

    return Results.Ok(result?.Output);
});

app.Run();

public record Application(string FirstName, string LastName, int Age);

public record Product(bool ExistingUsers, int MinimumAge);

public record CreatedApplicationEvent(string applicationId);

public class SubmitApplication(IFeatureManager featureManager) : BehaviorFeature<Application>
{
    public override Task<bool> GivenAsync(BehaviorContext context)
        => featureManager.IsEnabledAsync(nameof(SubmitApplication));

    public override List<BehaviorScenario> Scenarios => [
        new ProductLookup(),
        new AuthorizationPolicy(),
        new ApplicationValidation(),
        new AgeRestriction(),
        new ApplicationStore(),
        new AuditLog()
    ];
}

public class ProductLookup : BehaviorScenario
{
    public override BehaviorPhase? Given(BehaviorContext context) => BehaviorPhase.Initialize;

    public override BehaviorResult? Then(BehaviorContext context)
    {
        context.Set(new Product(ExistingUsers: true, MinimumAge: 18));
        return default;
    }
}

public class AuthorizationPolicy : BehaviorScenario
{
    public override BehaviorPhase? Given(BehaviorContext context) => BehaviorPhase.Before;

    public override bool When(BehaviorContext context)
        => context.Principal?.Identity?.IsAuthenticated == context.Get<Product>().ExistingUsers;

    public override BehaviorResult? Then(BehaviorContext context)
        => new() { IsComplete = true, Code = 401 };
}

public class AgeRestriction : BehaviorScenario<Application>
{
    public override BehaviorPhase? Given(BehaviorContext context) => BehaviorPhase.Before;

    public override bool When(BehaviorContext context, Application input)
        => input.Age < context.Get<Product>().MinimumAge;

    public override BehaviorResult? Then(BehaviorContext context, Application input)
        => new() { IsComplete = true, Code = 400, Messages = [$"Minimum age {context.Get<Product>().MinimumAge}"] };
}

public class ApplicationValidation : BehaviorScenario<Application>
{
    public override BehaviorPhase? Given(BehaviorContext context) => BehaviorPhase.Before;

    public override bool When(BehaviorContext context, Application input)
        => input.FirstName is null || input.LastName is null;

    public override BehaviorResult Then(BehaviorContext context, Application input)
        => new() { IsComplete = true, Code = 400, Messages = ["First name and last name required"] };
}

public class ApplicationStore : BehaviorScenario<Application>
{
    private static readonly ConcurrentDictionary<string, Application> Store = [];

    public override BehaviorResult? Then(BehaviorContext context, Application input)
    {
        var applicationId = context.Resource!;
        Store[applicationId] = input;
        context.Append(new CreatedApplicationEvent(applicationId));
        return new() { Output = applicationId };
    }
}

public class AuditLog : BehaviorScenario
{
    private static readonly ConcurrentDictionary<string, string> Store = [];

    public override BehaviorPhase? Given(BehaviorContext context) => BehaviorPhase.After;

    public override BehaviorResult? Then(BehaviorContext context)
    {
        Store[context.CorrelationId] = $"Application {context.Result!.Output} created by {context.Principal!.Identity!.Name}";
        return default;
    }
}

public static class BehaviorContextExtensions
{
    public static void Set<TItem>(this BehaviorContext context, TItem? item)
        => context.State.Add(nameof(TItem), item);

    public static TItem Get<TItem>(this BehaviorContext context) where TItem : class
        => context.State.GetValueOrDefault(nameof(TItem)) as TItem ?? throw new KeyNotFoundException(nameof(TItem));

    public static void Append<TItem>(this BehaviorContext context, TItem? item)
        => context.State.Add(Guid.NewGuid().ToString(), item);

    public static IEnumerable<TItem> GetAll<TItem>(this BehaviorContext context)
        => context.State.Values.OfType<TItem>();
}
