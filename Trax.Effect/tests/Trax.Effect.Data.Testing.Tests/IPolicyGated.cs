using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

[TraxAuthorize("premium")]
public interface IPolicyGated;
