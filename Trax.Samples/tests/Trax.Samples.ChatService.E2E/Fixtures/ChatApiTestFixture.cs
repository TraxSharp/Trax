using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.ChatService.Auth;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.E2E.Utilities;

namespace Trax.Samples.ChatService.E2E.Fixtures;

[TestFixture]
public abstract class ChatApiTestFixture
{
    protected HttpClient HttpClient { get; private set; } = null!;

    protected GraphQLClient GraphQL { get; private set; } = null!;

    protected IDataContext DataContext { get; private set; } = null!;

    protected ChatDbContext ChatDb { get; private set; } = null!;

    private IServiceScope Scope { get; set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        HttpClient = SharedChatApiSetup.Factory.CreateClient();
        GraphQL = new GraphQLClient(HttpClient);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        HttpClient.Dispose();
    }

    [SetUp]
    public virtual async Task SetUp()
    {
        Scope = SharedChatApiSetup.Factory.Services.CreateScope();

        var dataContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        DataContext = (IDataContext)dataContextFactory.Create();

        ChatDb = Scope.ServiceProvider.GetRequiredService<ChatDbContext>();

        await CleanData();
    }

    [TearDown]
    public void TearDown()
    {
        ChatDb.Dispose();

        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        Scope.Dispose();
    }

    protected static string AliceKey => ApiKeyDefaults.AliceKey;
    protected static string BobKey => ApiKeyDefaults.BobKey;
    protected static string CharlieKey => ApiKeyDefaults.CharlieKey;

    /// <summary>Creates a room as the caller holding <paramref name="apiKey"/> and returns its id.</summary>
    protected async Task<string> CreateRoomAsAsync(string apiKey, string name = "Room")
    {
        var created = await GraphQL.SendAsync(
            $$"""
            mutation { dispatch { createChatRoom(input: { name: "{{name}}" }) { output { chatRoomId } } } }
            """,
            apiKey: apiKey
        );
        created.HasErrors.Should().BeFalse(created.FirstErrorMessage);
        return created
            .GetData("dispatch", "createChatRoom", "output")
            .GetProperty("chatRoomId")
            .GetString()!;
    }

    /// <summary>Joins a room as the caller holding <paramref name="apiKey"/>.</summary>
    protected async Task JoinRoomAsAsync(string apiKey, string chatRoomId)
    {
        var joined = await GraphQL.SendAsync(
            $$"""
            mutation { dispatch { joinChatRoom(input: { chatRoomId: "{{chatRoomId}}" }) { output { joinedAt } } } }
            """,
            apiKey: apiKey
        );
        joined.HasErrors.Should().BeFalse(joined.FirstErrorMessage);
    }

    /// <summary>Sends a message as the caller holding <paramref name="apiKey"/>; returns the response.</summary>
    protected Task<GraphQLResponse> SendMessageAsAsync(
        string apiKey,
        string chatRoomId,
        string content
    ) =>
        GraphQL.SendAsync(
            $$"""
            mutation { dispatch { sendMessage(input: { chatRoomId: "{{chatRoomId}}", content: "{{content}}" }) { output { messageId senderUserId } } } }
            """,
            apiKey: apiKey
        );

    private async Task CleanData()
    {
        // Clean chat domain data (FK order: messages → participants → rooms)
        await ChatDb.ChatMessages.ExecuteDeleteAsync();
        await ChatDb.ChatParticipants.ExecuteDeleteAsync();
        await ChatDb.ChatRooms.ExecuteDeleteAsync();

        // Clean Trax execution data
        await DataContext.Logs.ExecuteDeleteAsync();
        await DataContext.WorkQueues.ExecuteDeleteAsync();
        await DataContext.DeadLetters.ExecuteDeleteAsync();
        await DataContext.Metadatas.ExecuteDeleteAsync();
        DataContext.Reset();
    }
}
