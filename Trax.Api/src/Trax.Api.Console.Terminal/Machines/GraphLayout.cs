using System.Text;

namespace Trax.Api.Console.Terminal.Machines;

/// <summary>A state as the graph draws it.</summary>
/// <param name="Name">The state's name, written in its box.</param>
/// <param name="Initial">Drawn with the entry arrow.</param>
/// <param name="Final">Drawn with rounded corners.</param>
/// <param name="Note">What the legend says of it (invokes a train, spawns a machine), or null.</param>
internal sealed record GraphState(string Name, bool Initial, bool Final, string? Note = null);

/// <summary>A transition as the graph draws it: a line from one box to another, its label written on the line.</summary>
internal sealed record GraphEdge(string From, string To, string Label);

/// <summary>
/// Lays a machine out as boxes and arrows in text. States go in columns by how far they are from the initial state
/// (the longest way there that does not go back), ordered in each column to keep lines short; each transition is
/// routed as box-drawing lines around the boxes, crossing where it must, with its label on its first straight run
/// and an arrowhead where it enters its target. A transition back to an earlier column is routed the same way, so it
/// runs around the boxes. A transition to the same state is written inside the box. The current state has a double
/// border. Laid out vertically, the columns are rows instead, top to bottom, and lines leave downward and enter from
/// above; a label with no horizontal stretch of line to sit on is written beside the line.
/// </summary>
internal static class GraphLayout
{
    private const int N = 1,
        E = 2,
        S = 4,
        W = 8;
    private static readonly int[] Dx = [0, 1, 0, -1];
    private static readonly int[] Dy = [-1, 0, 1, 0];
    private static readonly int[] Bit = [N, E, S, W];

    // Rows above and below the boxes, and columns before the first, kept free for lines that go around.
    private const int Margin = 2;
    private const int Lead = 4;
    private const int RowGap = 2;
    private const int MinColumnGap = 8;

