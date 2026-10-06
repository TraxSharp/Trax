using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Effect.Configuration.TraxEffectBuilder;

namespace Trax.Effect.Decisions.SystemOne.Extensions;

/// <summary>
/// Registers <see cref="SystemOneDecider"/> on the effect builder.
/// </summary>
/// <remarks>
/// Each method has two forms. Without a name it registers the decider every train asks, as both
/// <see cref="SystemOneDecider"/> and <see cref="IDecider"/>, and may be used once. With a name it
/// registers a keyed <see cref="SystemOneDecider"/> and nothing else, so several models can sit
/// side by side and be composed, for example Nimble in front of Jev:
/// <code>
/// services.AddTrax(trax => trax.AddEffects(effects => effects
///     .AddNimbleDecider("nimble", o => o.Endpoint = new Uri("http://localhost:8000/v1/systemone"))
///     .AddSystemOneDecider("jev", o =>
///     {
///         o.Endpoint = new Uri("https://api.typesafe.ai/v1/systemone");
///         o.Model = "jev-1.13.0";
///         o.ApiKey = configuration["Jev:ApiKey"];
///     })));
///
/// services.AddSingleton&lt;IDecider&gt;(sp => new CascadingDecider(
///     sp.GetRequiredKeyedService&lt;SystemOneDecider&gt;("nimble"),
///     sp.GetRequiredKeyedService&lt;SystemOneDecider&gt;("jev")));
/// </code>
/// The options are checked when the method is called, so a missing endpoint, an unpinned model or
/// plain HTTP to a remote host stops the host from starting rather than failing the first
/// decision. The decider itself is built on first use and owned by the container, which disposes
/// its HTTP client when the host stops.
/// </remarks>
public static class ServiceExtensions
{
    /// <summary>
    /// Answers every train's decisions through a typed decision model that speaks the System One
    /// request format (Jev, or a server that accepts it, such as Nimble's), by registering a
    /// <see cref="SystemOneDecider"/> as the <see cref="IDecider"/>.
    /// </summary>
    /// <remarks>
    /// <code>
    /// services.AddTrax(trax => trax.AddEffects(effects => effects
    ///     .UsePostgres(connectionString)
    ///     .AddDecisionRecording()
    ///     .AddSystemOneDecider(o =>
    ///     {
    ///         o.Endpoint = new Uri("https://api.typesafe.ai/v1/systemone");
    ///         o.Model = "jev-1.13.0";
    ///         o.ApiKey = configuration["Jev:ApiKey"];
    ///     })));
    /// </code>
    /// To run more than one model, name each one with
    /// <see cref="AddSystemOneDecider{TBuilder}(TBuilder, string, Action{SystemOneOptions})"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">The configured options are not usable.</exception>
    /// <exception cref="InvalidOperationException">An unnamed System One decider is already registered.</exception>
    public static TBuilder AddSystemOneDecider<TBuilder>(
        this TBuilder configurationBuilder,
        Action<SystemOneOptions> configure
    )
        where TBuilder : TraxEffectBuilder
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SystemOneOptions();
        configure(options);

        Register(configurationBuilder.ServiceCollection, name: null, options);
        return configurationBuilder;
    }

    /// <summary>
    /// Registers a typed decision model that speaks the System One request format as a keyed
    /// <see cref="SystemOneDecider"/>, under <paramref name="name"/>, without making it the
    /// <see cref="IDecider"/>. Resolve it with <c>GetRequiredKeyedService&lt;SystemOneDecider&gt;(name)</c>,
    /// for example to build a <see cref="CascadingDecider"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The name is blank, or the configured options are not usable.
    /// </exception>
    /// <exception cref="InvalidOperationException">A System One decider is already registered under this name.</exception>
    public static TBuilder AddSystemOneDecider<TBuilder>(
        this TBuilder configurationBuilder,
        string name,
        Action<SystemOneOptions> configure
    )
        where TBuilder : TraxEffectBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SystemOneOptions();
        configure(options);

        Register(configurationBuilder.ServiceCollection, name, options);
        return configurationBuilder;
    }

    /// <summary>
    /// Answers every train's decisions with Nimble, Bespoke Labs' open-weights typed decision
    /// model, on a Nimble server you run. Registers a <see cref="SystemOneDecider"/> as the
    /// <see cref="IDecider"/>.
    /// </summary>
    /// <remarks>
    /// <code>
    /// effects.AddNimbleDecider(o =&gt;
    /// {
    ///     o.Endpoint = new Uri("https://nimble.internal.example/v1/systemone");
    ///     o.ApiKey = configuration["Nimble:ApiKey"];   // when the server sets OPENJEV_API_KEY
    /// });
    /// </code>
    /// <see cref="NimbleOptions.Endpoint"/> is required: there is no hosted Nimble to default to.
    /// The model, concurrency and per-question limits default to what Nimble's server accepts.
    /// </remarks>
    /// <exception cref="ArgumentException">The configured options are not usable.</exception>
    /// <exception cref="InvalidOperationException">An unnamed System One decider is already registered.</exception>
    public static TBuilder AddNimbleDecider<TBuilder>(
        this TBuilder configurationBuilder,
        Action<NimbleOptions> configure
    )
        where TBuilder : TraxEffectBuilder
    {
        ArgumentNullException.ThrowIfNull(configure);

        Register(configurationBuilder.ServiceCollection, name: null, Nimble(configure));
        return configurationBuilder;
    }

    /// <summary>
    /// Registers Nimble, on a Nimble server you run, as a keyed <see cref="SystemOneDecider"/>
    /// under <paramref name="name"/>, without making it the <see cref="IDecider"/>; for example
    /// as the first tier of a <see cref="CascadingDecider"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The name is blank, or the configured options are not usable.
    /// </exception>
    /// <exception cref="InvalidOperationException">A System One decider is already registered under this name.</exception>
    public static TBuilder AddNimbleDecider<TBuilder>(
        this TBuilder configurationBuilder,
        string name,
        Action<NimbleOptions> configure
    )
        where TBuilder : TraxEffectBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        Register(configurationBuilder.ServiceCollection, name, Nimble(configure));
        return configurationBuilder;
    }

    private static SystemOneOptions Nimble(Action<NimbleOptions> configure)
    {
        var options = new NimbleOptions();
        configure(options);

        if (options.Problems().ToList() is { Count: > 0 } problems)
            throw new ArgumentException(
                $"AddNimbleDecider cannot be used: {string.Join(" ", problems)}",
                nameof(configure)
            );

        return options.ToSystemOne();
    }

    private static void Register(
        IServiceCollection services,
        string? name,
        SystemOneOptions options
    )
    {
        options.Check(nameof(options));
        var settings = options.Copy();

        if (
            services.Any(d =>
                d.ServiceType == typeof(SystemOneDecider) && Equals(d.ServiceKey, name)
            )
        )
            throw new InvalidOperationException(
                name is null
                    ? "A System One decider is already registered as the IDecider. To run more "
                        + "than one model, give each a name (AddSystemOneDecider(\"name\", ...) or "
                        + "AddNimbleDecider(\"name\", ...)) and compose them, for example with a "
                        + "CascadingDecider."
                    : $"A System One decider named '{name}' is already registered. Give each "
                        + "model its own name."
            );

        if (name is null)
        {
            services.AddSingleton(_ => new SystemOneDecider(settings));
            services.AddSingleton<IDecider>(sp => sp.GetRequiredService<SystemOneDecider>());
        }
        else
        {
            services.AddKeyedSingleton(name, (_, _) => new SystemOneDecider(settings));
        }
    }
}
