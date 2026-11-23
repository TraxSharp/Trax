using System.Globalization;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Trax.Api.Console.Terminal.Lists;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>Where a list screen is: its filters, the page it is on and the pages before it.</summary>
internal sealed record ListState(
    IReadOnlyDictionary<string, string> Filters,
    long? After,
    IReadOnlyList<long?> Back
);
