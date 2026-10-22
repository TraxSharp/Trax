using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Client;
using Trax.StateMachine.Tests.Client;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Machines;

/// <summary>
/// The C# views <c>trax machine generate --lang csharp</c> writes, as committed beside these tests for the kit's
/// machines: a typed client over <see cref="ServerMachineController"/> that predicts from the embedded IR, and that
/// refuses a manifest of another version.
/// </summary>
public class GeneratedViewTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static ClientManifestMachine Manifest(string machine)
    {
        var text = File.ReadAllText(FixturePaths.IrFile(machine)).TrimEnd('\n');
        return new ClientManifestMachine
        {
            Id = machine,
            IrHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
            Ir = (JsonObject)JsonNode.Parse(text)!,
        };
    }

    [Test]
    public void The_view_carries_the_hash_of_the_IR_it_was_generated_from()
    {
        TurnstileMachine.IrHash.Should().Be(Manifest("turnstile").IrHash);
        CheckoutMachine.IrHash.Should().Be(Manifest("checkout").IrHash);
        TurnstileMachine.Create().Id.Should().Be("turnstile");
    }

    [Test]
    public async Task A_typed_send_is_predicted_and_its_context_read_typed()
    {
        var server = new HandServer();
        using var turnstile = TurnstileClient.Create(Manifest("turnstile"), server, id: "i-1");
        await server.Answer(
            turnstile.Controller,
            ServerAnswer.Of(
                new ServerSnapshot
                {
                    Revision = 1,
                    State = "Locked",
                    Context = [],
                    DueAtKnown = true,
                }
            )
        );
        turnstile.State.Should().Be(TurnstileState.Locked);
        turnstile.AvailableTriggers().Should().Equal(TurnstileTrigger.Coin);
        turnstile.CanSendCoin(new TurnstileCoinInput { Coin = "quarter" }).Should().BeTrue();
        turnstile.CanSendCoin(new TurnstileCoinInput { Coin = "penny" }).Should().BeFalse();

        var send = turnstile.SendCoinAsync(new TurnstileCoinInput { Coin = "quarter" });
        var sent = await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        sent.Input!.ToJsonString().Should().Be("{\"coin\":\"quarter\"}");
        sent.ClientResult.Should()
            .Be(
                "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"quarter\"}}"
            );
        sent.Answer.SetResult(
            ServerAnswer.Of(
                new ServerSnapshot
                {
                    Revision = 2,
                    State = "Unlocked",
                    Context = new JsonObject { ["paidWith"] = "quarter" },
                    DueAtKnown = true,
                }
            )
        );
        (await send.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Confirmed>();

        turnstile.State.Should().Be(TurnstileState.Unlocked);
        turnstile.UnlockedContext!.PaidWith.Should().Be("quarter");
        turnstile.LockedContext.Should().BeNull();
    }

    [Test]
    public void A_manifest_of_another_version_is_refused()
    {
        var other = Manifest("turnstile") with { IrHash = new string('0', 64) };

        var create = () => TurnstileClient.Create(other, new HandServer());

        create
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*not the version this view was generated for*");
    }
}
