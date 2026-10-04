using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Train;
using Trax.Effect.Decisions.SystemOne;

namespace Trax.Effect.Tests.Integration.UnitTests.Decisions;

/// <summary>
/// <see cref="SystemOneDecider"/> speaks the System One request format to a typed decision model.
/// These pin the wire in both directions, what it retries, and what it refuses to send or read.
/// </summary>
public class SystemOneDeciderTests
{
    private static readonly DecisionRequest Ticket = new(
        "TriageTicket",
        new { CustomerId = "cus_1", OrderTotal = 42m },
        [
            new ChoiceQuestion(
                "TicketTrack",
                "Which team should handle this ticket?",
                [new("Refund", "Money back for an order."), new("Escalate", null)]
            ),
            new ScoreQuestion(
                "Urgency",
                "How urgent is it?",
                [new("Low", "Whenever."), new("High", "Now.")]
            ),
            new YesNoQuestion(
                "ContainsThreat",
                "Does it threaten anyone?",
                "It threatens harm.",
                null
            ),
        ]
    );

    private const string Answered = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "TicketTrack": { "type": "choice", "choice": "Refund", "confidence": 0.8,
                             "probabilities": { "Refund": 0.87, "Escalate": 0.13 } },
            "Urgency": { "type": "score", "score": 1.04, "confidence": 0.94,
                         "legend": { "0": "Whenever.", "1": "Now." },
                         "probabilities": { "1": 0.96, "0": 0.04 } },
            "ContainsThreat": { "type": "noul", "noul": 0.03 }
          },
          "usage": { "input_tokens": 212, "output_tokens": 0 }
        }
        """;

    private static SystemOneOptions Options(Action<SystemOneOptions>? configure = null)
    {
        var options = new SystemOneOptions
        {
            Endpoint = new Uri("https://api.example.test/v1/systemone"),
            Model = "jev-1.13.0",
            ApiKey = "sk-test",
            RetryDelay = TimeSpan.Zero,
        };
        configure?.Invoke(options);
        return options;
    }

    [Test]
    public async Task Decide_SendsEachQuestionInTheSystemOneShape()
    {
        var model = new FakeModel(_ => Ok(Answered));
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        await decider.Decide(Ticket, CancellationToken.None);

        var sent = model.Requests.Should().ContainSingle().Subject;
        sent.Authorization.Should().Be("Bearer sk-test");
        sent.Body["model"]!.GetValue<string>().Should().Be("jev-1.13.0");
        sent.Body["state"]!["customerId"]!.GetValue<string>().Should().Be("cus_1");

        var questions = sent.Body["questions"]!;
        questions["TicketTrack"]!["type"]!.GetValue<string>().Should().Be("choice");
        questions["TicketTrack"]!["instructions"]!
            .GetValue<string>()
            .Should()
            .Be("Which team should handle this ticket?");
        questions["TicketTrack"]!["criteria"]!
            .ToJsonString()
            .Should()
            .Be("""{"Refund":"Money back for an order.","Escalate":"Escalate"}""");
        questions["Urgency"]!["type"]!.GetValue<string>().Should().Be("score");
        questions["Urgency"]!["criteria"]!.ToJsonString().Should().Be("""["Whenever.","Now."]""");
        questions["ContainsThreat"]!["type"]!.GetValue<string>().Should().Be("noul");
        questions["ContainsThreat"]!["criteria"]!
            .ToJsonString()
            .Should()
            .Be("""{"true":"It threatens harm.","false":"No"}""");
    }

    [Test]
    public async Task Decide_ReadsEachAnswerAndTheModelThatGaveIt()
    {
        using var decider = new SystemOneDecider(
            Options(),
            new HttpClient(new FakeModel(_ => Ok(Answered)))
        );

        var result = await decider.Decide(Ticket, CancellationToken.None);

        result
            .Answers["TicketTrack"]
            .Should()
            .BeEquivalentTo(
                new ChoiceAnswer(
                    "Refund",
                    0.8,
                    new Dictionary<string, double> { ["Refund"] = 0.87, ["Escalate"] = 0.13 }
                )
                {
                    Model = "jev-1.13.0",
                }
            );
        var urgency = result.Answers["Urgency"].Should().BeOfType<ScoreAnswer>().Subject;
        urgency.Score.Should().Be(1.04);
        urgency.Probabilities.Should().Equal(0.04, 0.96);
        result
            .Answers["ContainsThreat"]
            .Should()
            .Be(new YesNoAnswer(0.03) { Model = "jev-1.13.0" });
    }

    [Test]
    public async Task Decide_LeavesOutAnAnswerItCannotRead()
    {
        // The train then fails on an unanswered question rather than acting on a guess.
        using var decider = new SystemOneDecider(
            Options(),
            new HttpClient(
                new FakeModel(_ =>
                    Ok(
                        """{"model":"jev-1.13.0","answers":{"TicketTrack":{"type":"choice","confidence":0.9}}}"""
                    )
                )
            )
        );

        var result = await decider.Decide(Ticket, CancellationToken.None);

        result.Answers.Should().BeEmpty();
    }

    [Test]
    public async Task Decide_RetriesAThrottledOrUnavailableModel()
    {
        var model = new FakeModel(attempt =>
            attempt switch
            {
                1 => Status(HttpStatusCode.TooManyRequests),
                2 => Status(HttpStatusCode.ServiceUnavailable),
                _ => Ok(Answered),
            }
        );
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var result = await decider.Decide(Ticket, CancellationToken.None);

        result.Answers.Should().HaveCount(3);
        model.Requests.Should().HaveCount(3);
    }

    [Test]
    public async Task Decide_WhenRetriesRunOut_FailsTransiently()
    {
        var model = new FakeModel(_ => Status(HttpStatusCode.BadGateway));
        using var decider = new SystemOneDecider(
            Options(o => o.MaxAttempts = 2),
            new HttpClient(model)
        );

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("502").And.Contain("after 2 attempts");
        ((TrainExceptionData)failure.Data["TrainExceptionData"]!)
            .FailureClass.Should()
            .Be(FailureClass.Transient);
        model.Requests.Should().HaveCount(2);
    }

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.UnprocessableEntity)]
    public async Task Decide_ARefusedRequest_FailsPermanentlyWithoutRetrying(HttpStatusCode status)
    {
        var model = new FakeModel(_ => Status(status, """{"error":"bad criteria"}"""));
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain(((int)status).ToString());
        ((TrainExceptionData)failure.Data["TrainExceptionData"]!)
            .FailureClass.Should()
            .Be(FailureClass.Permanent);
        model.Requests.Should().ContainSingle();
    }

    [TestCase(HttpRequestError.NameResolutionError)]
    [TestCase(HttpRequestError.SecureConnectionError)]
    public async Task Decide_AnEndpointThatCannotBeReachedAsConfigured_FailsPermanentlyWithoutRetrying(
        HttpRequestError error
    )
    {
        var model = new FakeModel(_ => throw new HttpRequestException(error, "cannot connect"));
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("cannot be reached as configured");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
        model.Requests.Should().ContainSingle();
    }

    [Test]
    public async Task Decide_ARefusedConnection_IsRetriedThenFailsTransiently()
    {
        var model = new FakeModel(_ =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")
        );
        using var decider = new SystemOneDecider(
            Options(o => o.MaxAttempts = 2),
            new HttpClient(model)
        );

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("could not be reached");
        ClassOf(failure).Should().Be(FailureClass.Transient);
        model.Requests.Should().HaveCount(2);
    }

    [Test]
    public async Task Decide_ASlowAttempt_IsAbandonedAndRetried()
    {
        var model = new FakeModel(attempt => Ok(Answered), hang: attempt => attempt == 1);
        using var decider = new SystemOneDecider(
            Options(o => o.AttemptTimeout = TimeSpan.FromMilliseconds(50)),
            new HttpClient(model)
        );

        var result = await decider.Decide(Ticket, CancellationToken.None);

        result.Answers.Should().HaveCount(3);
        model.Requests.Should().HaveCount(2);
    }

    [Test]
    public async Task Decide_CancellingTheTrain_CancelsTheRequest()
    {
        using var cts = new CancellationTokenSource();
        var model = new FakeModel(_ => Ok(Answered), hang: _ => true, onSend: cts.Cancel);
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var decide = () => decider.Decide(Ticket, cts.Token);

        await decide.Should().ThrowAsync<OperationCanceledException>();
        model.Requests.Should().ContainSingle("a cancelled train is not retried");
    }

    [Test]
    public async Task Decide_RefusesMoreOptionsThanTheModelAccepts()
    {
        using var decider = new SystemOneDecider(
            Options(o => o.MaxOptions = 2),
            new HttpClient(new FakeModel(_ => Ok(Answered)))
        );
        var tooMany = Ticket with
        {
            Questions =
            [
                new ChoiceQuestion(
                    "Lane",
                    "Which lane?",
                    [new("A", null), new("B", null), new("C", null)]
                ),
            ],
        };

        var decide = () => decider.Decide(tooMany, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("offers 3 options");
        ((TrainExceptionData)failure.Data["TrainExceptionData"]!)
            .FailureClass.Should()
            .Be(FailureClass.Permanent);
    }

    [Test]
    public async Task Decide_AChoiceOfOneOption_IsRefusedBeforeItIsSent()
    {
        // A Switch with a single track offers one option, which Nimble and OpenJev refuse.
        var model = new FakeModel(_ => Ok(Answered));
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));
        var single = Ticket with
        {
            Questions = [new ChoiceQuestion("Lane", "Which lane?", [new("A", null)])],
        };

        var decide = () => decider.Decide(single, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("offers 1 option").And.Contain("at least 2");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
        model.Requests.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task Decide_AQuestionWithoutInstructions_IsRefusedBeforeItIsSent(
        string instructions
    )
    {
        var model = new FakeModel(_ => Ok(Answered));
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));
        var blank = Ticket with { Questions = [new YesNoQuestion("Q", instructions, null, null)] };

        var decide = () => decider.Decide(blank, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("'Q' has no instructions");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
        model.Requests.Should().BeEmpty();
    }

    [TestCase(42, "number")]
    [TestCase(true, "true")]
    [TestCase(DayOfWeek.Monday, "number")]
    public async Task Decide_AStateTheFormatDoesNotTake_IsRefusedBeforeItIsSent(
        object state,
        string written
    )
    {
        var model = new FakeModel(_ => Ok(Answered));
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var decide = () => decider.Decide(Ticket with { State = state }, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure
            .Message.Should()
            .Contain($"written as JSON {written}")
            .And.Contain("a string, an object or an array");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
        model.Requests.Should().BeEmpty();
    }

    [TestCase("a plain string")]
    public async Task Decide_AStringStateIsSentAsIs(string state)
    {
        var model = new FakeModel(_ => Ok(Answered));
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        await decider.Decide(Ticket with { State = state }, CancellationToken.None);

        model.Requests.Should().ContainSingle().Which.Body["state"]!
            .GetValue<string>()
            .Should()
            .Be(state);
    }

    [Test]
    public async Task Decide_RefusesMoreQuestionsThanOneRequestMayCarry()
    {
        var model = new FakeModel(_ => Ok(Answered));
        using var decider = new SystemOneDecider(
            Options(o => o.MaxQuestions = 2),
            new HttpClient(model)
        );

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("asks 3 questions").And.Contain("at most 2");
        ((TrainExceptionData)failure.Data["TrainExceptionData"]!)
            .FailureClass.Should()
            .Be(FailureClass.Permanent);
        model.Requests.Should().BeEmpty("it is refused before it is sent");
    }

    [Test]
    public async Task Decide_KeepsNoMoreRequestsInFlightThanAllowed()
    {
        var gate = new TaskCompletionSource();
        var model = new FakeModel(_ => Ok(Answered), waitFor: () => gate.Task);
        using var decider = new SystemOneDecider(
            Options(o => o.MaxConcurrentRequests = 2),
            new HttpClient(model)
        );

        var decisions = Enumerable
            .Range(0, 5)
            .Select(_ => decider.Decide(Ticket, CancellationToken.None))
            .ToList();
        await model.InFlightReached(2);
        gate.SetResult();
        await Task.WhenAll(decisions);

        model.MostInFlight.Should().Be(2);
        model.Requests.Should().HaveCount(5);
    }

    [Test]
    public async Task Disposing_WhileADecisionIsInFlight_LeavesItsOutcomeAlone()
    {
        var gate = new TaskCompletionSource();
        var model = new FakeModel(_ => Ok(Answered), waitFor: () => gate.Task);
        var decider = new SystemOneDecider(
            Options(o => o.MaxConcurrentRequests = 1),
            new HttpClient(model)
        );

        var decision = decider.Decide(Ticket, CancellationToken.None);
        await model.InFlightReached(1);
        decider.Dispose();
        gate.SetResult();

        (await decision).Answers.Should().HaveCount(3);
    }

    [Test]
    public async Task Decide_RetriesAModelThatSaysItIsBusy()
    {
        var model = new FakeModel(attempt =>
            attempt == 1 ? Status((HttpStatusCode)529) : Ok(Answered)
        );
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        await decider.Decide(Ticket, CancellationToken.None);

        model.Requests.Should().HaveCount(2);
    }

    [Test]
    public async Task Decide_OutOfCredit_FailsPermanentlyWithTheProvidersRequestId()
    {
        // The provider's error shape, with its request id in the response header.
        var model = new FakeModel(_ =>
        {
            var response = Status(
                HttpStatusCode.PaymentRequired,
                """{"error":{"message":"Insufficient credit"}}"""
            );
            response.Headers.Add("x-typesafe-request-id", "req_9f2");
            return response;
        });
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure
            .Message.Should()
            .Contain("402")
            .And.Contain("cannot pay")
            .And.Contain("(request req_9f2)");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
        model.Requests.Should().ContainSingle();
    }

    [Test]
    public async Task Decide_ARefusalThatEchoesTheRequest_DoesNotRepeatItInTheFailure()
    {
        // FastAPI's validation error carries the input it refused, here the train's state, and
        // the failure message is stored on the run and shown on the dashboard.
        var model = new FakeModel(_ =>
            Status(
                HttpStatusCode.UnprocessableEntity,
                """
                {"detail":[{"type":"missing","loc":["body","questions"],"msg":"Field required",
                            "input":{"state":{"customerId":"cus_secret_7"}}}]}
                """
            )
        );
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("422").And.Contain("refused as invalid");
        failure.Message.Should().NotContain("cus_secret_7").And.NotContain("detail");
        ((TrainExceptionData)failure.Data["TrainExceptionData"]!)
            .Message.Should()
            .NotContain("cus_secret_7");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task Decide_ARequestIdThatIsNotAnId_IsLeftOut()
    {
        var model = new FakeModel(_ =>
        {
            var response = Status(HttpStatusCode.Unauthorized);
            response.Headers.TryAddWithoutValidation("x-typesafe-request-id", "<b>look here</b>");
            return response;
        });
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        (await decide.Should().ThrowAsync<DecisionServiceException>())
            .Which.Message.Should()
            .NotContain("look here");
    }

    [Test]
    public async Task Decide_DoesNotFollowARedirect()
    {
        // Through the decider's own client, against a real socket, so its handler is what is
        // tested: the configured endpoint answers with a redirect to a path that would answer.
        using var server = new RedirectingServer(Answered);
        using var decider = new SystemOneDecider(Options(o => o.Endpoint = server.Endpoint));

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("307").And.Contain("redirects are not followed");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
        server.Followed.Should().Be(0);
    }

    [TestCase(HttpStatusCode.MovedPermanently)]
    [TestCase(HttpStatusCode.TemporaryRedirect)]
    public async Task Decide_ARedirectHandedBackByTheCallersClient_FailsPermanently(
        HttpStatusCode status
    )
    {
        var model = new FakeModel(_ => Status(status));
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("redirects are not followed");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
        model.Requests.Should().ContainSingle();
    }

    [TestCase(null, "jev-1.13.0", "Endpoint is required")]
    [TestCase("http://models.example.test/v1/systemone", "jev-1.13.0", "is not HTTPS")]
    [TestCase("https://api.example.test/v1/systemone", null, "Model is required")]
    [TestCase("https://api.example.test/v1/systemone", "jev-latest", "floating alias")]
    [TestCase("https://api.example.test/v1/systemone", "jev", "floating alias")]
    public void Constructing_WithUnusableOptions_IsRefused(
        string? endpoint,
        string? model,
        string problem
    )
    {
        var construct = () =>
            new SystemOneDecider(
                Options(o =>
                {
                    o.Endpoint = endpoint is null ? null : new Uri(endpoint);
                    o.Model = model;
                })
            );

        construct.Should().Throw<ArgumentException>().WithMessage($"*{problem}*");
    }

    [TestCase("file:///etc/passwd", "not an http or https URL")]
    [TestCase("ftp://localhost/v1/systemone", "not an http or https URL")]
    [TestCase("http://localhost.attacker.example/v1/systemone", "is not HTTPS")]
    [TestCase("http://127.0.0.1.attacker.example/v1/systemone", "is not HTTPS")]
    [TestCase("http://192.168.1.10:8000/v1/systemone", "is not HTTPS")]
    public void Constructing_WithAnEndpointThatIsNotSafeToSendTo_IsRefused(
        string endpoint,
        string problem
    )
    {
        var construct = () => new SystemOneDecider(Options(o => o.Endpoint = new Uri(endpoint)));

        construct.Should().Throw<ArgumentException>().WithMessage($"*{problem}*");
    }

    [TestCase("http://localhost:8080/v1/systemone")]
    [TestCase("http://127.0.0.1:8000/v1/systemone")]
    [TestCase("http://127.8.9.10:8000/v1/systemone")]
    [TestCase("http://[::1]:8000/v1/systemone")]
    public void Constructing_ForAModelOnLoopback_AllowsHttp(string endpoint)
    {
        using var decider = new SystemOneDecider(Options(o => o.Endpoint = new Uri(endpoint)));

        decider.Should().NotBeNull();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task Decide_WithNoKey_SendsNoAuthorization(string? key)
    {
        var model = new FakeModel(_ => Ok(Answered));
        using var decider = new SystemOneDecider(
            Options(o => o.ApiKey = key),
            new HttpClient(model)
        );

        await decider.Decide(Ticket, CancellationToken.None);

        model.Requests.Should().ContainSingle().Which.Authorization.Should().BeNull();
    }

    [Test]
    public async Task Decide_WithoutConfidence_TakesTheProbabilityOfWhatWasChosen()
    {
        using var decider = new SystemOneDecider(
            Options(),
            new HttpClient(
                new FakeModel(_ =>
                    Ok(
                        """
                        {"model":"jev-1.13.0","answers":{
                          "TicketTrack":{"type":"choice","choice":"Refund",
                                         "probabilities":{"Refund":0.87,"Escalate":0.13}},
                          "Urgency":{"type":"score","score":0.7,
                                     "probabilities":{"0":0.3,"1":0.7}}}}
                        """
                    )
                )
            )
        );

        var result = await decider.Decide(Ticket, CancellationToken.None);

        result
            .Answers["TicketTrack"]
            .Should()
            .BeOfType<ChoiceAnswer>()
            .Which.Confidence.Should()
            .Be(0.87);
        result
            .Answers["Urgency"]
            .Should()
            .BeOfType<ScoreAnswer>()
            .Which.Confidence.Should()
            .Be(0.7, "the score is nearest level 1");
    }

    [Test]
    public async Task Decide_WithNeitherConfidenceNorProbabilities_LeavesTheAnswerOut()
    {
        using var decider = new SystemOneDecider(
            Options(),
            new HttpClient(
                new FakeModel(_ =>
                    Ok(
                        """
                        {"model":"jev-1.13.0","answers":{
                          "TicketTrack":{"type":"choice","choice":"Refund"},
                          "Urgency":{"type":"score","score":1.0}}}
                        """
                    )
                )
            )
        );

        var result = await decider.Decide(Ticket, CancellationToken.None);

        result.Answers.Should().BeEmpty();
    }

    [TestCase("""{"1":0.04,"2":0.96}""")]
    [TestCase("""{"0":1.0}""")]
    [TestCase("""{"0":0.5,"1":0.3,"2":0.2}""")]
    [TestCase("""{"0":0.5,"01":0.5}""")]
    public async Task Decide_AScoreWhoseProbabilitiesAreNotEveryLevelFromZero_IsLeftOut(
        string probabilities
    )
    {
        // Sorting "1" and "2" would otherwise shift the answer one level down.
        using var decider = new SystemOneDecider(
            Options(),
            new HttpClient(
                new FakeModel(_ =>
                    Ok(
                        $$"""
                        {"model":"jev-1.13.0","answers":{
                          "Urgency":{"type":"score","score":1.0,"confidence":0.9,
                                     "probabilities":{{probabilities}}
                        } } }
                        """
                    )
                )
            )
        );

        var result = await decider.Decide(Ticket, CancellationToken.None);

        result.Answers.Should().NotContainKey("Urgency");
    }

    [Test]
    public async Task Decide_AnAnswerOfTheWrongType_IsLeftOut()
    {
        using var decider = new SystemOneDecider(
            Options(),
            new HttpClient(
                new FakeModel(_ =>
                    Ok(
                        """{"model":"jev-1.13.0","answers":{"TicketTrack":{"type":"noul","noul":0.9}}}"""
                    )
                )
            )
        );

        var result = await decider.Decide(Ticket, CancellationToken.None);

        result.Answers.Should().BeEmpty();
    }

    [TestCase("<html>Bad gateway</html>")]
    [TestCase("")]
    [TestCase("[1, 2]")]
    [TestCase("""{"model": 7, "answers": {}}""")]
    [TestCase("""{"model": "jev-1.13.0"}""")]
    [TestCase("""{"model": "jev-1.13.0", "answers": null}""")]
    [TestCase("""{"model": "jev-1.13.0", "answers": []}""")]
    public async Task Decide_ASuccessThatIsNotASystemOneResponse_IsRetriedThenFailsTransiently(
        string body
    )
    {
        var model = new FakeModel(_ => Ok(body));
        using var decider = new SystemOneDecider(
            Options(o => o.MaxAttempts = 2),
            new HttpClient(model)
        );

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("not a System One response");
        ClassOf(failure).Should().Be(FailureClass.Transient);
        model.Requests.Should().HaveCount(2);
    }

    [Test]
    public async Task Decide_AStateThatCannotBeWrittenAsJson_FailsPermanentlyWithoutSending()
    {
        var model = new FakeModel(_ => Ok(Answered));
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));
        var loop = new Loop();
        loop.Next = loop;

        var decide = () => decider.Decide(Ticket with { State = loop }, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("state cannot be written as JSON");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
        model.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task Decide_AStateOfATypeJsonCannotHold_FailsPermanentlyWithoutSending()
    {
        var model = new FakeModel(_ => Ok(Answered));
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var decide = () =>
            decider.Decide(
                Ticket with
                {
                    State = new { Kind = typeof(string) },
                },
                CancellationToken.None
            );

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("state cannot be written as JSON");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
        model.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task Decide_AQuestionTheFormatCannotAsk_FailsPermanently()
    {
        var model = new FakeModel(_ => Ok(Answered));
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var decide = () =>
            decider.Decide(
                Ticket with
                {
                    Questions = [new FreeTextQuestion("Why", "Why?")],
                },
                CancellationToken.None
            );

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("'Why' is a FreeTextQuestion");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
        model.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task Decide_AnOptionNamedTwice_IsRefusedForWhatItIs()
    {
        using var decider = new SystemOneDecider(
            Options(),
            new HttpClient(new FakeModel(_ => Ok(Answered)))
        );
        var twice = Ticket with
        {
            Questions =
            [
                new ChoiceQuestion("Lane", "Which lane?", [new("A", "Left"), new("A", "Right")]),
            ],
        };

        var decide = () => decider.Decide(twice, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("offers the option 'A' more than once");
        failure.Message.Should().NotContain("at most");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task Decide_ATrainsQuestionIsSentUnderItsShortKey()
    {
        var model = new FakeModel(_ =>
            Ok(
                """
                {
                  "model": "jev-1.13.0",
                  "answers": {
                    "SystemOneDeciderTests.RefundRoute": { "type": "choice", "choice": "Credit", "confidence": 0.9 },
                    "refund_reason": { "type": "choice", "choice": "Damaged", "confidence": 0.9 }
                  }
                }
                """
            )
        );
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var result = await new RefundTrain(decider).RunEither("order 42");

        result.IsRight.Should().BeTrue();
        model.Requests.Should().ContainSingle().Which.Body["questions"]!
            .AsObject()
            .Select(q => q.Key)
            .Should()
            .Equal(
                ["SystemOneDeciderTests.RefundRoute", "refund_reason"],
                "the namespace is left out, and [Asks(Key = ...)] is sent as it is"
            );
    }

    [Test]
    public async Task Decide_AQuestionKeyUsedTwice_IsRefused()
    {
        using var decider = new SystemOneDecider(
            Options(),
            new HttpClient(new FakeModel(_ => Ok(Answered)))
        );
        var twice = Ticket with
        {
            Questions =
            [
                new YesNoQuestion("Q", "One?", null, null),
                new YesNoQuestion("Q", "Two?", null, null),
            ],
        };

        var decide = () => decider.Decide(twice, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("asks the question 'Q' more than once");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task Decide_WaitsAsLongAsTheModelAsks()
    {
        var model = new FakeModel(attempt =>
            attempt == 1 ? Status(HttpStatusCode.TooManyRequests, retryAfter: 2) : Ok(Answered)
        );
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));
        var waits = Record(decider, jitter: 0);

        await decider.Decide(Ticket, CancellationToken.None);

        waits.Should().Equal(TimeSpan.FromSeconds(2));
        model.Requests.Should().HaveCount(2);
    }

    [Test]
    public async Task Decide_JitterOnlyEverLengthensTheWaitTheModelAsks()
    {
        var model = new FakeModel(attempt =>
            attempt == 1 ? Status(HttpStatusCode.ServiceUnavailable, retryAfter: 2) : Ok(Answered)
        );
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));
        var waits = Record(decider, jitter: 0.5);

        await decider.Decide(Ticket, CancellationToken.None);

        waits.Should().Equal(TimeSpan.FromSeconds(2.1));
    }

    [Test]
    public async Task Decide_AModelAskingForALongerWaitThanAllowed_FailsTransientlyWithoutRetrying()
    {
        var model = new FakeModel(_ => Status((HttpStatusCode)529, retryAfter: 120));
        using var decider = new SystemOneDecider(
            Options(o => o.MaxRetryDelay = TimeSpan.FromSeconds(30)),
            new HttpClient(model)
        );
        var waits = Record(decider, jitter: 0);

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        failure.Message.Should().Contain("retried in 120s").And.Contain("MaxRetryDelay (30s)");
        ClassOf(failure).Should().Be(FailureClass.Transient);
        model.Requests.Should().ContainSingle("it is not retried before the model said to");
        waits.Should().BeEmpty();
    }

    [Test]
    public async Task Decide_BacksOffByDoublingWithJitter()
    {
        var model = new FakeModel(_ => Status(HttpStatusCode.BadGateway));
        using var decider = new SystemOneDecider(
            Options(o =>
            {
                o.RetryDelay = TimeSpan.FromSeconds(1);
                o.MaxAttempts = 4;
            }),
            new HttpClient(model)
        );
        var waits = Record(decider, jitter: 0.5);

        await decider
            .Invoking(d => d.Decide(Ticket, CancellationToken.None))
            .Should()
            .ThrowAsync<DecisionServiceException>();

        // Half of each doubled delay is fixed and half is jitter.
        waits
            .Should()
            .Equal(TimeSpan.FromSeconds(0.75), TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task Decide_ABackoffThatWouldOverflow_StopsAtMaxRetryDelay()
    {
        var model = new FakeModel(_ => Status(HttpStatusCode.ServiceUnavailable));
        using var decider = new SystemOneDecider(
            Options(o =>
            {
                o.RetryDelay = TimeSpan.MaxValue;
                o.MaxRetryDelay = TimeSpan.FromSeconds(5);
                o.MaxAttempts = 70;
            }),
            new HttpClient(model)
        );
        var waits = Record(decider, jitter: 0.999);

        await decider
            .Invoking(d => d.Decide(Ticket, CancellationToken.None))
            .Should()
            .ThrowAsync<DecisionServiceException>();

        waits.Should().HaveCount(69).And.OnlyContain(w => w <= TimeSpan.FromSeconds(5));
    }

    [TestCase(HttpStatusCode.NotImplemented)]
    [TestCase(HttpStatusCode.HttpVersionNotSupported)]
    public async Task Decide_AMethodOrVersionTheServerDoesNotImplement_IsNotRetried(
        HttpStatusCode status
    )
    {
        var model = new FakeModel(_ => Status(status));
        using var decider = new SystemOneDecider(Options(), new HttpClient(model));

        var decide = () => decider.Decide(Ticket, CancellationToken.None);

        var failure = (await decide.Should().ThrowAsync<DecisionServiceException>()).Which;
        ClassOf(failure).Should().Be(FailureClass.Permanent);
        model.Requests.Should().ContainSingle();
    }

    [TestCase(-1)]
    [TestCase(2 * 24 * 60 * 60)]
    public void Constructing_WithAMaxRetryDelayOutOfRange_IsRefused(int seconds)
    {
        var construct = () =>
            new SystemOneDecider(Options(o => o.MaxRetryDelay = TimeSpan.FromSeconds(seconds)));

        construct.Should().Throw<ArgumentException>().WithMessage("*MaxRetryDelay*");
    }

    private static FailureClass? ClassOf(Exception failure) =>
        ((TrainExceptionData)failure.Data["TrainExceptionData"]!).FailureClass;

    /// <summary>Records each wait before a retry instead of waiting, with the jitter fixed.</summary>
    private static List<TimeSpan> Record(SystemOneDecider decider, double jitter)
    {
        var waits = new List<TimeSpan>();
        decider.Wait = (delay, _) =>
        {
            lock (waits)
                waits.Add(delay);
            return Task.CompletedTask;
        };
        decider.Jitter = () => jitter;
        return waits;
    }

    private sealed class Loop
    {
        public Loop? Next { get; set; }
    }

    [Asks("How should the refund be paid?")]
    public enum RefundRoute
    {
        Original,
        Credit,
    }

    [Asks("Why is the order being refunded?", Key = "refund_reason")]
    public enum RefundReason
    {
        Damaged,
        Late,
    }

    private sealed class RefundTrain(IDecider decider) : Train<string, string>
    {
        protected override Task<Trax.Core.Functional.Either<Exception, string>> Junctions() =>
            AddServices(decider)
                .Decide<string>(q => q.Choice<RefundRoute>().Choice<RefundReason>())
                .Resolve();
    }

    private sealed record FreeTextQuestion(string Key, string Instructions)
        : Question(Key, Instructions);

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage Status(
        HttpStatusCode status,
        string body = "",
        int? retryAfter = null
    )
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        if (retryAfter is { } seconds)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(
                TimeSpan.FromSeconds(seconds)
            );
        return response;
    }

    private sealed record SentRequest(string? Authorization, JsonNode Body);

    /// <summary>
    /// A loopback server whose System One path redirects, with a 307 that keeps the method and
    /// body, to a path that answers.
    /// </summary>
    private sealed class RedirectingServer : IDisposable
    {
        private readonly HttpListener _listener = new();

        private int _followed;

        public RedirectingServer(string answer)
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            Endpoint = new Uri($"http://127.0.0.1:{port}/v1/systemone");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Serve(answer);
        }

        public Uri Endpoint { get; }

        public int Followed => Volatile.Read(ref _followed);

        private async Task Serve(string answer)
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                if (context.Request.Url!.AbsolutePath == "/v1/systemone")
                {
                    context.Response.StatusCode = 307;
                    context.Response.RedirectLocation = "/elsewhere";
                }
                else
                {
                    Interlocked.Increment(ref _followed);
                    var bytes = Encoding.UTF8.GetBytes(answer);
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(bytes);
                }

                context.Response.Close();
            }
        }

        public void Dispose() => _listener.Close();
    }

    /// <summary>A model endpoint that answers by attempt number, and can hang until cancelled.</summary>
    private sealed class FakeModel(
        Func<int, HttpResponseMessage> answer,
        Func<int, bool>? hang = null,
        Action? onSend = null,
        Func<Task>? waitFor = null
    ) : HttpMessageHandler
    {
        private readonly object _lock = new();

        private readonly List<(int Count, TaskCompletionSource Reached)> _watchers = [];

        private int _inFlight;

        public List<SentRequest> Requests { get; } = [];

        public int MostInFlight { get; private set; }

        /// <summary>Completes once this many requests are in flight at the same time.</summary>
        public Task InFlightReached(int count)
        {
            lock (_lock)
            {
                if (_inFlight >= count)
                    return Task.CompletedTask;

                var reached = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                _watchers.Add((count, reached));
                return reached.Task;
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var sent = new SentRequest(
                request.Headers.Authorization?.ToString(),
                JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!
            );

            int attempt;
            lock (_lock)
            {
                Requests.Add(sent);
                attempt = Requests.Count;
            }
            onSend?.Invoke();

            if (waitFor is not null)
            {
                lock (_lock)
                {
                    _inFlight++;
                    MostInFlight = Math.Max(MostInFlight, _inFlight);
                    foreach (var watcher in _watchers.Where(w => _inFlight >= w.Count))
                        watcher.Reached.TrySetResult();
                }

                await waitFor();

                lock (_lock)
                    _inFlight--;
            }

            if (hang?.Invoke(attempt) == true)
            {
                // Never answers; only cancellation (the train's, or the attempt timeout) ends it.
                var never = new TaskCompletionSource();
                await using var _ = cancellationToken.Register(() =>
                    never.TrySetCanceled(cancellationToken)
                );
                await never.Task;
            }

            return answer(attempt);
        }
    }
}
