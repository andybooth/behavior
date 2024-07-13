namespace Behavior;

public class BehaviorContext(ILogger logger)
{
    public ILogger Logger { get; } = logger;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
    public IPrincipal? Principal { get; init; }
    public string? Operation { get; init; }
    public string? Resource { get; init; }
    public object? Input { get; init; }
    public Dictionary<string, object?> State { get; } = [];
    public BehaviorResult? Result { get; set; }
}

public class BehaviorResult
{
    public bool IsComplete { get; init; } = false;
    public int? Code { get; init; }
    public List<string> Messages { get; init; } = [];
    public object? Output { get; init; }
}

public enum BehaviorPhase
{
    None,
    Initialize,
    Before,
    On,
    After
}

public abstract class BehaviorFeature
{
    public virtual bool Given(BehaviorContext context) => true;
    public virtual Task<bool> GivenAsync(BehaviorContext context) => Task.FromResult(true);
    public virtual List<BehaviorScenario> Scenarios { get; } = [];
}

public abstract class BehaviorFeature<TInput> : BehaviorFeature
{
    public override bool Given(BehaviorContext context) => context.Input is TInput input
        && Given(context, input);

    public virtual bool Given(BehaviorContext context, TInput input) => true;

    public override async Task<bool> GivenAsync(BehaviorContext context) => context.Input is TInput input
        && await GivenAsync(context, input);

    public virtual Task<bool> GivenAsync(BehaviorContext context, TInput input) => Task.FromResult(true);
}

public abstract class BehaviorScenario
{
    public virtual string Name => GetType().Name;
    public virtual BehaviorPhase? Given(BehaviorContext context) => BehaviorPhase.On;
    public virtual Task<BehaviorPhase?> GivenAsync(BehaviorContext context) => Task.FromResult<BehaviorPhase?>(BehaviorPhase.On);
    public virtual bool When(BehaviorContext context) => true;
    public virtual Task<bool> WhenAsync(BehaviorContext context) => Task.FromResult(true);
    public virtual BehaviorResult? Then(BehaviorContext context) => null;
    public virtual Task<BehaviorResult?> ThenAsync(BehaviorContext context) => Task.FromResult<BehaviorResult?>(null);
}

public abstract class BehaviorScenario<TInput> : BehaviorScenario
{
    public override async Task<BehaviorPhase?> GivenAsync(BehaviorContext context) => context.Input is TInput input
        ? await GivenAsync(context, input)
        : BehaviorPhase.None;

    public virtual Task<BehaviorPhase?> GivenAsync(BehaviorContext context, TInput input) => Task.FromResult<BehaviorPhase?>(BehaviorPhase.On);

    public override bool When(BehaviorContext context) => context.Input is TInput input
        && When(context, input);

    public virtual bool When(BehaviorContext context, TInput input) => true;

    public override async Task<bool> WhenAsync(BehaviorContext context) => context.Input is TInput input
        && await WhenAsync(context, input);

    public virtual Task<bool> WhenAsync(BehaviorContext context, TInput input) => Task.FromResult(true);

    public override BehaviorResult? Then(BehaviorContext context) => context.Input is TInput input
        ? Then(context, input)
        : null;

    public virtual BehaviorResult? Then(BehaviorContext context, TInput input) => null;

    public override Task<BehaviorResult?> ThenAsync(BehaviorContext context) => context.Input is TInput input
        ? ThenAsync(context, input)
        : Task.FromResult<BehaviorResult?>(null);

    public virtual Task<BehaviorResult?> ThenAsync(BehaviorContext context, TInput input)
        => Task.FromResult<BehaviorResult?>(null);
}

public partial class BehaviorRunner
{
    public async Task<BehaviorResult?> ExecuteAsync(BehaviorContext context, List<BehaviorFeature> features)
    {
        var loggerState = new Dictionary<string, object?>
        {
            { nameof(context.CorrelationId), context.CorrelationId },
            { nameof(context.Operation), context.Operation },
            { nameof(context.Resource), context.Resource }
        };

        using var scope = context.Logger.BeginScope(loggerState);

        var scenarios = new List<(BehaviorPhase?, BehaviorScenario)>();

        foreach (var feature in features)
        {
            if (feature.Given(context) && await feature.GivenAsync(context))
            {
                foreach (var scenario in feature.Scenarios)
                {
                    var phase = scenario.Given(context) ?? await scenario.GivenAsync(context);

                    if (phase is not null && phase != BehaviorPhase.None)
                    {
                        scenarios.Add((phase, scenario));
                    }
                }
            }
        }

        if (scenarios.Count == 0)
        {
            return default;
        }

        await ExecuteScenariosAsync(context, scenarios, BehaviorPhase.Initialize);
        await ExecuteScenariosAsync(context, scenarios, BehaviorPhase.Before);
        await ExecuteScenariosAsync(context, scenarios, BehaviorPhase.On);
        await ExecuteScenariosAsync(context, scenarios, BehaviorPhase.After);

        return default;
    }

    private static async Task ExecuteScenariosAsync(BehaviorContext context, List<(BehaviorPhase?, BehaviorScenario)> scenariosPhases, BehaviorPhase phase)
    {
        var scenarios = scenariosPhases.Where(s => s.Item1 == phase).Select(s => s.Item2).ToList();

        if (scenarios.Count == 0)
        {
            return;
        }

        if (context.Result?.IsComplete is true)
        {
            return;
        }

        foreach (var scenario in scenarios)
        {
            if (!scenario.When(context) && !await scenario.WhenAsync(context))
            {
                continue;
            }

            var loggerState = new Dictionary<string, object?>
            {
                { nameof(BehaviorPhase), phase }
            };

            using var scope = context.Logger.BeginScope(loggerState);

            try
            {
                LogScenarioBegin(context.Logger, scenario.Name);

                context.Result = scenario.Then(context) ?? await scenario.ThenAsync(context);

                LogScenarioEnd(context.Logger, scenario.Name);
            }
            catch (Exception ex)
            {
                LogScenarioError(context.Logger, ex, scenario.Name);

                context.Result = new() { IsComplete = true };
            }

            if (context.Result?.IsComplete is true)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scenario '{ScenarioName}' Begin")]
    private static partial void LogScenarioBegin(ILogger logger, string scenarioName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scenario '{ScenarioName}' End")]
    private static partial void LogScenarioEnd(ILogger logger, string scenarioName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scenario '{ScenarioName}' Error")]
    private static partial void LogScenarioError(ILogger logger, Exception exception, string scenarioName);
}
