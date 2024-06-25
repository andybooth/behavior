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
    public Dictionary<string, object> State { get; } = [];
    public List<object> Events { get; } = [];
    public BehaviourResult? Result { get; set; }

    public Task<BehaviourResult> Continue(int code = 0, object? output = null)
    {
        Result = new BehaviourResult { Continue = true, Code = code, Output = output };
        return Task.FromResult(Result);
    }

    public Task<BehaviourResult> NotContinue(int code = -1, object? output = null)
    {
        Result = new BehaviourResult { Continue = false, Code = code, Output = output };
        return Task.FromResult(Result);
    }

    public void SetState<TState>(TState item)
    {
        ArgumentNullException.ThrowIfNull(item);
        State.Add(nameof(TState), item);
    }

    public TState GetState<TState>() where TState : class
        => State.GetValueOrDefault(nameof(TState)) as TState ?? throw new KeyNotFoundException(nameof(TState));
}

public class BehaviourResult
{
    public bool Continue { get; init; } = true;
    public int Code { get; init; } = 0;
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
    public virtual Task<BehaviourResult> ThenAsync(BehaviourContext context) => context.Continue();
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
        : context.NotContinue();

    public abstract Task<BehaviourResult> ThenAsync(BehaviourContext context, TInput input);
}

public partial class BehaviourRunner
{
    public static async Task<BehaviourResult> ExecuteAsync(BehaviourContext context, List<BehaviourFeature> features, Func<string, bool>? featureFlags = null, Func<List<object>, Task>? sender = null)
    {
        var scenarios = features
            .Where(f => featureFlags is null || featureFlags(f.FeatureName) is true)
            .Where(f => f.Given(context))
            .SelectMany(f => f.Scenarios)
            .Select(s => (Phase: s.Given(context), Scenario: s))
            .Where(s => s.Phase is not BehaviourPhase.None)
            .ToLookup(s => s.Phase, s => s.Scenario);

        if (scenarios.Count == 0)
        {
            return await context.NotContinue();
        }

        var loggerState = new Dictionary<string, object?>
        {
            { nameof(context.CorrelationId), context.CorrelationId },
            { nameof(context.Operation), context.Operation },
            { nameof(context.Resource), context.Resource }
        };

        using var scope = context.Logger.BeginScope(loggerState);

        await ExecuteScenariosAsync(context, scenarios, BehaviourPhase.Initialize);
        await ExecuteScenariosAsync(context, scenarios, BehaviourPhase.Before);
        await ExecuteScenariosAsync(context, scenarios, BehaviourPhase.On);
        await ExecuteScenariosAsync(context, scenarios, BehaviourPhase.After);

        if (sender is not null && context.Events.Count > 0)
        {
            LogSendEventsBegin(context.Logger);

            await sender(context.Events);

            LogSendEventsEnd(context.Logger, context.Events.Count);
        }

        return await context.Continue();
    }

    private static async Task ExecuteScenariosAsync(BehaviourContext context, ILookup<BehaviourPhase, BehaviourScenario> scenarios, BehaviourPhase phase)
    {
        if (!scenarios.Contains(phase))
        {
            return;
        }

        if (context.Result?.Continue == false)
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
                { nameof(BehaviourPhase), phase },
                { nameof(scenario.ScenarioName), scenario.ScenarioName }
            };

            using var scope = context.Logger.BeginScope(loggerState);

            try
            {
                LogScenarioBegin(context.Logger);

                context.Result = await scenario.ThenAsync(context);

                LogScenarioEnd(context.Logger, context.Result?.Code);
            }
            catch (Exception ex)
            {
                LogScenarioError(context.Logger, ex);

                await context.NotContinue();
            }

            if (context.Result?.Continue == false)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scenario Begin")]
    private static partial void LogScenarioBegin(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scenario End {Code}")]
    private static partial void LogScenarioEnd(ILogger logger, int? code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scenario Error")]
    private static partial void LogScenarioError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Send Events Begin")]
    private static partial void LogSendEventsBegin(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Send Events End {Count}")]
    private static partial void LogSendEventsEnd(ILogger logger, int count);
}
