namespace Behavior.Tests;

public class BehaviorRunnerTests
{
    [Fact]
    public async Task BehaviorRunner_SingleScenario_ExecuteSingle()
    {
        var context = new BehaviorContext(NullLogger.Instance);
        var feature = Substitute.For<BehaviorFeature>();
        var scenario = Substitute.For<BehaviorScenario>();

        feature.Given(context).Returns(true);
        feature.GivenAsync(context).Returns(true);
        feature.Scenarios.Returns([scenario]);

        scenario.Given(context).Returns(BehaviorPhase.OnRun);
        scenario.When(context).Returns(true);

        var result = await new BehaviorRunner().ExecuteAsync(context, [feature]);

        scenario.Received(1).Then(context);
    }

    [Fact]
    public async Task BehaviorRunner_MultipleScenario_ExecuteAll()
    {
        var context = new BehaviorContext(NullLogger.Instance);
        var feature = Substitute.For<BehaviorFeature>();
        var scenario1 = Substitute.For<BehaviorScenario>();
        var scenario2 = Substitute.For<BehaviorScenario>();
        var scenario3 = Substitute.For<BehaviorScenario>();
        var scenario4 = Substitute.For<BehaviorScenario>();
        var scenario5 = Substitute.For<BehaviorScenario>();
        var scenario6 = Substitute.For<BehaviorScenario>();

        feature.Given(context).Returns(true);
        feature.GivenAsync(context).Returns(true);
        feature.Scenarios.Returns([scenario1, scenario2, scenario3, scenario4, scenario5, scenario6]);

        scenario1.Given(context).Returns(BehaviorPhase.None);
        scenario1.When(context).Returns(true);

        scenario2.Given(context).Returns(BehaviorPhase.BeforeRun);
        scenario2.When(context).Returns(true);

        scenario3.Given(context).Returns(BehaviorPhase.OnRun);
        scenario3.When(context).Returns(true);

        scenario4.Given(context).Returns(BehaviorPhase.OnRun);
        scenario4.When(context).Returns(true);

        scenario5.Given(context).Returns(BehaviorPhase.OnRun);
        scenario5.When(context).Returns(false);

        scenario6.Given(context).Returns(BehaviorPhase.AfterRun);
        scenario6.When(context).Returns(true);

        var result = await new BehaviorRunner().ExecuteAsync(context, [feature]);

        scenario1.DidNotReceive().Then(context);
        scenario2.Received(1).Then(context);
        scenario3.Received(1).Then(context);
        scenario4.Received(1).Then(context);
        scenario5.DidNotReceive().Then(context);
        scenario6.Received(1).Then(context);
    }

    [Fact]
    public async Task BehaviorRunner_WhenFalse_SkipScenario()
    {
        var context = new BehaviorContext(NullLogger.Instance);
        var feature = Substitute.For<BehaviorFeature>();
        var scenario1 = Substitute.For<BehaviorScenario>();
        var scenario2 = Substitute.For<BehaviorScenario>();
        var scenario3 = Substitute.For<BehaviorScenario>();

        feature.Given(context).Returns(true);
        feature.GivenAsync(context).Returns(true);
        feature.Scenarios.Returns([scenario1, scenario2, scenario3]);

        scenario1.Given(context).Returns(BehaviorPhase.OnRun);
        scenario1.When(context).Returns(true);

        scenario2.Given(context).Returns(BehaviorPhase.OnRun);
        scenario2.When(context).Returns(false);

        scenario3.Given(context).Returns(BehaviorPhase.OnRun);
        scenario3.When(context).Returns(true);

        var result = await new BehaviorRunner().ExecuteAsync(context, [feature]);

        scenario1.Received(1).Then(context);
        scenario2.DidNotReceive().Then(context);
        scenario3.Received(1).Then(context);
    }

    [Fact]
    public async Task BehaviorRunner_ThrowException_Exit()
    {
        var context = new BehaviorContext(NullLogger.Instance);
        var feature = Substitute.For<BehaviorFeature>();
        var scenario1 = Substitute.For<BehaviorScenario>();
        var scenario2 = Substitute.For<BehaviorScenario>();
        var scenario3 = Substitute.For<BehaviorScenario>();

        feature.Given(context).Returns(true);
        feature.GivenAsync(context).Returns(true);
        feature.Scenarios.Returns([scenario1, scenario2, scenario3]);

        scenario1.Given(context).Returns(BehaviorPhase.OnRun);
        scenario1.When(context).Returns(true);

        scenario2.Given(context).Returns(BehaviorPhase.OnRun);
        scenario2.When(context).Returns(true);
        scenario2.Then(context).Throws<InvalidOperationException>();

        scenario3.Given(context).Returns(BehaviorPhase.OnRun);
        scenario3.When(context).Returns(true);

        var result = await new BehaviorRunner().ExecuteAsync(context, [feature]);

        scenario1.Received(1).Then(context);
        scenario2.Received(1).Then(context);
        scenario3.DidNotReceive().Then(context);
    }

