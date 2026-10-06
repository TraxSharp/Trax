using Trax.Api.GraphQL.Filtering;

namespace Trax.Api.GraphQL.Configuration.TraxFilterBuilder;

public partial class TraxFilterBuilder
{
    /// <summary>
    /// Adds the case-insensitive string operations <c>icontains</c> and <c>ieq</c> to
    /// every string filter input in the schema. They fold with SQL <c>lower()</c>, so
    /// <c>icontains</c> scales with a <c>gin(lower(col) gin_trgm_ops)</c> index and
    /// <c>ieq</c> with a <c>btree(lower(col))</c> index. The built-in case-sensitive
    /// <c>contains</c> / <c>eq</c> operations are unchanged.
    /// </summary>
    public TraxFilterBuilder AddCaseInsensitiveStringOperations()
    {
        // Idempotent: registering the same operation names twice makes HotChocolate
        // throw at schema build, so a repeated call is a no-op.
        if (Modules.Any(m => m is CaseInsensitiveStringFilterModule))
            return this;

        Modules.Add(new CaseInsensitiveStringFilterModule());
        return this;
    }
}
