using Amazon.SQS;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Sqs.Configuration;
using Trax.Scheduler.Sqs.Extensions;
using Trax.Scheduler.Sqs.Services;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

internal interface ITestSqsTrain;
