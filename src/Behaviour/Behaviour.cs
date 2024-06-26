using Microsoft.Extensions.Logging;
using System.Security.Principal;

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
    public List<object> Events { get; } = [];
    public BehaviourResult? Result { get; set; }

    public Task<BehaviourResult> Next(int? code = null, string? message = null, object? output = null)
    {
        Result = BehaviourResult.Build(Result, false, code, message, output);
        return Task.FromResult(Result);
    }

    public Task<BehaviourResult> Complete(int? code = null, string? message = null, object? output = null)
    {
        Result = BehaviourResult.Build(Result, true, code, message, output);
        return Task.FromResult(Result);
    }

    public void SetState<TState>(TState? item)
        => State.Add(nameof(TState), item);

    public TState GetState<TState>() where TState : class
        => State.GetValueOrDefault(nameof(TState)) as TState ?? throw new KeyNotFoundException(nameof(TState));
}

public class BehaviourResult
{
    public bool IsComplete { get; init; } = false;
    public int? Code { get; init; }
    public List<string> Messages { get; init; } = [];
    public object? Output { get; init; }

    public static BehaviourResult Build(BehaviourResult? existing, bool isComplete, int? code = null, string? message = null, object? output = null)
    {
        var result = new BehaviourResult
        {
            IsComplete = isComplete,
            Code = code ?? existing?.Code,
            Messages = existing?.Messages ?? [],
            Output = output ?? existing?.Output
        };

        if (message is not null)
        {
            result.Messages.Add(message);
        }

        return result;
    }
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
    public virtual string FeatureName => GetType().Name;
    public virtual bool Given(BehaviourContext context) => true;
    public virtual List<BehaviourScenario> Scenarios { get; } = [];
}

public abstract class BehaviourFeature<TInput> : BehaviourFeature
{
    public override bool Given(BehaviourContext context) => context.Input is TInput input
        && Given(context, input);

    public virtual bool Given(BehaviourContext context, TInput input) => true;
}

public abstract class BehaviourScenario
{
    public virtual string ScenarioName => GetType().Name;
    public virtual BehaviourPhase Given(BehaviourContext context) => BehaviourPhase.On;
    public virtual bool When(BehaviourContext context) => true;
    public virtual Task<BehaviourResult> ThenAsync(BehaviourContext context) => context.Next();
}

public abstract class BehaviourScenario<TInput> : BehaviourScenario
{
    public override BehaviourPhase Given(BehaviourContext context) => context.Input is TInput input
        ? Given(context, input)
        : BehaviourPhase.None;

    public virtual BehaviourPhase Given(BehaviourContext context, TInput input) => BehaviourPhase.On;

    public override bool When(BehaviourContext context) => context.Input is TInput input
        && When(context, input);

    public virtual bool When(BehaviourContext context, TInput input) => true;

    public override Task<BehaviourResult> ThenAsync(BehaviourContext context) => context.Input is TInput input
        ? ThenAsync(context, input)
        : context.Complete();

    public abstract Task<BehaviourResult> ThenAsync(BehaviourContext context, TInput input);
}

public partial class BehaviourRunner
{
    public static async Task<BehaviourResult> ExecuteAsync(BehaviourContext context, List<BehaviourFeature> features, Func<string, bool>? featureFlags = null, Func<List<object>, Task>? sender = null)
    {
        var loggerState = new Dictionary<string, object?>
        {
            { nameof(context.CorrelationId), context.CorrelationId },
            { nameof(context.Operation), context.Operation },
            { nameof(context.Resource), context.Resource }
        };

        using var scope = context.Logger.BeginScope(loggerState);

        var scenarios = features
            .Where(f => featureFlags is null || featureFlags(f.FeatureName) is true)
            .Where(f => f.Given(context))
            .SelectMany(f => f.Scenarios)
            .Select(s => (Phase: s.Given(context), Scenario: s))
            .Where(s => s.Phase is not BehaviourPhase.None)
            .ToLookup(s => s.Phase, s => s.Scenario);

        if (scenarios.Count == 0)
        {
            return await context.Complete();
        }

        await ExecuteScenariosAsync(context, scenarios, BehaviourPhase.Initialize);
        await ExecuteScenariosAsync(context, scenarios, BehaviourPhase.Before);
        await ExecuteScenariosAsync(context, scenarios, BehaviourPhase.On);
        await ExecuteScenariosAsync(context, scenarios, BehaviourPhase.After);

        if (sender is not null && context.Events.Count > 0)
        {
            await sender(context.Events);
        }

        return await context.Next();
    }

    private static async Task ExecuteScenariosAsync(BehaviourContext context, ILookup<BehaviourPhase, BehaviourScenario> scenarios, BehaviourPhase phase)
    {
        if (!scenarios.Contains(phase))
        {
            return;
        }

        if (context.Result?.IsComplete is true)
        {
            return;
        }

        foreach (var scenario in scenarios[phase])
        {
            if (!scenario.When(context))
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
                LogScenarioBegin(context.Logger, scenario.ScenarioName);

                context.Result = await scenario.ThenAsync(context);

                LogScenarioEnd(context.Logger, scenario.ScenarioName);
            }
            catch (Exception ex)
            {
                LogScenarioError(context.Logger, ex, scenario.ScenarioName);

                context.Result = await context.Complete();
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
