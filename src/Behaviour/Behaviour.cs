namespace Behaviour;

public class BehaviourContext(ILogger logger)
{
    public ILogger Logger { get; } = logger;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
    public IPrincipal? Principal { get; init; }
    public string? Operation { get; init; }
    public string? Resource { get; init; }
    public object? Input { get; init; }
    public Dictionary<string, object?> State { get; } = [];
    public BehaviourResult? Result { get; set; }
}

public class BehaviourResult
{
    public bool IsComplete { get; init; } = false;
    public int? Code { get; init; }
    public List<string> Messages { get; init; } = [];
    public object? Output { get; init; }
}

public enum BehaviourPhase
{
    None,
    Initialize,
    Before,
    On,
    After
}

public abstract class BehaviourFeature
{
    public virtual bool Given(BehaviourContext context) => true;
    public virtual Task<bool> GivenAsync(BehaviourContext context) => Task.FromResult(true);
    public virtual List<BehaviourScenario> Scenarios { get; } = [];
}

public abstract class BehaviourFeature<TInput> : BehaviourFeature
{
    public override bool Given(BehaviourContext context) => context.Input is TInput input
        && Given(context, input);

    public virtual bool Given(BehaviourContext context, TInput input) => true;

    public override async Task<bool> GivenAsync(BehaviourContext context) => context.Input is TInput input
        && await GivenAsync(context, input);

    public virtual Task<bool> GivenAsync(BehaviourContext context, TInput input) => Task.FromResult(true);
}

public abstract class BehaviourScenario
{
    public virtual string Name => GetType().Name;
    public virtual BehaviourPhase? Given(BehaviourContext context) => BehaviourPhase.On;
    public virtual Task<BehaviourPhase?> GivenAsync(BehaviourContext context) => Task.FromResult<BehaviourPhase?>(BehaviourPhase.On);
    public virtual bool When(BehaviourContext context) => true;
    public virtual Task<bool> WhenAsync(BehaviourContext context) => Task.FromResult(true);
    public virtual BehaviourResult? Then(BehaviourContext context) => null;
    public virtual Task<BehaviourResult?> ThenAsync(BehaviourContext context) => Task.FromResult<BehaviourResult?>(null);
}

public abstract class BehaviourScenario<TInput> : BehaviourScenario
{
    public override async Task<BehaviourPhase?> GivenAsync(BehaviourContext context) => context.Input is TInput input
        ? await GivenAsync(context, input)
        : BehaviourPhase.None;

    public virtual Task<BehaviourPhase?> GivenAsync(BehaviourContext context, TInput input) => Task.FromResult<BehaviourPhase?>(BehaviourPhase.On);

    public override bool When(BehaviourContext context) => context.Input is TInput input
        && When(context, input);

    public virtual bool When(BehaviourContext context, TInput input) => true;

    public override async Task<bool> WhenAsync(BehaviourContext context) => context.Input is TInput input
        && await WhenAsync(context, input);

    public virtual Task<bool> WhenAsync(BehaviourContext context, TInput input) => Task.FromResult(true);

    public override BehaviourResult? Then(BehaviourContext context) => context.Input is TInput input
        ? Then(context, input)
        : null;

    public virtual BehaviourResult? Then(BehaviourContext context, TInput input) => null;

    public override Task<BehaviourResult?> ThenAsync(BehaviourContext context) => context.Input is TInput input
        ? ThenAsync(context, input)
        : Task.FromResult<BehaviourResult?>(null);

    public virtual Task<BehaviourResult?> ThenAsync(BehaviourContext context, TInput input)
        => Task.FromResult<BehaviourResult?>(null);
}

public partial class BehaviourRunner
{
    public async Task<BehaviourResult?> ExecuteAsync(BehaviourContext context, List<BehaviourFeature> features)
    {
        var loggerState = new Dictionary<string, object?>
        {
            { nameof(context.CorrelationId), context.CorrelationId },
            { nameof(context.Operation), context.Operation },
            { nameof(context.Resource), context.Resource }
        };

        using var scope = context.Logger.BeginScope(loggerState);

        var scenarios = new List<(BehaviourPhase?, BehaviourScenario)>();

        foreach (var feature in features)
        {
            if (feature.Given(context) && await feature.GivenAsync(context))
            {
                foreach (var scenario in feature.Scenarios)
                {
                    var phase = scenario.Given(context) ?? await scenario.GivenAsync(context);

                    if (phase is not null && phase != BehaviourPhase.None)
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

        await ExecuteScenariosAsync(context, scenarios, BehaviourPhase.Initialize);
        await ExecuteScenariosAsync(context, scenarios, BehaviourPhase.Before);
        await ExecuteScenariosAsync(context, scenarios, BehaviourPhase.On);
        await ExecuteScenariosAsync(context, scenarios, BehaviourPhase.After);

        return default;
    }

    private static async Task ExecuteScenariosAsync(BehaviourContext context, List<(BehaviourPhase?, BehaviourScenario)> scenariosPhases, BehaviourPhase phase)
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
                { nameof(BehaviourPhase), phase }
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