    /// <summary>The graph's lines, then a blank line and the legend.</summary>
    public static IReadOnlyList<string> Render(
        IReadOnlyList<GraphState> states,
        IReadOnlyList<GraphEdge> edges,
        string? current = null,
        bool vertical = false
    )
    {
        if (states.Count == 0)
            return ["(no states)"];
        var index = states
            .Select((s, i) => (s.Name, i))
            .ToDictionary(p => p.Name, p => p.i, StringComparer.Ordinal);
        var known = edges.Where(e => index.ContainsKey(e.From) && index.ContainsKey(e.To)).ToList();

        // A transition every state takes the same way (StartOver from anywhere) is said once under the graph, not
        // drawn from each.
        var everywhere = known
            .Where(e => e.From != e.To)
            .GroupBy(e => (e.Label, e.To))
            .Where(g => g.Count() >= FanIn)
            .ToList();
        var said = everywhere.SelectMany(g => g).ToHashSet();
        // Several transitions between the same two states are one line with their labels joined.
        var lines = known
            .Where(e => e.From != e.To && !said.Contains(e))
            .GroupBy(e => (e.From, e.To))
            .Select(g => new GraphEdge(
                g.Key.From,
                g.Key.To,
                Join(g.Select(e => e.Label).Distinct().ToList())
            ))
            .ToList();
        var loops = known
            .Where(e => e.From == e.To)
            .GroupBy(e => e.From)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Label).Distinct().ToList(),
                StringComparer.Ordinal
            );

        var layers = Layers(states, lines, index);
        var columns = Order(layers, lines, index, states.Count);
        int width,
            height;
        var boxes = vertical
            ? PlaceDown(states, columns, lines, loops, out width, out height)
            : Place(states, columns, lines, loops, index, out width, out height);

        var canvas = new Canvas(width, height, vertical);
        foreach (var box in boxes)
            canvas.Box(box, box.State.Name == current, loops.GetValueOrDefault(box.State.Name));
        var initial = boxes.FirstOrDefault(b => b.State.Initial);
        if (initial is not null && (vertical ? initial.Y >= 2 : initial.X >= 3))
            canvas.Entry(initial);

        var unplaced = new List<string>();
        var routes = new List<(GraphEdge Edge, List<(int X, int Y)> Path)>();
        // The cells of the lines already drawn into each state, which a later line into it may join.
        var trunks = new Dictionary<string, HashSet<(int X, int Y)>>(StringComparer.Ordinal);
        // Short lines first, so the long ones go around what is already drawn.
        foreach (
            var edge in lines.OrderBy(e =>
                Math.Abs(boxes[index[e.To]].Column - boxes[index[e.From]].Column)
            )
        )
        {
            if (!trunks.TryGetValue(edge.To, out var trunk))
                trunks[edge.To] = trunk = [];
            var path = canvas.Route(boxes[index[edge.From]], boxes[index[edge.To]], trunk);
            if (path is null)
                unplaced.Add($"{edge.From} ──▶ {edge.To}: {edge.Label}");
            else
            {
                routes.Add((edge, path));
                // A later line joins this one anywhere but at its arrowhead.
                trunk.UnionWith(path.Take(path.Count - 1));
            }
        }
        foreach (var (edge, path) in routes)
            if (!canvas.Label(path, edge.Label))
                unplaced.Add($"{edge.From} ──▶ {edge.To}: {edge.Label}");

        // The margins kept free for routing are dropped where no line used them.
        var output = canvas
            .Lines()
            .SkipWhile(l => l.Length == 0)
            .Reverse()
            .SkipWhile(l => l.Length == 0)
            .Reverse()
            .ToList();
        var legend = new List<string>();
        legend.AddRange(
            everywhere.Select(g =>
                $"{g.Key.Label} ──▶ {g.Key.To}  from {string.Join(", ", g.Select(e => e.From))}"
            )
        );
        legend.AddRange(unplaced);
        legend.AddRange(states.Where(s => s.Note is not null).Select(s => $"{s.Name}: {s.Note}"));
        if (legend.Count > 0)
        {
            output.Add("");
            output.AddRange(legend);
        }
        return output;
    }

    /// <summary>How many states take a transition the same way before it is said once rather than drawn from each.</summary>
    private const int FanIn = 3;

    // Labels of one line: "on cancelled, on failed" reads "on cancelled, failed".
    private static string Join(IReadOnlyList<string> labels) =>
        labels.Count > 1 && labels.All(l => l.StartsWith("on ", StringComparison.Ordinal))
            ? "on " + string.Join(", ", labels.Select(l => l[3..]))
            : string.Join(", ", labels);

    // The column of each state: the longest way to it from the initial state along transitions that do not go back.
    // A state no transition reaches starts a column of its own after the others.
    private static int[] Layers(
        IReadOnlyList<GraphState> states,
        List<GraphEdge> lines,
        Dictionary<string, int> index
    )
    {
        var n = states.Count;
        var outgoing = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        foreach (var e in lines)
            outgoing[index[e.From]].Add(index[e.To]);

        // A transition into a state still on the walk's path goes back: it is left out of the layering.
        var back = new HashSet<(int, int)>();
        var mark = new int[n]; // 0 unseen, 1 on the path, 2 done
        void Walk(int v)
        {
            mark[v] = 1;
            foreach (var w in outgoing[v])
                if (mark[w] == 1)
                    back.Add((v, w));
                else if (mark[w] == 0)
                    Walk(w);
            mark[v] = 2;
        }
        var start = states.ToList().FindIndex(s => s.Initial);
        if (start >= 0)
            Walk(start);
        for (var v = 0; v < n; v++)
            if (mark[v] == 0)
                Walk(v);

        var layer = new int[n];
        var incoming = new int[n];
        foreach (var (v, list) in outgoing.Select((l, v) => (v, l)))
        foreach (var w in list)
            if (!back.Contains((v, w)))
                incoming[w]++;
        var ready = new Queue<int>(
            Enumerable
                .Range(0, n)
                .Where(v => incoming[v] == 0)
                .OrderBy(v => v == start ? 0 : 1)
                .ThenBy(v => v)
        );
        while (ready.Count > 0)
        {
            var v = ready.Dequeue();
            foreach (var w in outgoing[v])
            {
                if (back.Contains((v, w)))
                    continue;
                layer[w] = Math.Max(layer[w], layer[v] + 1);
                if (--incoming[w] == 0)
                    ready.Enqueue(w);
            }
        }
        // A state with nothing leading to it but not the initial one goes after the initial state's column.
        if (start >= 0)
            for (var v = 0; v < n; v++)
                if (v != start && layer[v] == 0 && lines.All(e => index[e.To] != v))
                    layer[v] = 1;
        return layer;
    }

    // The states of each column, ordered by the mean position of their neighbours in the column before and after,
    // a few times over, so lines cross as little as this cheaply manages.
    private static List<List<int>> Order(
        int[] layer,
        List<GraphEdge> lines,
        Dictionary<string, int> index,
        int n
    )
    {
        var columns = Enumerable
            .Range(0, layer.Max() + 1)
            .Select(c => Enumerable.Range(0, n).Where(v => layer[v] == c).ToList())
            .ToList();
        var neighbours = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        foreach (var e in lines)
        {
            neighbours[index[e.From]].Add(index[e.To]);
            neighbours[index[e.To]].Add(index[e.From]);
        }
        for (var pass = 0; pass < 6; pass++)
        {
            var position = new double[n];
            foreach (var column in columns)
                for (var i = 0; i < column.Count; i++)
                    position[column[i]] = i;
            var order = pass % 2 == 0 ? columns : Enumerable.Reverse(columns);
            foreach (var column in order)
            {
                var keyed = column
                    .Select(
                        (v, i) =>
                        {
                            var near = neighbours[v]
                                .Where(w => Math.Abs(layer[w] - layer[v]) == 1)
                                .ToList();
                            return (
                                v,
                                key: near.Count == 0 ? i : near.Average(w => position[w]),
                                i
                            );
                        }
                    )
                    .OrderBy(p => p.key)
                    .ThenBy(p => p.i)
                    .Select(p => p.v)
                    .ToList();
                column.Clear();
                column.AddRange(keyed);
                for (var i = 0; i < column.Count; i++)
                    position[column[i]] = i;
            }
        }
        return columns;
    }

    private static int ColumnOf(List<List<int>> columns, int v) =>
        columns.FindIndex(c => c.Contains(v));

    private sealed record Placed(GraphState State, int Column, int X, int Y, int Width, int Height)
    {
        public int Mid => Y + 1;

        public int MidX => X + Width / 2;
    }

    private static int BoxWidth(GraphState s, Dictionary<string, List<string>> loops) =>
        Math.Max(s.Name.Length, loops.TryGetValue(s.Name, out var l) ? l.Max(x => x.Length) + 2 : 0)
        + 4;

    private static int BoxHeight(GraphState s, Dictionary<string, List<string>> loops) =>
        3 + (loops.TryGetValue(s.Name, out var l) ? l.Count : 0);

    // Where each box goes laid out vertically: the columns become rows, top to bottom, each row centred on the widest,
    // the boxes in a row far enough apart for a label written beside a line between them.
    private static List<Placed> PlaceDown(
        IReadOnlyList<GraphState> states,
        List<List<int>> rows,
        List<GraphEdge> lines,
        Dictionary<string, List<string>> loops,
        out int width,
        out int height
    )
    {
        var longest = lines.Select(e => e.Label.Length).DefaultIfEmpty(0).Max();
        var gap = Math.Max(MinColumnGap, longest + 6);
        // A line back up runs down a side, with its label beside it: the sides are as wide as the longest label.
        var side = Math.Max(Lead, longest + 4);
        var rowWidth = rows.Select(r =>
                r.Sum(v => BoxWidth(states[v], loops)) + gap * Math.Max(0, r.Count - 1)
            )
            .ToList();
        var widest = rowWidth.Count == 0 ? 0 : rowWidth.Max();
        var placed = new Placed[states.Count];
        var y = Margin;
        for (var r = 0; r < rows.Count; r++)
        {
            var rowHeight = rows[r].Count == 0 ? 0 : rows[r].Max(v => BoxHeight(states[v], loops));
            var x = side + (widest - rowWidth[r]) / 2;
            foreach (var v in rows[r])
            {
                var w = BoxWidth(states[v], loops);
                placed[v] = new Placed(states[v], r, x, y, w, BoxHeight(states[v], loops));
                x += w + gap;
            }
            y += rowHeight + RowsApart;
        }
        width = side + widest + side;
        height = y - RowsApart + Margin;
        return [.. placed];
    }

    // Rows between rows of boxes laid out vertically: room for a line to turn twice between them.
    private const int RowsApart = 4;

    // Where each box goes: columns left to right, a gap after each wide enough for the labels of the lines leaving
    // it, each column centred on the tallest.
    private static List<Placed> Place(
        IReadOnlyList<GraphState> states,
        List<List<int>> columns,
        List<GraphEdge> lines,
        Dictionary<string, List<string>> loops,
        Dictionary<string, int> index,
        out int width,
        out int height
    )
    {
        int BoxWidth(GraphState s) => GraphLayout.BoxWidth(s, loops);
        int BoxHeight(GraphState s) => GraphLayout.BoxHeight(s, loops);

        var columnHeight = columns
            .Select(c => c.Sum(v => BoxHeight(states[v])) + RowGap * Math.Max(0, c.Count - 1))
            .ToList();
        var tallest = columnHeight.Count == 0 ? 0 : columnHeight.Max();
        var placed = new Placed[states.Count];
        var x = Lead;
        for (var c = 0; c < columns.Count; c++)
        {
            var columnWidth = columns[c].Count == 0 ? 0 : columns[c].Max(v => BoxWidth(states[v]));
            var y = Margin + (tallest - columnHeight[c]) / 2;
            foreach (var v in columns[c])
            {
                var w = BoxWidth(states[v]);
                placed[v] = new Placed(
                    states[v],
                    c,
                    x + (columnWidth - w) / 2,
                    y,
                    w,
                    BoxHeight(states[v])
                );
                y += placed[v].Height + RowGap;
            }
            // Wide enough for the label of any line that starts its run across this gap, whichever way it goes.
            var labels = lines
                .Where(e =>
                    Math.Min(ColumnOf(columns, index[e.From]), ColumnOf(columns, index[e.To])) == c
                )
                .Select(e => e.Label.Length)
                .DefaultIfEmpty(0)
                .Max();
            x += columnWidth + Math.Max(MinColumnGap, labels + 6);
        }
        width = x + Margin;
        height = tallest + 2 * Margin;
        return [.. placed];
    }

    // A grid of cells: box text, and the line segments each cell joins, which make its glyph.
    private sealed class Canvas(int width, int height, bool vertical)
    {
        private readonly char?[,] _text = new char?[width, height];
        private readonly int[,] _mask = new int[width, height];
        private readonly int[,] _uses = new int[width, height];
        private readonly bool[,] _blocked = new bool[width, height];
        private readonly bool[,] _fixed = new bool[width, height];

        public void Box(Placed box, bool current, List<string>? loops)
        {
            var (h, v, tl, tr, bl, br) =
                current ? ('═', '║', '╔', '╗', '╚', '╝')
                : box.State.Final ? ('─', '│', '╭', '╮', '╰', '╯')
                : ('─', '│', '┌', '┐', '└', '┘');
            for (var x = box.X; x < box.X + box.Width; x++)
            for (var y = box.Y; y < box.Y + box.Height; y++)
            {
                _blocked[x, y] = true;
                var left = x == box.X;
                var right = x == box.X + box.Width - 1;
                var top = y == box.Y;
                var bottom = y == box.Y + box.Height - 1;
                _text[x, y] =
                    top && left ? tl
                    : top && right ? tr
                    : bottom && left ? bl
                    : bottom && right ? br
                    : top || bottom ? h
                    : left || right ? v
                    : ' ';
            }
            Write(box.X + 2, box.Y + 1, box.State.Name);
            for (var i = 0; i < (loops?.Count ?? 0); i++)
                Write(box.X + 2, box.Y + 2 + i, $"↻ {loops![i]}");
        }

        // The initial state's entry: a dot and an arrow into its left side.
        public void Entry(Placed box)
        {
            if (vertical)
            {
                _text[box.MidX, box.Y - 2] = '●';
                _text[box.MidX, box.Y - 1] = '▼';
                _blocked[box.MidX, box.Y - 2] = _blocked[box.MidX, box.Y - 1] = true;
                return;
            }
            Write(box.X - 3, box.Mid, "●─▶");
            for (var x = box.X - 3; x < box.X; x++)
                _blocked[x, box.Mid] = true;
        }

        private void Write(int x, int y, string text)
        {
            for (var i = 0; i < text.Length && x + i < width; i++)
                _text[x + i, y] = text[i];
        }

        /// <summary>
        /// The cheapest line from a side of <paramref name="from"/> to a side of <paramref name="to"/>, drawn: each
        /// step costs one, a turn more, running beside a box a little more, crossing another line more again, and
        /// running along one most of all. Leaving to the right and entering from the left cost least.
        /// </summary>
        public List<(int X, int Y)>? Route(
            Placed from,
            Placed to,
            IReadOnlySet<(int X, int Y)> trunk
        )
        {
            var starts = Ports(from)
                .Select(p => (p.X, p.Y, Dir: p.Out, Cost: p.SideCost + Facing(from, to, p.Out)))
                .ToList();
            var ends = Ports(to)
                .ToDictionary(
                    p => (p.X, p.Y),
                    p => (In: (p.Out + 2) % 4, Cost: EndCost(p.Out) + Facing(to, from, p.Out))
                );
            var dist = new int[width, height, 4];
            var prev = new (int X, int Y, int D)[width, height, 4];
            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
            for (var d = 0; d < 4; d++)
                dist[x, y, d] = int.MaxValue;
            var queue = new PriorityQueue<(int X, int Y, int D), int>();
            foreach (var s in starts)
            {
                if (!Free(s.X, s.Y))
                    continue;
                var cost = s.Cost + Penalty(s.X, s.Y, s.Dir);
                if (cost < dist[s.X, s.Y, s.Dir])
                {
                    dist[s.X, s.Y, s.Dir] = cost;
                    prev[s.X, s.Y, s.Dir] = (-1, -1, -1);
                    queue.Enqueue((s.X, s.Y, s.Dir), cost);
                }
            }
            (int X, int Y, int D)? best = null;
            (int X, int Y, int D)? join = null;
            var bestCost = int.MaxValue;
            while (queue.TryDequeue(out var at, out var cost))
            {
                if (cost > dist[at.X, at.Y, at.D] || cost >= bestCost)
                    continue;
                // The arrowhead's cell is reached heading into the box, so the arrow points the way the line runs.
                if (ends.TryGetValue((at.X, at.Y), out var end) && at.D == end.In)
                {
                    var total = cost + end.Cost;
                    if (total < bestCost)
                    {
                        bestCost = total;
                        best = at;
                        join = null;
                    }
                }
                for (var d = 0; d < 4; d++)
                {
                    if (d == (at.D + 2) % 4)
                        continue;
                    var (nx, ny) = (at.X + Dx[d], at.Y + Dy[d]);
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;
                    // Stepping onto a line already drawn into the same state ends here: this one joins it, at a
                    // right angle, so the junction reads.
                    if (
                        trunk.Contains((nx, ny))
                        && !_fixed[nx, ny]
                        && (_mask[nx, ny] & (d is 1 or 3 ? (E | W) : (N | S))) == 0
                        && cost + 1 < bestCost
                    )
                    {
                        bestCost = cost + 1;
                        best = at;
                        join = (nx, ny, d);
                    }
                    if (!Free(nx, ny))
                        continue;
                    // Laid out down, a step across costs more the further it is below the box the line leaves, so lines
                    // leaving one box turn off sideways at once rather than running down side by side, and the one
                    // that goes straight down keeps clear rows for its label.
                    var step =
                        vertical && d is 1 or 3 ? 1 + Math.Max(0, ny - (from.Y + from.Height)) : 1;
                    var next = cost + step + (d == at.D ? 0 : 4) + Penalty(nx, ny, d);
                    if (next < dist[nx, ny, d])
                    {
                        dist[nx, ny, d] = next;
                        prev[nx, ny, d] = at;
                        queue.Enqueue((nx, ny, d), next);
                    }
                }
            }
            if (best is not { } last)
                return null;

            var path = new List<(int X, int Y, int D)>();
            for (var p = last; p.X >= 0; p = prev[p.X, p.Y, p.D])
                path.Add(p);
            path.Reverse();
            if (join is { } j)
            {
                // The trunk cell takes the joining line's end; no arrowhead, the trunk has one.
                path.Add(j);
                Draw(path, from, into: null);
            }
            else
                Draw(path, from, ends[(last.X, last.Y)].In);
            return path.Select(p => (p.X, p.Y)).ToList();
        }

        private void Draw(List<(int X, int Y, int D)> path, Placed from, int? into)
        {
            // Where the line leaves its box, the box's border joins it.
            var first = path[0];
            var outward = first.D;
            var (bx, by) = (first.X - Dx[outward], first.Y - Dy[outward]);
            if (
                bx >= from.X
                && bx < from.X + from.Width
                && by >= from.Y
                && by < from.Y + from.Height
            )
                _text[bx, by] = Junction(_text[bx, by] ?? ' ', outward);
            _mask[first.X, first.Y] |= Bit[(outward + 2) % 4];
            for (var i = 0; i < path.Count - 1; i++)
            {
                var d = path[i + 1].D;
                _mask[path[i].X, path[i].Y] |= Bit[d];
                _mask[path[i + 1].X, path[i + 1].Y] |= Bit[(d + 2) % 4];
            }
            foreach (var p in path)
                _uses[p.X, p.Y]++;
            _fixed[first.X, first.Y] = true;
            if (into is not { } inward)
                return;
            var end = path[^1];
            _text[end.X, end.Y] = "▲▶▼◀"[inward];
            _fixed[end.X, end.Y] = true;
            _blocked[end.X, end.Y] = true;
        }

        private static char Junction(char border, int outward) =>
            (border, outward) switch
            {
                ('│', 1) => '├',
                ('│', 3) => '┤',
                ('─', 0) => '┴',
                ('─', 2) => '┬',
                ('║', 1) => '╟',
                ('║', 3) => '╢',
                ('═', 0) => '╧',
                ('═', 2) => '╤',
                _ => border,
            };

        // The cells just outside a box's sides a line may leave or enter by, with the way out and what it costs.
        private IEnumerable<(int X, int Y, int Out, int SideCost)> Ports(Placed box)
        {
            // Laid out across, a line leaves by the right and the middle row costs least; laid out down, it leaves by
            // the bottom and the middle column costs least.
            for (var y = box.Y + 1; y < box.Y + box.Height - 1; y++)
            {
                yield return (
                    box.X + box.Width,
                    y,
                    1,
                    vertical ? 4
                    : y == box.Mid ? 0
                    : 2
                );
                yield return (
                    box.X - 1,
                    y,
                    3,
                    vertical ? 4
                    : y == box.Mid ? 6
                    : 8
                );
            }
            for (var x = box.X + 1; x < box.X + box.Width - 1; x++)
            {
                yield return (x, box.Y - 1, 0, vertical ? (x == box.MidX ? 6 : 8) : 4);
                yield return (x, box.Y + box.Height, 2, vertical ? (x == box.MidX ? 0 : 2) : 4);
            }
        }

        // What a side costs for a line between this box and another: the side facing the other box costs nothing more,
        // a side facing away costs more, so a line to a box above leaves by the top rather than crossing others.
        private static int Facing(Placed box, Placed other, int side)
        {
            var above = other.Y + other.Height <= box.Y;
            var below = other.Y >= box.Y + box.Height;
            return side switch
            {
                0 => above ? -3 : 3,
                2 => below ? -3 : 3,
                _ => above || below ? 2 : 0,
            };
        }

        // Entering by the left side is cheapest, or the top laid out down; a line that leaves a side "out" enters it
        // the other way.
        private int EndCost(int outward) =>
            vertical
                ? outward switch
                {
                    0 => 0,
                    1 or 3 => 3,
                    _ => 6,
                }
                : outward switch
                {
                    3 => 0,
                    0 or 2 => 3,
                    _ => 6,
                };

        private bool Free(int x, int y) => !_blocked[x, y] && !_fixed[x, y];

        private int Penalty(int x, int y, int d)
        {
            var penalty = 0;
            var mask = _mask[x, y];
            if (mask != 0)
            {
                var along = d is 1 or 3 ? (E | W) : (N | S);
                penalty += (mask & along) != 0 ? 40 : 6;
            }
            for (var k = 0; k < 4; k++)
            {
                var (nx, ny) = (x + Dx[k], y + Dy[k]);
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && _blocked[nx, ny])
                    penalty += 1;
            }
            return penalty;
        }

        /// <summary>Writes <paramref name="label"/> on the first straight horizontal run of the line long enough for it.</summary>
        public bool Label(List<(int X, int Y)> path, string label)
        {
            var need = label.Length + 2;
            var run = new List<(int X, int Y)>();
            void Flush(List<(int X, int Y)> r, ref bool done)
            {
                if (done || r.Count < need)
                    return;
                // A line running left is walked right to left; its label still reads left to right.
                r = [.. r.OrderBy(c => c.X)];
                var start = (r.Count - label.Length) / 2;
                for (var i = 0; i < label.Length; i++)
                {
                    var (x, y) = r[start + i];
                    _text[x, y] = label[i];
                    _fixed[x, y] = true;
                }
                done = true;
            }
            var placed = false;
            foreach (var p in path)
            {
                var plain =
                    _mask[p.X, p.Y] == (E | W)
                    && _uses[p.X, p.Y] == 1
                    && !_fixed[p.X, p.Y]
                    && _text[p.X, p.Y] is null;
                if (plain && (run.Count == 0 || run[^1].Y == p.Y))
                    run.Add(p);
                else
                {
                    Flush(run, ref placed);
                    run = plain ? [p] : [];
                }
            }
            Flush(run, ref placed);
            return placed || Beside(path, label) || Across(path, label);
        }

        // Writes the label beside the middle of a straight vertical stretch of the line, to its right if there is room,
        // else to its left: a line laid out down often has no horizontal stretch long enough.
        private bool Beside(List<(int X, int Y)> path, string label)
        {
            var upright = path.Where(p =>
                    _mask[p.X, p.Y] == (N | S) && _uses[p.X, p.Y] == 1 && !_fixed[p.X, p.Y]
                )
                .ToList();
            foreach (
                var (x, y) in upright
                    .Skip(upright.Count / 2)
                    .Concat(upright.Take(upright.Count / 2))
            )
            foreach (var start in new[] { x + 2, x - 1 - label.Length })
                if (Clear(start - 1, y, label.Length + 2))
                {
                    Write(start, y, label);
                    for (var i = 0; i < label.Length; i++)
                        _fixed[start + i, y] = _blocked[start + i, y] = true;
                    return true;
                }
            return false;
        }

        // The last resort for a short vertical line hemmed in by others: the label is written across it, centred on it,
        // where nothing else is on that row.
        private bool Across(List<(int X, int Y)> path, string label)
        {
            var upright = path.Where(p =>
                    _mask[p.X, p.Y] == (N | S) && _uses[p.X, p.Y] == 1 && !_fixed[p.X, p.Y]
                )
                .ToList();
            foreach (
                var (x, y) in upright
                    .Skip(upright.Count / 2)
                    .Concat(upright.Take(upright.Count / 2))
            )
            {
                var start = x - label.Length / 2;
                var clear =
                    start >= 1
                    && start + label.Length < width
                    && Enumerable
                        .Range(start - 1, label.Length + 2)
                        .All(i =>
                            i == x || (_mask[i, y] == 0 && _text[i, y] is null && !_blocked[i, y])
                        );
                if (!clear)
                    continue;
                Write(start, y, label);
                for (var i = 0; i < label.Length; i++)
                    _fixed[start + i, y] = _blocked[start + i, y] = true;
                return true;
            }
            return false;
        }

        private bool Clear(int x, int y, int length)
        {
            if (x < 0 || x + length > width)
                return false;
            for (var i = x; i < x + length; i++)
                if (_mask[i, y] != 0 || _text[i, y] is not null || _blocked[i, y])
                    return false;
            return true;
        }

        public IEnumerable<string> Lines()
        {
            for (var y = 0; y < height; y++)
            {
                var sb = new StringBuilder(width);
                for (var x = 0; x < width; x++)
                    sb.Append(_text[x, y] ?? Glyph(_mask[x, y]));
                var line = sb.ToString().TrimEnd();
                yield return line;
            }
        }

        private static char Glyph(int mask) =>
            mask switch
            {
                0 => ' ',
                E or W or (E | W) => '─',
                N or S or (N | S) => '│',
                E | S => '┌',
                W | S => '┐',
                N | E => '└',
                N | W => '┘',
                N | E | S => '├',
                N | W | S => '┤',
                E | W | S => '┬',
                N | E | W => '┴',
                _ => '┼',
            };
    }
}
