// What the compiled examples on security/authorization-rules.md take as given: the trains' interfaces, inputs,
// outputs and junctions, and the entity the type-extension example extends.
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

public record UserInfo(string Id);

public record DeleteUserInput(string UserId);

public record ReportInput(int Year);

public record ReportOutput(string Url);

public record LookupAnnouncementInput(long Id);

public record AnnouncementOutput(string Title);

public record Excerpt(string Text);

public interface IWhoAmITrain : IServiceTrain<Unit, UserInfo>;

public interface IDeleteUserTrain : IServiceTrain<DeleteUserInput, Unit>;

public interface IGenerateReportTrain : IServiceTrain<ReportInput, ReportOutput>;

public interface ILookupAnnouncementTrain
    : IServiceTrain<LookupAnnouncementInput, AnnouncementOutput>;

public class ReadUserJunction : Junction<Unit, UserInfo>
{
    public override Task<UserInfo> Run(Unit input) => Task.FromResult(new UserInfo("alice"));
}

public class DeleteUserJunction : Junction<DeleteUserInput, Unit>
{
    public override Task<Unit> Run(DeleteUserInput input) => Task.FromResult(Unit.Default);
}

public class GenerateReportJunction : Junction<ReportInput, ReportOutput>
{
    public override Task<ReportOutput> Run(ReportInput input) =>
        Task.FromResult(new ReportOutput($"/reports/{input.Year}"));
}

public class LookupAnnouncementJunction : Junction<LookupAnnouncementInput, AnnouncementOutput>
{
    public override Task<AnnouncementOutput> Run(LookupAnnouncementInput input) =>
        Task.FromResult(new AnnouncementOutput("Maintenance on Sunday"));
}

[TraxQueryModel(Namespace = "public")]
[TraxAllowAnonymous]
public class Article
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}
