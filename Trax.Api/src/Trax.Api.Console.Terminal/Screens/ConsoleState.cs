using System.Collections.ObjectModel;
using System.Globalization;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>Where the console was: its tab and each list's page and filters.</summary>
internal sealed record ConsoleState(int Tab, IReadOnlyDictionary<string, ListState> Lists);
