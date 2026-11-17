namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>The order machine's irreversible effect port (bound inline via RunsOnce&lt;IOrderCharge&gt;).</summary>
public interface IOrderCharge : ISnapshotEffect;
