// What the example on sdk-reference/graphql-api/trax-broadcast-attribute.md takes as given: the
// input, output, interface and junction of the train it broadcasts.
public record LookupPlayerInput(string PlayerId);

public record LookupPlayerOutput(string PlayerId, int Rank);

public interface ILookupPlayerTrain : IServiceTrain<LookupPlayerInput, LookupPlayerOutput>;

public class FetchPlayerJunction : Junction<LookupPlayerInput, LookupPlayerOutput>
{
    public override Task<LookupPlayerOutput> Run(LookupPlayerInput input) =>
        Task.FromResult(new LookupPlayerOutput(input.PlayerId, 1));
}
