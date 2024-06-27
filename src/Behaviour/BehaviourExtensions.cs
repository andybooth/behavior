namespace Behaviour;

public static class BehaviourExtensions
{
    public static Task<BehaviourResult> Next(this BehaviourContext context, int? code = null, string? message = null, object? output = null)
    {
        context.Result = Build(context.Result, false, code, message, output);
        return Task.FromResult(context.Result);
    }

    public static Task<BehaviourResult> Complete(this BehaviourContext context, int? code = null, string? message = null, object? output = null)
    {
        context.Result = Build(context.Result, true, code, message, output);
        return Task.FromResult(context.Result);
    }

    private static BehaviourResult Build(BehaviourResult? previousResult, bool isComplete, int? code = null, string? message = null, object? output = null)
    {
        var result = new BehaviourResult
        {
            IsComplete = isComplete,
            Code = code ?? previousResult?.Code,
            Messages = previousResult?.Messages ?? [],
            Output = output ?? previousResult?.Output
        };

        if (message is not null)
        {
            result.Messages.Add(message);
        }

        return result;
    }
}
