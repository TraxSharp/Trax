using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.ChatService.Auth;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.E2E.Utilities;

namespace Trax.Samples.ChatService.E2E.Fixtures;

[TestFixture]
public abstract class ChatApiTestFixture
{
    protected HttpClient HttpClient
    {
        get => field.NotNull(nameof(HttpClient));
        private set;
    }

    protected GraphQLClient GraphQL
    {
        get => field.NotNull(nameof(GraphQL));
        private set;
    }

    protected IDataContext DataContext
    {
        get => field.NotNull(nameof(DataContext));
        private set;
    }

    protected ChatDbContext ChatDb
    {
        get => field.NotNull(nameof(ChatDb));
        private set;
    }

    private AsyncServiceScope Scope { get; set; }

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
        Scope = SharedChatApiSetup.Factory.Services.CreateAsyncScope();

        var dataContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        DataContext = (IDataContext)dataContextFactory.Create();

        ChatDb = Scope.ServiceProvider.GetRequiredService<ChatDbContext>();

        await CleanData();
    }

    [TearDown]
    public async Task TearDown()
    {
        await ChatDb.DisposeAsync();

        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        await Scope.DisposeAsync().ConfigureAwait(false);
    }

    protected static string AliceKey => ApiKeyDefaults.AliceKey;
    protected static string BobKey => ApiKeyDefaults.BobKey;
    protected static string CharlieKey => ApiKeyDefaults.CharlieKey;

    /// <summary>Creates a room as the caller holding <paramref name="apiKey"/> and returns its id.</summary>
    protected async Task<string> CreateRoomAsAsync(string apiKey, string name = "Room")
    {
        var created = await GraphQL
            .SendAsync(
                $$"""
                mutation { dispatch { createChatRoom(input: { name: "{{name}}" }) { output { chatRoomId } } } }
                """,
                apiKey: apiKey
            )
            .ConfigureAwait(false);
        created.HasErrors.Should().BeFalse(created.FirstErrorMessage);
        return created
            .GetData("dispatch", "createChatRoom", "output")
            .GetProperty("chatRoomId")
            .GetString()
            .NotNull();
    }

    /// <summary>Joins a room as the caller holding <paramref name="apiKey"/>.</summary>
    protected async Task JoinRoomAsAsync(string apiKey, string chatRoomId)
    {
        var joined = await GraphQL
            .SendAsync(
                $$"""
                mutation { dispatch { joinChatRoom(input: { chatRoomId: "{{chatRoomId}}" }) { output { joinedAt } } } }
                """,
                apiKey: apiKey
            )
            .ConfigureAwait(false);
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
        await ChatDb.ChatMessages.ExecuteDeleteAsync().ConfigureAwait(false);
        await ChatDb.ChatParticipants.ExecuteDeleteAsync().ConfigureAwait(false);
        await ChatDb.ChatRooms.ExecuteDeleteAsync().ConfigureAwait(false);

        // Clean Trax execution data
        await DataContext.Logs.ExecuteDeleteAsync().ConfigureAwait(false);
        await DataContext.WorkQueues.ExecuteDeleteAsync().ConfigureAwait(false);
        await DataContext.DeadLetters.ExecuteDeleteAsync().ConfigureAwait(false);
        await DataContext.Metadatas.ExecuteDeleteAsync().ConfigureAwait(false);
        DataContext.Reset();
    }
}
