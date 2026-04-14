namespace Trax.Samples.JobHunt.Data.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string ApiKey { get; set; }
    public required string DisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
}
