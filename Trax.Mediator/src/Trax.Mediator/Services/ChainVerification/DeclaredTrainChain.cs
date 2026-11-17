using System.Diagnostics.CodeAnalysis;
using Trax.Core.Monad;

namespace Trax.Mediator.Services.ChainVerification;

/// <summary>A registered train's declared chain, as its class declares it.</summary>
/// <param name="Train">The train's class.</param>
/// <param name="Chain">Its declared chain.</param>
/// <param name="Input">Its input type.</param>
/// <param name="Output">Its output type.</param>
public sealed record DeclaredTrainChain(Type Train, ChainRecorder Chain, Type Input, Type Output);
