using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.CreateApplication.Junctions;

namespace Trax.Samples.JobHunt.Trains.CreateApplication;

[TraxMutation(Description = "Creates a new application for a job posting")]
public class CreateApplicationTrain
    : ServiceTrain<CreateApplicationInput, CreateApplicationOutput>,
        ICreateApplicationTrain
{
    protected override CreateApplicationOutput Junctions() => Chain<PersistApplicationJunction>();
}
