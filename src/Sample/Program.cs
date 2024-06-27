using Behaviour;
using System.Collections.Concurrent;
using System.Security.Principal;

var builder = WebApplication.CreateSlimBuilder(args);
var app = builder.Build();
var group = app.MapGroup("/application");

group.MapPost("/SubmitApplication/{applicationId}", async (string applicationId, Application application, IPrincipal principal, ILogger logger) =>
{
    var context = new BehaviourContext(logger)
    {
        Principal = principal,
        Operation = "SubmitApplication",
        Resource = applicationId
    };

    var result = await new BehaviourRunner()
        .ExecuteAsync(context, [new SubmitApplicationFeature()]);

    return Results.Ok(result.Output);
});

app.Run();

public record Application(string FirstName, string LastName, int Age);

public record Product(bool ExistingUsers, int MinimumAge);

public record CreatedApplicationEvent(string applicationId);

public class SubmitApplicationFeature : BehaviourFeature<Application>
{
    public override List<BehaviourScenario> Scenarios => [
        new ProductLookup(),
        new AuthorizationPolicy(),
        new ApplicationValidation(),
        new AgeRestriction(),
        new ApplicationStore(),
        new AuditLog()
    ];
}

public class ProductLookup : BehaviourScenario
{
    public override BehaviourPhase Given(BehaviourContext context) => BehaviourPhase.Initialize;

    public override Task<BehaviourResult> ThenAsync(BehaviourContext context)
    {
        context.Set(new Product(ExistingUsers: true, MinimumAge: 18));
        return context.Next();
    }
}

public class AuthorizationPolicy : BehaviourScenario
{
    public override BehaviourPhase Given(BehaviourContext context) => BehaviourPhase.Before;

    public override bool When(BehaviourContext context)
        => context.Principal?.Identity?.IsAuthenticated == context.Get<Product>().ExistingUsers;

    public override Task<BehaviourResult> ThenAsync(BehaviourContext context)
        => context.Complete(code: 401);
}

public class AgeRestriction : BehaviourScenario<Application>
{
    public override BehaviourPhase Given(BehaviourContext context) => BehaviourPhase.Before;

    public override bool When(BehaviourContext context, Application input)
        => input.Age < context.Get<Product>().MinimumAge;

    public override Task<BehaviourResult> ThenAsync(BehaviourContext context, Application input)
        => context.Complete(code: 400, message: $"Minimum age {context.Get<Product>().MinimumAge}");
}

public class ApplicationValidation : BehaviourScenario<Application>
{
    public override BehaviourPhase Given(BehaviourContext context) => BehaviourPhase.Before;

    public override bool When(BehaviourContext context, Application input)
        => input.FirstName is null || input.LastName is null;

    public override Task<BehaviourResult> ThenAsync(BehaviourContext context, Application input)
        => context.Complete(code: 400, message: "First and last name required");
}

public class ApplicationStore : BehaviourScenario<Application>
{
    private static readonly ConcurrentDictionary<string, Application> Store = [];

    public override Task<BehaviourResult> ThenAsync(BehaviourContext context, Application input)
    {
        var applicationId = context.Resource!;
        Store[applicationId] = input;
        context.Append(new CreatedApplicationEvent(applicationId));
        return context.Next(code: 200, output: applicationId);
    }
}

public class AuditLog : BehaviourScenario
{
    private static readonly ConcurrentDictionary<string, string> Store = [];

    public override BehaviourPhase Given(BehaviourContext context) => BehaviourPhase.After;

    public override Task<BehaviourResult> ThenAsync(BehaviourContext context)
    {
        Store[context.CorrelationId] = $"Application {context.Result!.Output} created by {context.Principal!.Identity!.Name}";
        return base.ThenAsync(context);
    }
}

public static class BehaviourContextExtensions
{
    public static void Set<TItem>(this BehaviourContext context, TItem? item)
        => context.State.Add(nameof(TItem), item);

    public static TItem Get<TItem>(this BehaviourContext context) where TItem : class
        => context.State.GetValueOrDefault(nameof(TItem)) as TItem ?? throw new KeyNotFoundException(nameof(TItem));

    public static void Append<TItem>(this BehaviourContext context, TItem? item)
        => context.State.Add(Guid.NewGuid().ToString(), item);

    public static IEnumerable<TItem> GetAll<TItem>(this BehaviourContext context)
        => context.State.Values.OfType<TItem>();
}
