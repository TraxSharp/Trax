using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

#region Query train (no per-train auth)

public record EchoInput : IManifestProperties
{
    public required string Message { get; init; }
}

public record EchoOutput
{
    public required string Reply { get; init; }
}

public interface IEchoTrain : IServiceTrain<EchoInput, EchoOutput>;

[TraxQuery(Namespace = "audit", Description = "Echoes input back.")]
public class EchoTrain : ServiceTrain<EchoInput, EchoOutput>, IEchoTrain
{
    protected override EchoOutput Junctions() => Chain<EchoJunction>();
}

internal sealed class EchoJunction : Junction<EchoInput, EchoOutput>
{
    public override Task<EchoOutput> Run(EchoInput input) =>
        Task.FromResult(new EchoOutput { Reply = $"echo: {input.Message}" });
}

#endregion

#region Mutation train (no per-train auth)

public record NotifyInput : IManifestProperties
{
    public required string Topic { get; init; }
    public required string Body { get; init; }
}

public record NotifyOutput
{
    public required string DeliveryId { get; init; }
}

public interface INotifyTrain : IServiceTrain<NotifyInput, NotifyOutput>;

[TraxMutation(Namespace = "audit", Description = "Notifies a topic.")]
public class NotifyTrain : ServiceTrain<NotifyInput, NotifyOutput>, INotifyTrain
{
    protected override NotifyOutput Junctions() => Chain<NotifyJunction>();
}

internal sealed class NotifyJunction : Junction<NotifyInput, NotifyOutput>
{
    public override Task<NotifyOutput> Run(NotifyInput input) =>
        Task.FromResult(new NotifyOutput { DeliveryId = $"{input.Topic}:{Guid.NewGuid():N}" });
}

#endregion

#region Query train gated by [TraxAuthorize(Roles="Admin")]

public record AdminLookupInput : IManifestProperties
{
    public required string Target { get; init; }
}

public record AdminLookupOutput
{
    public required string Secret { get; init; }
}

public interface IAdminLookupTrain : IServiceTrain<AdminLookupInput, AdminLookupOutput>;

[TraxQuery(Namespace = "admin", Description = "Admin-only lookup.")]
[TraxAuthorize(Roles = "Admin")]
public class AdminLookupTrain : ServiceTrain<AdminLookupInput, AdminLookupOutput>, IAdminLookupTrain
{
    protected override AdminLookupOutput Junctions() => Chain<AdminLookupJunction>();
}

internal sealed class AdminLookupJunction : Junction<AdminLookupInput, AdminLookupOutput>
{
    public override Task<AdminLookupOutput> Run(AdminLookupInput input) =>
        Task.FromResult(new AdminLookupOutput { Secret = $"classified:{input.Target}" });
}

#endregion

#region Mutation train gated by [TraxAuthorize(Policy="AdminPolicy")]

public record WipeInput : IManifestProperties
{
    public required string Target { get; init; }
}

public record WipeOutput
{
    public required string Acknowledged { get; init; }
}

public interface IWipeTrain : IServiceTrain<WipeInput, WipeOutput>;

[TraxMutation(Namespace = "admin", Description = "Admin-only wipe.")]
[TraxAuthorize(Policy = "AdminPolicy")]
public class WipeTrain : ServiceTrain<WipeInput, WipeOutput>, IWipeTrain
{
    protected override WipeOutput Junctions() => Chain<WipeJunction>();
}

internal sealed class WipeJunction : Junction<WipeInput, WipeOutput>
{
    public override Task<WipeOutput> Run(WipeInput input) =>
        Task.FromResult(new WipeOutput { Acknowledged = $"wiped:{input.Target}" });
}

#endregion
