using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Fails the host at startup when an <c>@authorize</c> directive anywhere in the built schema
/// names a policy the host has not registered: a resolver's or type's <c>[TraxAuthorize(Policy =
/// ...)]</c>, a <c>GateOperations(policy: ...)</c> gate, or a directive a <c>ConfigureSchema</c>
/// callback added.
/// </summary>
/// <remarks>
/// Without it the host starts, and every caller of the field is refused at request time with an
/// error that named the missing policy. Reading the built schema rather than Trax's registration
/// lists means a directive from any source is covered, including ones no registration records.
/// <c>QueryModelAuthorizationValidator</c> and <c>TraxGraphQLAuthPolicyValidator</c> check the
/// query models' and the endpoint's policies before the schema is built, with messages that name
/// the attribute; this gate is the catch-all after them.
/// </remarks>
internal sealed class SchemaAuthorizationPolicyValidator(IServiceProvider serviceProvider)
    : StartupGate
{
    /// <summary>Schema name registered by Trax for the GraphQL endpoint.</summary>
    private const string TraxSchemaName = "trax";

    protected override async Task CheckAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var executor = await scope
            .ServiceProvider.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync(TraxSchemaName, cancellationToken);

        var named = PoliciesByLocation(executor.Schema);
        if (named.Count == 0)
            return;

        var policyProvider = scope.ServiceProvider.GetService<IAuthorizationPolicyProvider>();
        var missing = new List<string>();
        foreach (var (policy, locations) in named)
            if (policyProvider is null || await policyProvider.GetPolicyAsync(policy) is null)
                missing.Add(
                    $"'{policy}', named by @authorize on {string.Join(", ", locations.Order(StringComparer.Ordinal))}"
                );

        if (missing.Count == 0)
            return;

        throw new InvalidOperationException(
            $"{missing.Count} authorization polic{(missing.Count == 1 ? "y is" : "ies are")} "
                + "named in the GraphQL schema but not registered:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, missing.Select(m => "  " + m))
                + Environment.NewLine
                + "Register each with services.AddAuthorization(o => o.AddPolicy(\"<name>\", ...)), "
                + "or correct the name in [TraxAuthorize(Policy = ...)] or GateOperations(policy: ...). "
                + "A caller of a field gated by a missing policy could never be admitted."
        );
    }

    /// <summary>
    /// Every policy an <c>@authorize</c> directive on an object or interface type, or on one of
    /// its fields, names, with the coordinates that name it.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> PoliciesByLocation(ISchemaDefinition schema)
    {
        var named = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void Collect(IEnumerable<IDirective> directives, string location)
        {
            foreach (var directive in directives)
            {
                if (directive.Definition.Name != "authorize")
                    continue;
                var policy = directive.ToValue<AuthorizeDirective>().Policy;
                if (string.IsNullOrWhiteSpace(policy))
                    continue;
                if (!named.TryGetValue(policy, out var locations))
                    named[policy] = locations = new HashSet<string>(StringComparer.Ordinal);
                locations.Add(location);
            }
        }

        foreach (var type in schema.Types)
        {
            switch (type)
            {
                case ObjectType objectType:
                    Collect(objectType.Directives, objectType.Name);
                    foreach (var field in objectType.Fields)
                        Collect(field.Directives, $"{objectType.Name}.{field.Name}");
                    break;
                case InterfaceType interfaceType:
                    Collect(interfaceType.Directives, interfaceType.Name);
                    foreach (var field in interfaceType.Fields)
                        Collect(field.Directives, $"{interfaceType.Name}.{field.Name}");
                    break;
            }
        }

        return named;
    }
}
