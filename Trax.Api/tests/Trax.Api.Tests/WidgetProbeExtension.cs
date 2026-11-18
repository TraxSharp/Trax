using System.Globalization;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

[ExtendObjectType(typeof(Widget))]
public sealed class WidgetProbeExtension
{
    /// <summary>Reads the entity key. Trax must supply it with no annotation here.</summary>
    public int GetEchoId([Parent] Widget widget) => widget.Id;

    /// <summary>Reads a non-key column, declared through HotChocolate's own mechanism.</summary>
    public string GetEchoGadgetId([Parent(requires: nameof(Widget.GadgetId))] Widget widget) =>
        widget.GadgetId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<unset>";

    /// <summary>Reads a non-key column with no declaration: must stay unset.</summary>
    public string GetEchoName([Parent] Widget widget) =>
        string.IsNullOrEmpty(widget.Name) ? "<unset>" : widget.Name;

    /// <summary>Same column, declared: must arrive.</summary>
    public string GetEchoNameRequired([Parent(requires: nameof(Widget.Name))] Widget widget) =>
        string.IsNullOrEmpty(widget.Name) ? "<unset>" : widget.Name;

    /// <summary>
    /// Declares one column and also reads the key. Trax must merge its automatic key
    /// requirement with the declared one instead of replacing either.
    /// </summary>
    public string GetEchoNameAndId([Parent(requires: nameof(Widget.Name))] Widget widget) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(string.IsNullOrEmpty(widget.Name) ? "<unset>" : widget.Name)}/{widget.Id}"
        );
}
