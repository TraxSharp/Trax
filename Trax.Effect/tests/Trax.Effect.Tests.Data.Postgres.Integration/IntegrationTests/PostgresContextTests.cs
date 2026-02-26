using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Services.WorkflowBus;
using Trax.Effect.Services.EffectStep;
using Trax.Effect.Services.ServiceTrain;
using Trax.Core.Step;
using Trax.Effect.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Metadata = Trax.Effect.Models.Metadata.Metadata;

namespace Trax.Effect.Tests.Data.Postgres.Integration.IntegrationTests;

public class PostgresContextTests : TestSetup
{
    [Theory]
    public async Task TestPostgresProviderCanCreateMetadata()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "TestMetadata",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N")
            }
        );

        await context.Track(metadata);

        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Act
        var foundMetadata = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);

        // Assert
        foundMetadata.Should().NotBeNull();
        foundMetadata.Id.Should().Be(metadata.Id);
        foundMetadata.Name.Should().Be(metadata.Name);
    }

    [Theory]
    public async Task TestPostgresProviderCanRunWorkflow()
    {
        // Arrange
        // Act
        var workflow = await WorkflowBus.RunAsync<TestWorkflow>(new TestWorkflowInput());

        // Assert
        workflow.Metadata.Name.Should().Be(typeof(TestWorkflow).FullName);
        workflow.Metadata.FailureException.Should().BeNullOrEmpty();
        workflow.Metadata.FailureReason.Should().BeNullOrEmpty();
        workflow.Metadata.FailureStep.Should().BeNullOrEmpty();
        workflow.Metadata.WorkflowState.Should().Be(WorkflowState.Completed);
    }

    [Theory]
    public async Task TestPostgresProviderCanRunWorkflowTwo()
    {
        // Arrange
        // Act
        var workflow = await WorkflowBus.RunAsync<TestWorkflow>(new TestWorkflowInput());
        var workflowTwo = await WorkflowBus.RunAsync<TestWorkflowWithoutInterface>(
            new TestWorkflowWithoutInterfaceInput()
        );

        // Assert
        workflow.Metadata.Name.Should().Be(typeof(TestWorkflow).FullName);
        workflow.Metadata.FailureException.Should().BeNullOrEmpty();
        workflow.Metadata.FailureReason.Should().BeNullOrEmpty();
        workflow.Metadata.FailureStep.Should().BeNullOrEmpty();
        workflow.Metadata.WorkflowState.Should().Be(WorkflowState.Completed);
    }

    [Theory]
    public async Task TestPostgresProviderCanRunWorkflowWithinWorkflow()
    {
        // Arrange
        var dataContextProvider =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var arrayLoggerProvider = Scope.ServiceProvider.GetRequiredService<IArrayLoggingProvider>();

        // Act
        var (innerWorkflow, workflow) = await WorkflowBus.RunAsync<(
            ITestWorkflow,
            ITestWorkflowWithinWorkflow
        )>(new TestWorkflowWithinWorkflowInput());

        // Assert
        workflow.Metadata.Name.Should().Be(typeof(TestWorkflowWithinWorkflow).FullName);
        workflow.Metadata.FailureException.Should().BeNullOrEmpty();
        workflow.Metadata.FailureReason.Should().BeNullOrEmpty();
        workflow.Metadata.FailureStep.Should().BeNullOrEmpty();
        workflow.Metadata.WorkflowState.Should().Be(WorkflowState.Completed);
        innerWorkflow.Metadata.Name.Should().Be(typeof(TestWorkflow).FullName);
        innerWorkflow.Metadata.FailureException.Should().BeNullOrEmpty();
        innerWorkflow.Metadata.FailureReason.Should().BeNullOrEmpty();
        innerWorkflow.Metadata.FailureStep.Should().BeNullOrEmpty();
        innerWorkflow.Metadata.WorkflowState.Should().Be(WorkflowState.Completed);

        using var dataContext = (IDataContext)dataContextProvider.Create();

        var parentWorkflowResult = await dataContext.Metadatas.FirstOrDefaultAsync(
            x => x.Id == workflow.Metadata.Id
        );
        var childWorkflowResult = await dataContext.Metadatas.FirstOrDefaultAsync(
            x => x.Id == innerWorkflow.Metadata.Id
        );
        parentWorkflowResult.Should().NotBeNull();
        parentWorkflowResult!.Id.Should().Be(workflow.Metadata.Id);
        parentWorkflowResult!.WorkflowState.Should().Be(WorkflowState.Completed);
        parentWorkflowResult.Input.Should().NotBeNull();
        parentWorkflowResult.Output.Should().NotBeNull();

        childWorkflowResult.Should().NotBeNull();
        childWorkflowResult!.Id.Should().Be(innerWorkflow.Metadata.Id);
        childWorkflowResult.WorkflowState.Should().Be(WorkflowState.Completed);
        childWorkflowResult.Input.Should().NotBeNull();
        childWorkflowResult.Output.Should().NotBeNull();

        var logLevel = arrayLoggerProvider
            .Loggers.SelectMany(x => x.Logs)
            .Select(x => x.Level)
            .Count(x => x == LogLevel.Critical);
        logLevel.Should().Be(1);
    }

    internal class TestWorkflow : ServiceTrain<TestWorkflowInput, TestWorkflow>, ITestWorkflow
    {
        protected override async Task<Either<Exception, TestWorkflow>> RunInternal(
            TestWorkflowInput input
        ) => Activate(input, this).Resolve();
    }

    internal class TestWorkflowWithoutInterface
        : ServiceTrain<TestWorkflowWithoutInterfaceInput, TestWorkflowWithoutInterface>
    {
        protected override async Task<Either<Exception, TestWorkflowWithoutInterface>> RunInternal(
            TestWorkflowWithoutInterfaceInput input
        ) => Activate(input, this).Resolve();
    }

    internal record TestWorkflowWithoutInterfaceInput;

    internal record TestWorkflowInput;

    internal class TestWorkflowWithinWorkflow()
        : ServiceTrain<
            TestWorkflowWithinWorkflowInput,
            (ITestWorkflow, ITestWorkflowWithinWorkflow)
        >,
            ITestWorkflowWithinWorkflow
    {
        protected override async Task<
            Either<Exception, (ITestWorkflow, ITestWorkflowWithinWorkflow)>
        > RunInternal(TestWorkflowWithinWorkflowInput input) =>
            Activate(input)
                .AddServices<ITestWorkflowWithinWorkflow>(this)
                .Chain<StepToRunTestWorkflow>()
                .Resolve();
    }

    internal record TestWorkflowWithinWorkflowInput;

    internal class StepToRunTestWorkflow(
        IWorkflowBus workflowBus,
        ILogger<StepToRunTestWorkflow> logger
    ) : EffectStep<Unit, ITestWorkflow>
    {
        public override async Task<ITestWorkflow> Run(Unit input)
        {
            var testWorkflow = await workflowBus.RunAsync<TestWorkflow>(new TestWorkflowInput());

            logger.LogCritical("Ran {WorkflowName}", "TestWorkflow");

            return testWorkflow;
        }
    }

    internal interface ITestWorkflow : IServiceTrain<TestWorkflowInput, TestWorkflow> { }

    internal interface ITestWorkflowWithinWorkflow
        : IServiceTrain<
            TestWorkflowWithinWorkflowInput,
            (ITestWorkflow, ITestWorkflowWithinWorkflow)
        > { }
}
