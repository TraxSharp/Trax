namespace Trax.Effect.Configuration.TraxEffectBuilder;

public partial class TraxEffectBuilder
{
    internal TraxEffectConfiguration.TraxEffectConfiguration Build()
    {
        if (JunctionProgressEnabled && !HasDataProvider)
        {
            throw new InvalidOperationException(
                "AddJunctionProgress() requires a data provider (UsePostgres() or UseInMemory()). "
                    + "Junction progress tracking persists progress to metadata and checks for cancellation signals, "
                    + "which requires a data context.\n\n"
                    + "Add a data provider to your effects configuration:\n\n"
                    + "  services.AddTrax(trax => trax\n"
                    + "      .AddEffects(effects => effects\n"
                    + "          .UsePostgres(connectionString) // or .UseInMemory()\n"
                    + "          .AddJunctionProgress()\n"
                    + "      )\n"
                    + "  );\n"
            );
        }

        var configuration = new TraxEffectConfiguration.TraxEffectConfiguration
        {
            SystemJsonSerializerOptions = TrainParameterJsonSerializerOptions,
            SerializeJunctionData = SerializeJunctionData,
            LogLevel = LogLevel,
        };

        TraxEffectConfiguration.TraxEffectConfiguration.StaticSystemJsonSerializerOptions =
            TrainParameterJsonSerializerOptions;

        return configuration;
    }
}
