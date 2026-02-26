using Trax.Effect.Services.EffectProvider;

namespace Trax.Effect.Provider.Json.Services.JsonEffect;

/// <summary>
/// Defines a JSON-based effect provider for tracking and serializing model changes.
/// </summary>
/// <remarks>
/// The IJsonEffectProvider interface serves as a marker interface that extends the base
/// IEffectProvider interface. It represents a specialized effect provider that uses
/// JSON serialization to track and record changes to models.
///
/// This interface is used by the dependency injection system to identify and resolve
/// JSON-specific effect providers. Implementations of this interface are responsible
/// for tracking model changes, serializing them to JSON format, and logging or persisting
/// the serialized data.
///
/// The interface doesn't define any additional methods beyond those inherited from
/// IEffectProvider, but it establishes a distinct type that can be registered and
/// resolved separately from other effect provider implementations.
/// </remarks>
public interface IJsonEffectProvider : IEffectProvider { }
