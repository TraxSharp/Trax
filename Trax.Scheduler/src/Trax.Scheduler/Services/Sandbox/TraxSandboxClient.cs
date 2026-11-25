using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Http;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>
/// The hub's side of the sandbox channel: builds the fixed request from the train's declaration,
/// signs it, and reads only an answer the launcher signed for it.
/// </summary>
internal sealed class TraxSandboxClient(
    HttpClient httpClient,
    SandboxClientOptions options,
    ITrainDiscoveryService discovery,
    TimeProvider time,
    ILogger<TraxSandboxClient> logger
) : ITraxSandbox
{
    /// <summary>The launcher's run route.</summary>
    internal const string RunRoute = "/trax/sandbox/run";

    public async Task<SandboxRun<TOutput>> RunAsync<TTrain, TInput, TOutput>(
        TInput input,
        CancellationToken ct = default
    )
        where TTrain : Effect.Services.ServiceTrain.IServiceTrain<TInput, TOutput>
        where TInput : notnull
    {
        ArgumentNullException.ThrowIfNull(input);
        var declared = Declaration(typeof(TTrain), out var implementation);
        if (Undeliverable(declared.Limits) is { } why)
            throw new SandboxRunException(
                SandboxOutcome.Refused,
                $"{implementation.FullName} declares [TraxSandbox] limits that cannot run: {why}"
            );
        var request = new SandboxRunRequest(
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            declared.Profile,
            typeof(TTrain).FullName ?? typeof(TTrain).Name,
            JsonSerializer.Serialize(
                input,
                input.GetType(),
                TraxJsonSerializationOptions.ManifestProperties
            ),
            declared.Limits
        );

        var response = await SendAsync(request, ct).ConfigureAwait(false);

        if (response.Outcome != SandboxOutcome.Completed)
            throw new SandboxRunException(
                response.Outcome,
                $"The sandbox for {request.TrainName} (run {request.RunId}) ended "
                    + $"{response.Outcome}: {response.Reason ?? "no reason given"}"
            );

        var result = ReadResult(request, response.ResultJson);
        if (result.IsError)
            throw result.ToTrainException();

        var files = await ReadFilesAsync(request, response.Archive, ct).ConfigureAwait(false);
        var output = RemoteRunOutput.Read(result.OutputJson, typeof(TOutput), result.OutputType);
        return new SandboxRun<TOutput>(output is TOutput typed ? typed : default, files);
    }

    /// <summary>The longest a hub can wait for an answer: the most a cancellation timer takes.</summary>
    internal static readonly TimeSpan MaxWait = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>Why <paramref name="limits"/> cannot be sent, or null when they can.</summary>
    private string? Undeliverable(SandboxLimits limits) =>
        limits.Invalid()
        ?? (
            TimeSpan.FromSeconds(limits.TimeoutSeconds) + options.ResponseGrace > MaxWait
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"its timeout of {limits.TimeoutSeconds} seconds is longer than the hub can wait for an answer."
                )
                : null
        );

    /// <summary>The declaration on the class registered for <paramref name="train"/>.</summary>
    private TraxSandboxAttribute Declaration(Type train, out Type implementation)
    {
        var registration =
            discovery.DiscoverTrains().FirstOrDefault(r => r.ServiceType == train)
            ?? throw new InvalidOperationException(
                $"{train.FullName} is not a registered train on this host, so its sandbox "
                    + "declaration cannot be read."
            );

        implementation = registration.ImplementationType;
        return registration.ImplementationType.GetCustomAttribute<TraxSandboxAttribute>()
            ?? throw new InvalidOperationException(
                $"{registration.ImplementationType.FullName} does not carry [TraxSandbox], so it "
                    + "has no profile or limits to run in a sandbox with."
            );
    }

    private async Task<SandboxRunResponse> SendAsync(
        SandboxRunRequest request,
        CancellationToken ct
    )
    {
        var key = options.SigningKey ?? throw new InvalidOperationException("No signing key.");
        var body = JsonSerializer.SerializeToUtf8Bytes(request, SandboxJson.Options);
        var signature = SandboxChannelSignature.Sign(key, body, time);

        // The launcher kills the sandbox at its timeout; the grace covers starting and collecting it.
        var wait = TimeSpan.FromSeconds(request.Limits.TimeoutSeconds) + options.ResponseGrace;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(wait);

        using var message = new HttpRequestMessage(HttpMethod.Post, RunRoute)
        {
            Content = new ByteArrayContent(body),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.TryAddWithoutValidation(SandboxChannelSignature.HeaderName, signature);

        HttpResponseMessage answer;
        try
        {
            answer = await httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw Failed(request, $"the launcher did not answer within {wait}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "The sandbox launcher could not be reached.");
            throw Failed(request, $"the launcher could not be reached ({ex.Message})");
        }

        using (answer)
            return await ReadAnswerAsync(request, signature, key, answer, wait, ct, deadline.Token)
                .ConfigureAwait(false);
    }

    /// <summary>Reads the launcher's answer, and only when it is signed for this request.</summary>
    private async Task<SandboxRunResponse> ReadAnswerAsync(
        SandboxRunRequest request,
        string signature,
        byte[] key,
        HttpResponseMessage answer,
        TimeSpan wait,
        CancellationToken ct,
        CancellationToken deadline
    )
    {
        byte[] bytes;
        bool tooLarge;
        try
        {
            (bytes, tooLarge) = await BoundedBody
                .ReadAsync(
                    answer.Content,
                    options.MaxResponseBodyBytes,
                    Timeout.InfiniteTimeSpan,
                    deadline
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw Failed(request, $"the launcher's answer did not arrive within {wait}");
        }

        if (tooLarge)
            throw Failed(
                request,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the launcher's answer is larger than {options.MaxResponseBodyBytes} bytes"
                )
            );

        var responseSignature = answer.Headers.TryGetValues(
            SandboxChannelSignature.ResponseHeaderName,
            out var values
        )
            ? values.FirstOrDefault()
            : null;
        if (!SandboxChannelSignature.VerifyResponse(key, signature, responseSignature, bytes))
            throw Failed(
                request,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the answer (HTTP {(int)answer.StatusCode}) is not signed by the launcher "
                ) + "for this request; check that hub and launcher share the key"
            );

        return Parse(request, bytes);
    }

    private static SandboxRunResponse Parse(SandboxRunRequest request, byte[] bytes)
    {
        SandboxRunResponse response;
        try
        {
            response =
                JsonSerializer.Deserialize<SandboxRunResponse>(bytes, SandboxJson.Options)
                ?? throw new JsonException("null");
        }
        catch (JsonException)
        {
            throw Failed(request, "the launcher's answer is not a sandbox run response");
        }

        // The signature already binds the answer to this request. A refusal of a request too
        // malformed to read may name no run; a completed run must name this one.
        if (
            response.Outcome == SandboxOutcome.Completed
            && !string.Equals(response.RunId, request.RunId, StringComparison.Ordinal)
        )
            throw Failed(request, "the launcher answered for another run");

        return response;
    }

    private static RemoteRunResponse ReadResult(SandboxRunRequest request, string? resultJson)
    {
        if (resultJson is null)
            throw Failed(request, "the launcher reported completion with no result");
        try
        {
            return JsonSerializer.Deserialize<RemoteRunResponse>(resultJson, RemoteRunJson.Read)
                ?? throw new JsonException("null");
        }
        catch (JsonException)
        {
            throw new SandboxRunException(
                SandboxOutcome.Exited,
                $"The sandbox for {request.TrainName} (run {request.RunId}) wrote a result that "
                    + "is not a run response."
            );
        }
    }

    private async Task<IReadOnlyList<SandboxFile>> ReadFilesAsync(
        SandboxRunRequest request,
        string? archive,
        CancellationToken ct
    )
    {
        if (archive is null)
            return [];

        byte[] tar;
        try
        {
            tar = Convert.FromBase64String(archive);
        }
        catch (FormatException)
        {
            throw Failed(request, "the launcher's archive is not base64");
        }

        using var stream = new MemoryStream(tar, writable: false);
        var (files, refused) = await SandboxArchive
            .ReadAsync(stream, options.Archive, "", ct)
            .ConfigureAwait(false);
        if (refused is not null)
            throw new SandboxRunException(
                SandboxOutcome.OutputRefused,
                $"The sandbox for {request.TrainName} (run {request.RunId}) left output the hub "
                    + $"refuses: {refused}"
            );
        return files;
    }

    private static SandboxRunException Failed(SandboxRunRequest request, string why) =>
        new(
            SandboxOutcome.Failed,
            $"The sandbox for {request.TrainName} (run {request.RunId}) failed: {why}"
        );
}
