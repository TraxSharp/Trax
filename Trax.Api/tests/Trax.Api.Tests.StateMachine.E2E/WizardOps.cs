using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// The GraphQL documents of <see cref="WizardMachine"/>'s typed operations under the <c>machines</c> root, and the
/// readers of their answers. The documents are the contract the typed surface is written to:
/// <code>
/// mutation { machines { wizard { start(input: {...}, requestId: "...") { id revision state context { ... } problem { code message } } } } }
/// mutation { machines { wizard { describe(id: "...", input: {...}, requestId: "...") { ... } } } }
/// mutation { machines { wizard { approve(id: "...", requestId: "...") { ... } } } }
/// mutation { machines { wizard { discard(id: "...", requestId: "...") { ... } } } }
/// query    { machines { wizard { get(id: "...") { id revision state context { ... } } } } }
/// </code>
/// Every value is written inline, so a document names no generated type and is the same text on every node.
/// </summary>
public static class WizardOps
{
    /// <summary>The context's fields as the typed surface returns them.</summary>
    public const string ContextFields = "context { title pages note receipt artifact }";

    /// <summary>What every typed mutation returns: the server's snapshot with its revision, or a problem.</summary>
    public const string ResultFields =
        $"id revision state {ContextFields} problem {{ code message }}";

    /// <summary>What <c>get</c> returns.</summary>
    public const string ReadFields = $"id revision state {ContextFields}";

    /// <summary>A GraphQL string literal holding <paramref name="value"/>.</summary>
    public static string Literal(string value) => JsonSerializer.Serialize(value);

    /// <summary>A fresh request id.</summary>
    public static string NewRequestId() => $"req-{Guid.NewGuid():N}";

    /// <summary>The <c>start</c> mutation.</summary>
    public static string Start(string title, string requestId) =>
        $"mutation {{ machines {{ wizard {{ start(input: {{ title: {Literal(title)} }}, requestId: {Literal(requestId)}) {{ {ResultFields} }} }} }} }}";

    /// <summary>
    /// The mutation of the event whose field is <paramref name="field"/>. <paramref name="input"/> is a GraphQL
    /// object literal, or null for an event declared without <c>WithInput</c>.
    /// </summary>
    public static string Event(
        string field,
        Guid id,
        string requestId,
        string? input = null,
        string? clientResult = null
    ) =>
        $"mutation {{ machines {{ wizard {{ {EventField(field, id, requestId, input, clientResult)} }} }} }}";

    /// <summary>
    /// One event field, without the operation around it, for a test that puts several in one operation.
    /// </summary>
    public static string EventField(
        string field,
        Guid id,
        string requestId,
        string? input = null,
        string? clientResult = null,
        string? alias = null
    )
    {
        var args = $"id: {Literal(id.ToString())}";
        if (input is not null)
            args += $", input: {input}";
        args += $", requestId: {Literal(requestId)}";
        if (clientResult is not null)
            args += $", clientResult: {Literal(clientResult)}";
        return $"{(alias is null ? "" : alias + ": ")}{field}({args}) {{ {ResultFields} }}";
    }

    /// <summary>The input object of <see cref="WizardTrigger.Describe"/>.</summary>
    public static string DetailsInput(
        string title = "Atlas",
        int pages = 3,
        string? note = null,
        string? secret = null
    )
    {
        var fields = $"title: {Literal(title)}, pages: {pages}";
        if (note is not null)
            fields += $", note_text: {Literal(note)}";
        if (secret is not null)
            fields += $", secret: {Literal(secret)}";
        return $"{{ {fields} }}";
    }

    /// <summary>The <c>describe</c> mutation.</summary>
    public static string Describe(
        Guid id,
        string requestId,
        string title = "Atlas",
        int pages = 3,
        string? note = null,
        string? secret = null,
        string? clientResult = null
    ) => Event("describe", id, requestId, DetailsInput(title, pages, note, secret), clientResult);

    /// <summary>The <c>discard</c> mutation.</summary>
    public static string Discard(Guid id, string requestId) =>
        $"mutation {{ machines {{ wizard {{ discard(id: {Literal(id.ToString())}, requestId: {Literal(requestId)}) {{ id problem {{ code message }} }} }} }} }}";

    /// <summary>The <c>get</c> query.</summary>
    public static string Get(Guid id) =>
        $"{{ machines {{ wizard {{ get(id: {Literal(id.ToString())}) {{ {ReadFields} }} }} }} }}";

    /// <summary>
    /// The answer of <c>machines.wizard.&lt;field&gt;</c>. Fails the test, quoting the first error, when the request
    /// answered errors: a typed operation refuses through its problem, never through a GraphQL error.
    /// </summary>
    public static MachineResult Result(JsonDocument doc, string field)
    {
        FailOnErrors(doc, field);
        var value = doc
            .RootElement.GetProperty("data")
            .GetProperty("machines")
            .GetProperty("wizard")
            .GetProperty(field);
        if (value.ValueKind == JsonValueKind.Null)
            Assert.Fail($"machines.wizard.{field} answered null: {doc.RootElement.GetRawText()}");
        return new MachineResult(value.Clone());
    }

    /// <summary>
    /// The answer of <c>get</c>, which is null for an id the caller may not see. Fails the test on errors.
    /// </summary>
    public static JsonElement ReadGet(JsonDocument doc)
    {
        FailOnErrors(doc, "get");
        return doc
            .RootElement.GetProperty("data")
            .GetProperty("machines")
            .GetProperty("wizard")
            .GetProperty("get")
            .Clone();
    }