    [Fact]
    public async Task BehaviorRunner_NotContinue_SkipScenario()
    {
        var context = new BehaviorContext(NullLogger.Instance);
        var feature = Substitute.For<BehaviorFeature>();
        var scenario1 = Substitute.For<BehaviorScenario>();
        var scenario2 = Substitute.For<BehaviorScenario>();
        var scenario3 = Substitute.For<BehaviorScenario>();

        feature.Given(context).Returns(true);
        feature.GivenAsync(context).Returns(true);
        feature.Scenarios.Returns([scenario1, scenario2, scenario3]);

        scenario1.Given(context).Returns(BehaviorPhase.OnRun);
        scenario1.When(context).Returns(true);

        scenario2.Given(context).Returns(BehaviorPhase.OnRun);
        scenario2.When(context).Returns(true);
        scenario2.Then(context).Returns(new BehaviorResult { IsComplete = true });

        scenario3.Given(context).Returns(BehaviorPhase.OnRun);
        scenario3.When(context).Returns(true);

        var result = await new BehaviorRunner().ExecuteAsync(context, [feature]);

        scenario1.Received(1).Then(context);
        scenario2.Received(1).Then(context);
        scenario3.DidNotReceive().Then(context);
    }

    [Fact]
    public async Task BehaviorRunner_MultipleFeature_ExecuteAll()
    {
        var context = new BehaviorContext(NullLogger.Instance);
        var feature1 = Substitute.For<BehaviorFeature>();
        var feature2 = Substitute.For<BehaviorFeature>();
        var feature3 = Substitute.For<BehaviorFeature>();
        var scenario1 = Substitute.For<BehaviorScenario>();
        var scenario2 = Substitute.For<BehaviorScenario>();
        var scenario3 = Substitute.For<BehaviorScenario>();

        feature1.Given(context).Returns(false);
        feature1.GivenAsync(context).Returns(true);
        feature1.Scenarios.Returns([scenario1]);

        feature2.Given(context).Returns(true);
        feature2.GivenAsync(context).Returns(true);
        feature2.Scenarios.Returns([scenario2]);

        feature3.Given(context).Returns(true);
        feature3.GivenAsync(context).Returns(true);
        feature3.Scenarios.Returns([scenario3]);

        scenario1.Given(context).Returns(BehaviorPhase.OnRun);
        scenario1.When(context).Returns(true);

        scenario2.Given(context).Returns(BehaviorPhase.OnRun);
        scenario2.When(context).Returns(true);

        scenario3.Given(context).Returns(BehaviorPhase.OnRun);
        scenario3.When(context).Returns(true);

        var result = await new BehaviorRunner().ExecuteAsync(context, [feature1, feature2, feature3]);

        scenario1.DidNotReceive().Then(context);
        scenario2.Received(1).Then(context);
        scenario3.Received(1).Then(context);
    }

    [Fact]
    public async Task BehaviorRunner_LogScenario_BeginAndEnd()
    {
        var logger = Substitute.For<MockLogger>();
        var context = new BehaviorContext(logger);
        var feature = Substitute.For<BehaviorFeature>();
        var scenario1 = Substitute.For<BehaviorScenario>();
        var scenario2 = Substitute.For<BehaviorScenario>();

        feature.Given(context).Returns(true);
        feature.GivenAsync(context).Returns(true);
        feature.Scenarios.Returns([scenario1, scenario2]);

        scenario1.Name.Returns(nameof(scenario1));
        scenario1.Given(context).Returns(BehaviorPhase.OnRun);
        scenario1.When(context).Returns(true);

        scenario2.Name.Returns(nameof(scenario2));
        scenario2.Given(context).Returns(BehaviorPhase.OnRun);
        scenario2.When(context).Returns(true);

        var result = await new BehaviorRunner().ExecuteAsync(context, [feature]);

        logger.Received(1).Log(LogLevel.Information, $"Scenario '{nameof(scenario1)}' Begin");
        logger.Received(1).Log(LogLevel.Information, $"Scenario '{nameof(scenario1)}' End");
        logger.Received(1).Log(LogLevel.Information, $"Scenario '{nameof(scenario2)}' Begin");
        logger.Received(1).Log(LogLevel.Information, $"Scenario '{nameof(scenario2)}' End");
    }
}

public abstract class MockLogger : ILogger
{
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Log(logLevel, formatter(state, exception));

    public abstract void Log(LogLevel logLevel, string message);

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
