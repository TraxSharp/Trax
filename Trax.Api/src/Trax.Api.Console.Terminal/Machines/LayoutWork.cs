using System.Runtime.InteropServices;
using System.Text;

namespace Trax.Api.Console.Terminal.Machines;

/// <summary>
/// Lays a machine out as boxes and arrows in text. States go in columns by how far they are from the initial state
/// (the longest way there that does not go back), ordered in each column to keep lines short; each transition is
/// routed as box-drawing lines around the boxes, crossing where it must, with its label on its first straight run
/// and an arrowhead where it enters its target. A transition back to an earlier column is routed the same way, so it
/// runs around the boxes. A transition to the same state is written inside the box. The current state has a double
/// border. Laid out vertically, the columns are rows instead, top to bottom, and lines leave downward and enter from
/// above; a label with no horizontal stretch of line to sit on is written beside the line.
/// A machine too big to draw in a terminal is listed instead, state by state with its transitions; a transition to a
/// state the machine does not declare, or a second state of one name, is said under the graph.
/// </summary>
/// <summary>
/// How much routing one drawing took: the search steps of all its lines together, out of the budget they share, and
/// the cells their search windows covered.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct LayoutWork(long Steps, long CellsSearched, long Budget);