    /// <summary>Fails the test, quoting the first error, when <paramref name="doc"/> carries errors.</summary>
    public static void FailOnErrors(JsonDocument doc, string field)
    {
        if (
            doc.RootElement.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0
        )
            Assert.Fail(
                $"machines.wizard.{field} answered errors: {FirstErrorMessage(doc)} ({errors.GetRawText()})"
            );
    }

    /// <summary>The first error's message, or null when there is none.</summary>
    public static string? FirstErrorMessage(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("errors", out var errors)
        && errors.ValueKind == JsonValueKind.Array
        && errors.GetArrayLength() > 0
            ? errors[0].GetProperty("message").GetString()
            : null;

    /// <summary>The error codes the response carries, in order.</summary>
    public static IReadOnlyList<string?> ErrorCodes(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("errors", out var errors)
        && errors.ValueKind == JsonValueKind.Array
            ? errors
                .EnumerateArray()
                .Select(e =>
                    e.TryGetProperty("extensions", out var ext)
                    && ext.TryGetProperty("code", out var code)
                        ? code.GetString()
                        : null
                )
                .ToList()
            : [];

    /// <summary>Starts a wizard as <paramref name="apiKey"/>'s user and returns the server's answer.</summary>
    public static async Task<MachineResult> StartWizardAsync(
        this IHost host,
        string apiKey,
        string title = "Atlas",
        string? requestId = null
    )
    {
        using var doc = await host.PostAsync(
            Start(title, requestId ?? NewRequestId()),
            apiKey: apiKey
        );
        var result = Result(doc, "start");
        if (result.ProblemCode is { } code)
            Assert.Fail($"start was refused: {code} {result.ProblemMessage}");
        return result;
    }

    /// <summary>Sends one event and returns the server's answer, which may be a problem.</summary>
    public static async Task<MachineResult> SendWizardEventAsync(
        this IHost host,
        string apiKey,
        string field,
        Guid id,
        string? input = null,
        string? requestId = null,
        string? clientResult = null
    )
    {
        using var doc = await host.PostAsync(
            Event(field, id, requestId ?? NewRequestId(), input, clientResult),
            apiKey: apiKey
        );
        return Result(doc, field);
    }

    /// <summary>Sends one event that must be accepted, and returns the server's snapshot.</summary>
    public static async Task<MachineResult> AdvanceWizardAsync(
        this IHost host,
        string apiKey,
        string field,
        Guid id,
        string? input = null,
        string? requestId = null
    )
    {
        var result = await host.SendWizardEventAsync(apiKey, field, id, input, requestId);
        if (result.ProblemCode is { } code)
            Assert.Fail($"{field} was refused: {code} {result.ProblemMessage}");
        return result;
    }

    /// <summary>Starts a wizard and describes it, so it waits in <see cref="WizardState.Paying"/>.</summary>
    public static async Task<MachineResult> WizardInPayingAsync(this IHost host, string apiKey)
    {
        var started = await host.StartWizardAsync(apiKey);
        return await host.AdvanceWizardAsync(apiKey, "describe", started.Id, DetailsInput());
    }

    /// <summary>Starts a wizard, describes it and pays, so it waits in <see cref="WizardState.Approving"/>.</summary>
    public static async Task<MachineResult> WizardInApprovingAsync(this IHost host, string apiKey)
    {
        var paying = await host.WizardInPayingAsync(apiKey);
        return await host.AdvanceWizardAsync(apiKey, "pay", paying.Id);
    }

    /// <summary>The answer of <c>get</c> for <paramref name="apiKey"/>'s caller.</summary>
    public static async Task<JsonElement> GetWizardAsync(this IHost host, string? apiKey, Guid id)
    {
        using var doc = await host.PostAsync(Get(id), apiKey: apiKey);
        return ReadGet(doc);
    }
}

/// <summary>One answer of a typed machine mutation: the server's snapshot, or the problem it refused with.</summary>
public sealed record MachineResult(JsonElement Raw)
{
    /// <summary>The instance's id.</summary>
    public Guid Id =>
        Raw.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? Guid.Parse(id.GetString()!)
            : Guid.Empty;

    /// <summary>The revision the server recorded, or null when the answer carries none.</summary>
    public long? Revision =>
        Raw.TryGetProperty("revision", out var revision)
            ? revision.ValueKind switch
            {
                JsonValueKind.Number => revision.GetInt64(),
                JsonValueKind.String => long.Parse(revision.GetString()!),
                _ => null,
            }
            : null;

    /// <summary>The state, as the schema spells it.</summary>
    public string? State =>
        Raw.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
            ? state.GetString()
            : null;

    /// <summary>The context object.</summary>
    public JsonElement Context => Raw.GetProperty("context");

    /// <summary>The refusal's code, or null when the server accepted.</summary>
    public string? ProblemCode =>
        Raw.TryGetProperty("problem", out var problem) && problem.ValueKind == JsonValueKind.Object
            ? problem.GetProperty("code").GetString()
            : null;

    /// <summary>The refusal's message, or null when the server accepted.</summary>
    public string? ProblemMessage =>
        Raw.TryGetProperty("problem", out var problem) && problem.ValueKind == JsonValueKind.Object
            ? problem.GetProperty("message").GetString()
            : null;

    /// <summary>
    /// Whether the state is <paramref name="state"/>, however the schema spells an enum value
    /// (<c>BuildFailed</c> or <c>BUILD_FAILED</c>).
    /// </summary>
    public bool StateIs(WizardState state) => SameName(State, state.ToString());

    /// <summary>Compares two enum spellings, ignoring case and underscores.</summary>
    public static bool SameName(string? a, string b) =>
        a is not null
        && string.Equals(
            a.Replace("_", "", StringComparison.Ordinal),
            b.Replace("_", "", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase
        );
}
