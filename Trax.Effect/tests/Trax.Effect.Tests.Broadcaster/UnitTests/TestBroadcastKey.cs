namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>The signing key the broadcaster tests share: 32 fixed bytes.</summary>
internal static class TestBroadcastKey
{
    public static byte[] Value { get; } = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
}
