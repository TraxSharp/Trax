using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

// Each train has an input type of its own: the mediator routes a run by its input's type.

/// <summary>What every invoked train here returns: a pointer, never data.</summary>
public sealed record JobOutput
{
    public string Artifact { get; init; } = "";
}

public sealed record GoodInput(string Source);

public interface IGoodTrain : IServiceTrain<GoodInput, JobOutput>;

/// <summary>A train a machine may invoke: a ServiceTrain built only from effect junctions.</summary>
public class GoodTrain : ServiceTrain<GoodInput, JobOutput>, IGoodTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<GoodStep>().Resolve();
}

public sealed class GoodStep : EffectJunction<GoodInput, JobOutput>
{
    public override Task<JobOutput> Run(GoodInput input) =>
        Task.FromResult(new JobOutput { Artifact = "artifact:" + input.Source });
}

public sealed record PlainInput(string Source);

public interface IPlainJunctionTrain : IServiceTrain<PlainInput, JobOutput>;

/// <summary>Runs a plain junction, which never reads the run's cancel flag.</summary>
public class PlainJunctionTrain : ServiceTrain<PlainInput, JobOutput>, IPlainJunctionTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<PlainStep>().Resolve();
}

public sealed class PlainStep : Junction<PlainInput, JobOutput>
{
    public override Task<JobOutput> Run(PlainInput input) => Task.FromResult(new JobOutput());
}

public sealed record ContractInput(string Source);

/// <summary>A junction contract IChain resolves; the container registers it with a plain junction.</summary>
public interface IPlainContract : IJunction<ContractInput, JobOutput>;

public sealed class PlainContractStep : Junction<ContractInput, JobOutput>, IPlainContract
{
    public override Task<JobOutput> Run(ContractInput input) => Task.FromResult(new JobOutput());
}

public interface IContractTrain : IServiceTrain<ContractInput, JobOutput>;

public class ContractTrain : ServiceTrain<ContractInput, JobOutput>, IContractTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        IChain<IPlainContract>().Resolve();
}

public sealed record BareInput(string Source);

public interface IBareTrain : IServiceTrain<BareInput, JobOutput>;

/// <summary>Implements the train interface without ServiceTrain, so its run is its own.</summary>
public sealed class BareTrain : IBareTrain
{
    public Metadata? Metadata => null;

    public Task<JobOutput> Run(BareInput input, CancellationToken cancellationToken = default) =>
        Task.FromResult(new JobOutput());

    public void Dispose() { }
}

public sealed record RefusedInput(string Source);

public interface IRefusedChainTrain : IServiceTrain<RefusedInput, JobOutput>;

/// <summary>Names a class in IChain, which the chain recorder refuses.</summary>
public class RefusedChainTrain : ServiceTrain<RefusedInput, JobOutput>, IRefusedChainTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        IChain<RefusedStep>().Resolve();
}

public sealed class RefusedStep : EffectJunction<RefusedInput, JobOutput>
{
    public override Task<JobOutput> Run(RefusedInput input) => Task.FromResult(new JobOutput());
}

public sealed record ForkInput(string Source);

public sealed record LeftOut(string Value);

public sealed record RightOut(string Value);

public interface IForkTrain : IServiceTrain<ForkInput, JobOutput>;

/// <summary>A Parallel step whose second branch runs a plain junction.</summary>
public class ForkTrain : ServiceTrain<ForkInput, JobOutput>, IForkTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Parallel(p =>
                p.Branch("effect", b => b.Chain<ForkLeft>())
                    .Branch("plain", b => b.Chain<PlainForkRight>())
            )
            .Chain<ForkJoin>()
            .Resolve();
}

public sealed class ForkLeft : EffectJunction<ForkInput, LeftOut>
{
    public override Task<LeftOut> Run(ForkInput input) => Task.FromResult(new LeftOut("l"));
}

public sealed class PlainForkRight : Junction<ForkInput, RightOut>
{
    public override Task<RightOut> Run(ForkInput input) => Task.FromResult(new RightOut("r"));
}

public sealed class ForkJoin : EffectJunction<LeftOut, JobOutput>
{
    public override Task<JobOutput> Run(LeftOut input) => Task.FromResult(new JobOutput());
}

public sealed record HookedInput(string Source);

public interface IHookedTrain : IServiceTrain<HookedInput, JobOutput>;

/// <summary>Has an OnQueue hook and defers promotion, both of which commit outside the caller's transaction.</summary>
public class HookedTrain : ServiceTrain<HookedInput, JobOutput>, IHookedTrain
{
    protected override bool DeferQueuePromotion => true;

    protected override Task OnQueue(Metadata metadata, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<HookedStep>().Resolve();
}

public sealed class HookedStep : EffectJunction<HookedInput, JobOutput>
{
    public override Task<JobOutput> Run(HookedInput input) => Task.FromResult(new JobOutput());
}

public sealed record AdminInput(string Source);

public interface IAdminTrain : IServiceTrain<AdminInput, JobOutput>;

/// <summary>Needs a role, stricter than the state-machine mutations' authenticated user.</summary>
[TraxAuthorize(Roles = "admin")]
public class AdminTrain : ServiceTrain<AdminInput, JobOutput>, IAdminTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<AdminStep>().Resolve();
}

public sealed class AdminStep : EffectJunction<AdminInput, JobOutput>
{
    public override Task<JobOutput> Run(AdminInput input) => Task.FromResult(new JobOutput());
}

public sealed record LoudInput(string Source);

public interface ILoudTrain : IServiceTrain<LoudInput, JobOutput>;

/// <summary>Broadcast: its subscribers see every run's output.</summary>
[TraxBroadcast]
public class LoudTrain : ServiceTrain<LoudInput, JobOutput>, ILoudTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<LoudStep>().Resolve();
}

public sealed class LoudStep : EffectJunction<LoudInput, JobOutput>
{
    public override Task<JobOutput> Run(LoudInput input) => Task.FromResult(new JobOutput());
}

public sealed record SecretInput(string Source);

/// <summary>An output that carries a sensitive value.</summary>
public sealed record SecretOutput
{
    public string Artifact { get; init; } = "";

    [TraxSensitive]
    public string ApiKey { get; init; } = "";
}

public interface ISecretTrain : IServiceTrain<SecretInput, SecretOutput>;

public class SecretTrain : ServiceTrain<SecretInput, SecretOutput>, ISecretTrain
{
    protected override Task<Either<Exception, SecretOutput>> Junctions() =>
        Chain<SecretStep>().Resolve();
}

public sealed class SecretStep : EffectJunction<SecretInput, SecretOutput>
{
    public override Task<SecretOutput> Run(SecretInput input) =>
        Task.FromResult(new SecretOutput());
}
