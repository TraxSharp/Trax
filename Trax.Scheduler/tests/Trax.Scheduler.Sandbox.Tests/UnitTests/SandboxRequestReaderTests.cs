using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Scheduler.Sandbox.Launcher.Core;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// A compromised hub can ask for a sandbox but not shape one: any member beyond the fixed request
/// is a refusal, and limits over the caps are refused rather than lowered.
///
/// <para>Enforces <c>docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md")]
[TestFixture]
public class SandboxRequestReaderTests
{
    private const string Adr =
        "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md";

    private static JsonObject Valid() =>
        new()
        {
            ["runId"] = "0123456789abcdef0123456789abcdef",
            ["profile"] = "escape",
            ["trainName"] = "Trax.Scheduler.Sandbox.Image.IHostileTrain",
            ["inputJson"] = "{\"act\":\"echo\",\"targets\":[]}",
            ["limits"] = new JsonObject
            {
                ["cpuMillicores"] = 500,
                ["memoryMiB"] = 192,
                ["diskMiB"] = 8,
                ["timeoutSeconds"] = 30,
            },
        };

    private static JsonObject Limits(JsonObject request) =>
        request["limits"]?.AsObject() ?? throw new InvalidOperationException("no limits");

    private static string? Refused(JsonObject request) =>
        SandboxRequestReader
            .Read(Encoding.UTF8.GetBytes(request.ToJsonString()), LauncherOptions.Podman())
            .Refused;

    [Test]
    public void Read_TheFixedShape_IsAccepted()
    {
        var (request, profile, refused) = SandboxRequestReader.Read(
            Encoding.UTF8.GetBytes(Valid().ToJsonString()),
            LauncherOptions.Podman()
        );

        refused.Should().BeNull();
        request?.Profile.Should().Be("escape");
        profile
            ?.Image.Should()
            .Be(
                LauncherOptions.Image,
                "the image comes from the launcher's profile, never the request"
            );
    }

    [TestCase("image", "\"docker.io/library/alpine\"")]
    [TestCase("command", "[\"/bin/sh\"]")]
    [TestCase("args", "[\"-c\",\"id\"]")]
    [TestCase("env", "{\"AWS_ROLE_ARN\":\"x\"}")]
    [TestCase("mounts", "[{\"source\":\"/\",\"destination\":\"/host\"}]")]
    [TestCase("volumes", "[\"/var/run/docker.sock:/s\"]")]
    [TestCase("serviceAccountName", "\"default\"")]
    [TestCase("runtimeClassName", "\"runc\"")]
    [TestCase("ociRuntime", "\"crun\"")]
    [TestCase("hostNetwork", "true")]
    [TestCase("network", "\"host\"")]
    [TestCase("privileged", "true")]
    [TestCase("capabilities", "[\"SYS_ADMIN\"]")]
    [TestCase("user", "\"0:0\"")]
    public void Read_AnyMemberBeyondTheShape_IsRefused(string member, string value)
    {
        var request = Valid();
        request[member] = JsonNode.Parse(value);

        Refused(request).Should().Contain("not the sandbox request shape", Adr).And.Contain(member);
    }

    [TestCase("privileged")]
    [TestCase("image")]
    public void Read_AMemberSmuggledIntoTheLimits_IsRefused(string member)
    {
        var request = Valid();
        Limits(request)[member] = true;

        Refused(request).Should().Contain("not the sandbox request shape");
    }

    [Test]
    public void Read_UnknownProfile_IsRefused()
    {
        var request = Valid();
        request["profile"] = "other";

        Refused(request).Should().Contain("no profile 'other'");
    }

    [TestCase("cpuMillicores", 4001, "CPU")]
    [TestCase("memoryMiB", 4097, "memory")]
    [TestCase("diskMiB", 2049, "disk")]
    [TestCase("timeoutSeconds", 3601, "timeout")]
    public void Read_LimitOverTheCap_IsRefusedNotLowered(string limit, int value, string what)
    {
        var request = Valid();
        Limits(request)[limit] = value;

        Refused(request).Should().Contain(what).And.Contain("over this launcher's cap");
    }

    [TestCase("cpuMillicores", 4000)]
    [TestCase("memoryMiB", 4096)]
    [TestCase("diskMiB", 2048)]
    [TestCase("timeoutSeconds", 3600)]
    public void Read_LimitAtTheCap_IsAccepted(string limit, int value)
    {
        var request = Valid();
        Limits(request)[limit] = value;

        Refused(request).Should().BeNull();
    }

    [TestCase("cpuMillicores", 0)]
    [TestCase("memoryMiB", -1)]
    [TestCase("diskMiB", 0)]
    [TestCase("timeoutSeconds", 0)]
    public void Read_NonPositiveLimit_IsRefused(string limit, int value)
    {
        var request = Valid();
        Limits(request)[limit] = value;

        Refused(request).Should().Contain("must be positive");
    }

    [Test]
    public void Read_MissingLimit_IsRefused()
    {
        var request = Valid();
        Limits(request).Remove("timeoutSeconds");

        Refused(request).Should().Contain("not the sandbox request shape");
    }

    [TestCase("0123456789ABCDEF0123456789ABCDEF")]
    [TestCase("0123")]
    [TestCase("../../etc/passwd0123456789abcdef")]
    [TestCase("")]
    public void Read_BadRunId_IsRefused(string runId)
    {
        var request = Valid();
        request["runId"] = runId;

        Refused(request).Should().Contain("run id");
    }

    [TestCase("")]
    [TestCase("a b")]
    [TestCase("x;rm -rf /")]
    [TestCase("1Train")]
    public void Read_BadTrainName_IsRefused(string name)
    {
        var request = Valid();
        request["trainName"] = name;

        Refused(request).Should().Contain("train name");
    }

    [Test]
    public void Read_InputOverTheCap_IsRefused()
    {
        var options = LauncherOptions.Podman();
        options.Caps.MaxInputBytes = 16;
        var request = Valid();
        request["inputJson"] = JsonSerializer.Serialize(new string('x', 20));

        SandboxRequestReader
            .Read(Encoding.UTF8.GetBytes(request.ToJsonString()), options)
            .Refused.Should()
            .Contain("larger than 16 bytes");
    }

    [Test]
    public void Read_InputNotJson_IsRefused()
    {
        var request = Valid();
        request["inputJson"] = "{not json";

        Refused(request).Should().Contain("not JSON");
    }

    [TestCase("")]
    [TestCase("null")]
    [TestCase("[]")]
    [TestCase("{\"runId\": 1}")]
    public void Read_NotTheShapeAtAll_IsRefused(string body) =>
        SandboxRequestReader
            .Read(Encoding.UTF8.GetBytes(body), LauncherOptions.Podman())
            .Refused.Should()
            .NotBeNull();

    [Test]
    public void Read_RefusalOfAHostileProfileName_DoesNotEchoControlCharacters()
    {
        var request = Valid();
        request["profile"] = "evil\n\u001b[2Jname";

        Refused(request).Should().NotContain("\n").And.NotContain("\u001b");
    }
}
