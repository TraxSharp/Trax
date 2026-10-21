using Trax.Samples.Board.Auth;
using Trax.Samples.Board.E2E.Utilities;

namespace Trax.Samples.Board.E2E.Fixtures;

/// <summary>Gives each test the board's five workers, each with its own key, and a way to file a fresh ticket.</summary>
public abstract class BoardTestFixture
{
    protected HttpClient Http { get; private set; } = null!;
    protected GraphQLClient GraphQL { get; private set; } = null!;
    protected BoardWorker Triage { get; private set; } = null!;
    protected BoardWorker Repro { get; private set; } = null!;
    protected BoardWorker Fix { get; private set; } = null!;
    protected BoardWorker Qa { get; private set; } = null!;
    protected BoardWorker Sam { get; private set; } = null!;

    [SetUp]
    public void OpenConnections()
    {
        Http = SharedBoardSetup.Factory.CreateClient();
        GraphQL = new GraphQLClient(Http);
        Triage = new BoardWorker(GraphQL, BoardKeys.TriageBot);
        Repro = new BoardWorker(GraphQL, BoardKeys.ReproBot);
        Fix = new BoardWorker(GraphQL, BoardKeys.FixBot);
        Qa = new BoardWorker(GraphQL, BoardKeys.QaBot);
        Sam = new BoardWorker(GraphQL, BoardKeys.Sam);
    }

    [TearDown]
    public void CloseConnections() => Http.Dispose();

    /// <summary>Files a ticket from <paramref name="report"/> as Sam, under an id no other test uses. It lands in Triage.</summary>
    protected async Task<string> FileAsync(string report)
    {
        var id = $"T-{Guid.NewGuid():N}"[..14];
        var response = await GraphQL.SendAsync(
            "mutation($i: FileTicketInput!) { dispatch { fileTicket(input: $i) { output { id state } } } }",
            BoardKeys.Sam,
            new { i = new { id, report } }
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        response
            .GetData("dispatch", "fileTicket", "output", "state")
            .GetString()
            .Should()
            .Be("Triage");
        return id;
    }

    /// <summary>Files a ticket and has triage-bot send it to Reproduce.</summary>
    protected async Task<string> TriagedAsync(string report)
    {
        var id = await FileAsync(report);
        await Triage.ClaimAndStartAsync("triage", id);
        var triaged = await Triage.SubmitAsync(
            id,
            "triage",
            "TriageInput",
            new { area = "shipping" }
        );
        triaged.State.Should().Be("Accepted");
        return id;
    }

    protected async Task<string> LaneOf(string ticketId) =>
        (await Sam.TicketAsync(ticketId)).GetProperty("lane").GetString()!;

    protected async Task<string> StateOf(string ticketId) =>
        (await Sam.TicketAsync(ticketId)).GetProperty("state").GetString()!;
}
