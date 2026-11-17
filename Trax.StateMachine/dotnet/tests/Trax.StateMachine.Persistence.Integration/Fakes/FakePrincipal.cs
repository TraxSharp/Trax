namespace Trax.StateMachine.Persistence.Integration.Fakes;

public sealed class FakePrincipal(string? userKey) : ISnapshotPrincipal
{
    public string? CurrentUserKey => userKey;
}
