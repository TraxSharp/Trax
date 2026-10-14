// What the example on sdk-reference/train-methods/parallel.md names but does not declare: the
// corpus, the three signals its branches compute, and the junctions on either side of the join.
using Trax.Core.Junction;

public record CorpusRequest(string Topic);

public record Corpus(IReadOnlyList<string> Documents);

public record EmbeddingScores(double Score);

public record CoCitationScores(double Score);

public record AuthorScores(double Score);

public record TopicMap(double Embedding, double CoCitation, double Authors);

public class LoadCorpus : Junction<CorpusRequest, Corpus>
{
    public override Task<Corpus> Run(CorpusRequest input) => Task.FromResult(new Corpus([]));
}

public class EmbeddingSimilarity : Junction<Corpus, EmbeddingScores>
{
    public override Task<EmbeddingScores> Run(Corpus input) =>
        Task.FromResult(new EmbeddingScores(0));
}

public class CoCitationOverlap : Junction<Corpus, CoCitationScores>
{
    public override Task<CoCitationScores> Run(Corpus input) =>
        Task.FromResult(new CoCitationScores(0));
}

public class SharedAuthorOverlap : Junction<Corpus, AuthorScores>
{
    public override Task<AuthorScores> Run(Corpus input) => Task.FromResult(new AuthorScores(0));
}

public class CombineSignals : Junction<(EmbeddingScores, CoCitationScores, AuthorScores), TopicMap>
{
    public override Task<TopicMap> Run((EmbeddingScores, CoCitationScores, AuthorScores) input) =>
        Task.FromResult(new TopicMap(input.Item1.Score, input.Item2.Score, input.Item3.Score));
}
