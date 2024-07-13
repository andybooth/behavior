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

public record Product(bool ExistingUser, int MinimumAge);

public record CreatedApplicationEvent(string applicationId);

public class SubmitApplication(IFeatureManager featureManager) : BehaviorFeature<Application>
{
    public override Task<bool> GivenAsync(BehaviorContext context)
        => featureManager.IsEnabledAsync(nameof(SubmitApplication));

    public override List<BehaviorScenario> Scenarios => [
        new ProductLookup(),
        new ExistingUserRequired(),
        new FirstNameRequired(),
        new LastNameRequired(),
        new MinimumAge(),
        new StoreApplication(),
        new AuditLog()
    ];
}

public abstract class Lookup<T> : BehaviorScenario
{
    public override BehaviorPhase? Given(BehaviorContext context) => BehaviorPhase.OnPrepare;

    public override async Task<BehaviorResult?> ThenAsync(BehaviorContext context)
    {
        context.SetState(await LookupAsync(context));
        return default;
    }

    public abstract Task<T> LookupAsync(BehaviorContext context);
}

public class Authorizer : BehaviorScenario
{
    public override BehaviorPhase? Given(BehaviorContext context) => BehaviorPhase.AfterPrepare;

    public override BehaviorResult Then(BehaviorContext context)
        => new() { IsComplete = true, Code = 401, Messages = [$"Authorizer error {Name}"] };
}

public class Validator<T> : BehaviorScenario<T>
{
    public override BehaviorPhase? Given(BehaviorContext context) => BehaviorPhase.BeforeRun;

    public override BehaviorResult Then(BehaviorContext context, T input)
        => new() { IsComplete = true, Code = 400, Messages = [$"Validator error {Name}"] };
}

public class ProductLookup : Lookup<Product>
{
    public override Task<Product> LookupAsync(BehaviorContext context)
    {
        var product = new Product(ExistingUser: true, MinimumAge: 18);
        return Task.FromResult(product);
    }
}

public class ExistingUserRequired : Authorizer
{
    public override bool When(BehaviorContext context)
        => context.Principal?.Identity?.IsAuthenticated == context.GetState<Product>().ExistingUser;
}

public class FirstNameRequired : Validator<Application>
{
    public override bool When(BehaviorContext context, Application input)
        => input.FirstName is null;
}

public class LastNameRequired : Validator<Application>
{
    public override bool When(BehaviorContext context, Application input)
        => input.LastName is null;
}

public class MinimumAge : Validator<Application>
{
    public override bool When(BehaviorContext context, Application input)
        => input.Age < context.GetState<Product>().MinimumAge;
}

public class StoreApplication : BehaviorScenario<Application>
{
    private static readonly ConcurrentDictionary<string, Application> Store = [];

    public override BehaviorResult? Then(BehaviorContext context, Application input)
    {
        var applicationId = context.Resource!;
        Store[applicationId] = input;
        return new() { Output = applicationId };
    }
}

public class AuditLog : BehaviorScenario
{
    private static readonly ConcurrentDictionary<string, string> Store = [];

    public override BehaviorPhase? Given(BehaviorContext context) => BehaviorPhase.AfterRun;

    public override BehaviorResult? Then(BehaviorContext context)
    {
        Store[context.CorrelationId] = $"Application {context.Result!.Output} created by {context.Principal!.Identity!.Name}";
        return default;
    }
}

public static class BehaviorContextExtensions
{
    public static void SetState<TItem>(this BehaviorContext context, TItem? item)
        => context.State.Add(nameof(TItem), item);

    public static TItem GetState<TItem>(this BehaviorContext context) where TItem : class
        => context.State.GetValueOrDefault(nameof(TItem)) as TItem ?? throw new KeyNotFoundException(nameof(TItem));
}
