using Trax.Api.Auth;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Auth.Auth;
using Trax.Samples.Auth.Data;

namespace Trax.Samples.Auth.Trains;

public record PublishArticleOutput(long ArticleId, string AuthorId);
