// What the example on sdk-reference/attributes/trax-allow-anonymous.md names but does not declare.
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public record LookupAnnouncementInput(string Slug);

public record AnnouncementOutput(string Title);

public interface ILookupAnnouncementTrain
    : IServiceTrain<LookupAnnouncementInput, AnnouncementOutput>;

public class LookupAnnouncementJunction : Junction<LookupAnnouncementInput, AnnouncementOutput>
{
    public override Task<AnnouncementOutput> Run(LookupAnnouncementInput input) =>
        Task.FromResult(new AnnouncementOutput(input.Slug));
}
