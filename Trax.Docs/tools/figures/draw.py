"""Draws the figures in Trax.Docs/images.

    python3 Trax.Docs/tools/figures/draw.py Trax.Docs/images

One colour per idea, the same in every figure: teal states, lavender moves, amber guards, blue
trains, pink junctions, orange failures. Edit a figure here and rerun; do not edit the SVGs.
"""
import sys, os
from xml.sax.saxutils import escape

OUT = sys.argv[1]
W = 768

BG = "#151515"; CARD = "#1e1e1e"; EDGE = "#2a2a28"; TEXT = "#e4e2de"; MUTED = "#a09d96"; LINE = "#686560"
STATE, STATE_BG = "#5fc9b5", "#12302b"
MOVE = "#b4a7f5"
GUARD, GUARD_BG = "#f2c94c", "#2f2810"
TRAIN, TRAIN_BG = "#7fb0e0", "#16243a"
JUNC, JUNC_BG = "#f2a7c3", "#2e1a24"
FAIL, FAIL_BG = "#e8853d", "#33200f"

SANS = "ui-sans-serif, system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
MONO = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"


class Fig:
    def __init__(self, name, h, title):
        self.name, self.h, self.title, self.parts = name, h, title, []

    def add(self, s):
        self.parts.append(s)

    def rect(self, x, y, w, h, fill=CARD, stroke=EDGE, sw=1.5, rx=8, dash=None):
        d = f' stroke-dasharray="{dash}"' if dash else ""
        self.add(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"{d}/>')

    def text(self, x, y, s, size=14, fill=TEXT, weight=400, anchor="start", mono=False, spacing=0):
        fam = MONO if mono else SANS
        extra = f' text-anchor="{anchor}"' if anchor != "start" else ""
        ls = f";letter-spacing:{spacing}px" if spacing else ""
        self.add(f'<text x="{x}" y="{y}"{extra} style="font-family:{fam};font-size:{size}px;font-weight:{weight};fill:{fill}{ls}">{escape(s)}</text>')

    def label(self, x, y, s, fill=MUTED, anchor="start"):
        self.text(x, y, s.upper(), size=12, fill=fill, anchor=anchor, spacing=1)

    def lines(self, x, y, items, size=13, fill=MUTED, gap=17, anchor="start", mono=False):
        for i, s in enumerate(items):
            self.text(x, y + i * gap, s, size=size, fill=fill, anchor=anchor, mono=mono)

    def arrow(self, d, color=LINE, sw=1.5, dash=None, head=True):
        mid = self.marker(color) if head else None
        m = f' marker-end="url(#{mid})"' if mid else ""
        ds = f' stroke-dasharray="{dash}"' if dash else ""
        self.add(f'<path d="{d}" fill="none" stroke="{color}" stroke-width="{sw}"{ds}{m}/>')

    _markers = None

    def marker(self, color):
        self._markers = self._markers or {}
        mid = "a" + color.strip("#")
        self._markers[mid] = color
        return mid

    def circle(self, cx, cy, r, fill, stroke=None, sw=0, dash=None):
        s = f' stroke="{stroke}" stroke-width="{sw}"' if stroke else ""
        d = f' stroke-dasharray="{dash}"' if dash else ""
        self.add(f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="{fill}"{s}{d}/>')

    def line(self, x1, y1, x2, y2, color, sw, dash=None):
        d = f' stroke-dasharray="{dash}"' if dash else ""
        self.add(f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="{sw}"{d}/>')

    def signal(self, x, ytop):
        # A signal post: head with an amber light, standing on the track at ytop+44.
        self.line(x, ytop + 26, x, ytop + 46, MUTED, 2.5)
        self.rect(x - 8, ytop, 16, 28, fill="#101010", stroke=MUTED, sw=1.2, rx=5)
        self.circle(x, ytop + 8, 4, GUARD)
        self.circle(x, ytop + 19, 4, "#333")

    def train(self, x, y, w=72):
        self.rect(x, y - 11, w, 22, fill=TRAIN, stroke=TRAIN, sw=1, rx=6)
        for i in range(3):
            self.rect(x + 8 + i * ((w - 16) // 3), y - 5, (w - 16) // 3 - 6, 8, fill=BG, stroke=BG, sw=0, rx=2)

    def track(self, d, color=MOVE, dash=None, bed=True):
        if bed:
            self.add(f'<path d="{d}" fill="none" stroke="#262626" stroke-width="18" stroke-dasharray="4 9"/>')
        ds = f' stroke-dasharray="{dash}"' if dash else ""
        self.add(f'<path d="{d}" fill="none" stroke="{color}" stroke-width="3"{ds}/>')

    def station(self, cx, cy, filled=False, color=STATE, dash=None):
        self.circle(cx, cy, 9, color if filled else BG, color, 3, dash)

    def yours(self, x, y, rows, w=None):
        # A tag marking code you write, inside the part Trax runs.
        w = w or 16 + 6.7 * max(len(r) for r in rows)
        self.rect(x, y, w, 6 + 16 * len(rows), fill=GUARD_BG, stroke=GUARD, sw=1.2, rx=5, dash="4 3")
        self.lines(x + 8, y + 15, rows, 12, GUARD, 16, mono=True)

    def write(self):
        defs = ""
        if self._markers:
            defs = "<defs>" + "".join(
                f'<marker id="{mid}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="{c}"/></marker>'
                for mid, c in self._markers.items()) + "</defs>\n"
        body = "\n  ".join(self.parts)
        svg = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {self.h}" width="{W}" height="{self.h}" role="img" aria-labelledby="title">\n'
               f'  <title id="title">{escape(self.title)}</title>\n  {defs}'
               f'<rect width="{W}" height="{self.h}" fill="{BG}"/>\n  {body}\n</svg>\n')
        with open(os.path.join(OUT, self.name + ".svg"), "w") as f:
            f.write(svg)


# ---------------------------------------------------------------- problem
f = Fig("problem", 250, "A web user, a script and an AI agent all reach the same process. The web user skips ahead to payment, the script sends data in the wrong shape, the agent approves its own work. The process's rules usually live in checks spread through the code, or in a prompt an AI may ignore.")
clients = [("A web user", "forms, clicks, back buttons", "skips ahead to payment"),
           ("A script", "integrations, other services", "sends data in the wrong shape"),
           ("An AI agent", "calls tools on its own", "approves its own work")]
for i, (name, sub, bad) in enumerate(clients):
    y = 16 + i * 76
    f.rect(16, y, 200, 64)
    f.text(32, y + 27, name, 15, weight=600)
    f.text(32, y + 48, sub, 13, MUTED)
    f.arrow(f"M216,{y + 32} L494,{y + 32}", FAIL, dash="6 5")
    f.text(232, y + 24, bad, 13, FAIL, mono=True)
f.rect(500, 16, 252, 216, fill=STATE_BG, stroke=STATE, sw=2, rx=10)
f.text(520, 50, "Your process", 18, weight=600)
f.lines(520, 82, ["Its rules usually live in checks", "spread through the code, or in", "instructions in a prompt that an", "AI may not follow."], 13, MUTED, 19)
f.write()

# ---------------------------------------------------------------- traffic light
f = Fig("traffic-light", 300, "A traffic light is a state machine. It is always green, yellow or red. When the timer runs out it moves from green to yellow, yellow to red, and red back to green. Green straight to red is not a move.")
f.rect(16, 16, 480, 268, rx=10)
f.label(32, 40, "Example: a traffic light")
def light(x, y, name, dot):
    f.rect(x, y, 128, 48, fill=STATE_BG, stroke=STATE, sw=2)
    f.circle(x + 24, y + 24, 9, dot, "#101010", 2)
    f.text(x + 44, y + 29, name, 15, weight=600)
light(40, 64, "Green", "#3e9b4f")
light(344, 64, "Yellow", "#f2c94c")
light(192, 212, "Red", "#d2483e")
f.arrow("M168,88 L342,88", MOVE, 2)
f.text(255, 80, "timer", 12, MOVE, anchor="middle", mono=True)
f.arrow("M408,112 C408,180 380,236 322,236", MOVE, 2)
f.text(410, 180, "timer", 12, MOVE, mono=True)
f.arrow("M190,236 C130,236 104,180 104,114", MOVE, 2)
f.text(98, 180, "timer", 12, MOVE, anchor="end", mono=True)
f.rect(176, 132, 160, 52, fill=GUARD_BG, stroke=GUARD, sw=1.5, dash="5 4")
f.text(256, 154, "Green straight to red?", 12, TEXT, anchor="middle")
f.text(256, 172, "Not a move.", 12, GUARD, weight=600, anchor="middle")
for i, (head, color, ls) in enumerate([("States", STATE, ["Green, yellow or red.", "Always exactly one."]),
                                       ("Moves", MOVE, ["Something happens:", "the timer runs out."]),
                                       ("Rules", GUARD, ["Which moves each state", "allows, and where to."])]):
    y = 16 + i * 92
    f.rect(512, y, 240, 84)
    f.add(f'<rect x="512" y="{y}" width="5" height="84" fill="{color}"/>')
    f.text(530, y + 26, head, 15, color, 600)
    f.lines(530, y + 48, ls, 13, MUTED, 17)
f.write()

# ---------------------------------------------------------------- railway
f = Fig("railway", 302, "The railway picture. Stations Cart, Address, Payment, Placing and Placed sit on a track. A signal stands before Placing. The PlaceOrder train has left Placing and passes junctions on its way to Placed. Stations are states, tracks are moves, signals are guards, trains are the work, junctions are its steps.")
Y = 92
f.track(f"M0,{Y} L{W},{Y}")
xs = [("Cart", 40), ("Address", 170), ("Payment", 300), ("Placing", 450), ("Placed", 724)]
f.signal(420, Y - 70)
f.text(406, Y - 54, "signal = guard", 12, GUARD, anchor="end", mono=True)
for x in (610, 650, 690):
    f.line(x, Y - 12, x, Y + 12, JUNC, 4)
f.train(500, Y, 80)
f.text(540, Y - 22, "PlaceOrder train", 12, TRAIN, anchor="middle", mono=True)
f.text(650, Y + 32, "junctions", 12, JUNC, anchor="middle", mono=True)
f.text(105, Y - 18, "track = move", 12, MOVE, anchor="middle", mono=True)
for name, x in xs:
    f.station(x, Y, filled=(name == "Placed"))
    f.text(x, Y + 32, name, 13, STATE if name != "Placed" else TEXT, 600 if name == "Placed" else 400, "middle")
cards = [("Stations", "States", STATE, ["Where the process", "waits for someone."]),
         ("Tracks", "Moves", MOVE, ["The only ways from", "station to station."]),
         ("Signals", "Guards", GUARD, ["Checks a move must", "pass first."]),
         ("Trains", "The work", TRAIN, ["Runs without", "stopping to the", "next station."]),
         ("Junctions", "Steps", JUNC, ["The points a train", "passes on its way."])]
for i, (a, b, color, ls) in enumerate(cards):
    x = 16 + i * 148
    f.rect(x, 168, 144, 118)
    f.add(f'<rect x="{x}" y="168" width="144" height="5" rx="2" fill="{color}"/>')
    f.label(x + 12, 194, a, color)
    f.text(x + 12, 218, "= " + b, 15, weight=600)
    f.lines(x + 12, 242, ls, 12, MUTED, 16)
f.write()

# ---------------------------------------------------------------- checkout map
f = Fig("checkout-map", 300, "The book's checkout drawn as a railway map. The main line runs Cart, Address, Payment, Placing, Placed, with the moves SetAddress, ChoosePayment and PlaceOrder between them and a guard before each. Placing runs the PlaceOrder train; if it fails the checkout goes to Failed, and Retry goes back to Placing. The customer is at Address.")
Y = 110
f.track(f"M24,{Y} L744,{Y}")
f.track(f"M478,{Y + 12} L478,{Y + 118}", FAIL)
f.track(f"M500,{Y + 118} L500,{Y + 12}", MOVE, dash="8 6", bed=False)
st = [("Cart", 50), ("Address", 200), ("Payment", 340), ("Placing", 490), ("Placed", 724)]
moves = [("SetAddress", 125), ("ChoosePayment", 270), ("PlaceOrder", 415)]
for name, x in moves:
    f.text(x - 8, Y - 14, name, 12, MOVE, anchor="middle", mono=True)
for x in (180, 320, 468):
    f.signal(x, Y - 90)
f.text(16, 24, "signals = guards", 12, GUARD, mono=True)
f.circle(200, Y, 17, "none", STATE, 1.5)
f.text(200, Y + 54, "you are here", 12, STATE, anchor="middle")
f.train(560, Y, 72)
f.text(596, Y - 22, "PlaceOrder", 12, TRAIN, anchor="middle", mono=True)
for x in (652, 680):
    f.line(x, Y - 10, x, Y + 10, JUNC, 4)
for name, x in st:
    f.station(x, Y, filled=name in ("Placed", "Address"))
    f.text(x if name != "Placing" else 466, Y + 34, name, 13, STATE if name != "Placed" else TEXT, 600 if name == "Placed" else 400, "middle" if name != "Placing" else "end")
f.text(470, Y + 74, "run failed", 12, FAIL, anchor="end", mono=True)
f.text(508, Y + 74, "Retry", 12, MOVE, mono=True)
f.station(489, Y + 130, color=FAIL, dash="4 3")
f.text(510, Y + 135, "Failed", 13, FAIL)
f.text(16, 288, "Anything not drawn here is not a move, and the server refuses it.", 13, MUTED)
f.write()

# ---------------------------------------------------------------- place order train
f = Fig("place-order-train", 306, "The PlaceOrder train runs four junctions in order: PriceCart, ReserveStock, TakePayment and ConfirmOrder. When all succeed the caller gets the order. When ReserveStock fails because an item is out of stock, TakePayment and ConfirmOrder are skipped, no card is charged, and the caller gets the exception. Each junction is code you write; Trax runs them in order, skips the rest when one fails, and records the run.")
names = ["PriceCart", "ReserveStock", "TakePayment", "ConfirmOrder"]
def row(y, failed_at):
    for i, n in enumerate(names):
        x = 16 + i * 150
        if failed_at is not None and i > failed_at:
            f.rect(x, y, 128, 66, fill="none", stroke="#3a3a37", dash="5 5")
            f.text(x + 14, y + 28, n, 14, "#686560")
            f.text(x + 14, y + 50, "skipped", 13, "#686560")
        elif failed_at == i:
            f.rect(x, y, 128, 66, fill=FAIL_BG, stroke=FAIL, sw=2.5)
            f.text(x + 14, y + 28, n, 14, weight=600)
            f.text(x + 14, y + 50, "out of stock", 13, FAIL)
        else:
            f.rect(x, y, 128, 66, fill=JUNC_BG, stroke=JUNC, sw=1.5)
            f.text(x + 14, y + 28, n, 14, weight=600)
            f.text(x + 14, y + 50, "done", 13, JUNC)
        bad = failed_at is not None and i >= failed_at
        if bad:
            f.arrow(f"M{x + 128},{y + 33} L{x + 148},{y + 33}", FAIL, dash="4 4", head=False)
        else:
            f.arrow(f"M{x + 128},{y + 33} L{x + 148},{y + 33}", TRAIN, 2)
    if failed_at is None:
        f.rect(616, y, 136, 66, fill=TRAIN_BG, stroke=TRAIN, sw=1.5)
        f.text(630, y + 28, "The order", 14, TRAIN, 600)
    else:
        f.rect(616, y, 136, 66, fill=FAIL_BG, stroke=FAIL, sw=1.5)
        f.text(630, y + 28, "The exception", 14, FAIL, 600)
    f.text(630, y + 50, "back to the caller", 13, MUTED)
f.label(16, 30, "PlaceOrder train · every junction succeeds", TRAIN)
row(42, None)
f.label(16, 146, "PlaceOrder train · one junction fails", FAIL)
row(158, 1)
f.text(16, 254, "No card is charged for an order that cannot be filled, and the run record names the junction that failed.", 13, MUTED)
f.yours(16, 274, ["you write: each junction"])
f.text(210, 289, "Trax runs them in order, skips the rest when one fails, and records the run.", 13, TEXT)
f.write()

# ---------------------------------------------------------------- machine runs train
f = Fig("machine-runs-train", 220, "Reaching the Placing station starts the PlaceOrder train. The train passes its junctions without stopping and returns a result: the checkout arrives at Placed when the train succeeds and at Failed when it does not.")
f.label(16, 30, "Station (state)", STATE)
f.rect(16, 78, 120, 64, fill=STATE_BG, stroke=STATE, sw=2)
f.text(32, 116, "Placing", 15, weight=600)
f.arrow("M136,110 L176,110", TRAIN, 2)
f.text(156, 100, "starts", 12, TRAIN, anchor="middle")
f.label(180, 30, "The train", TRAIN)
f.rect(180, 44, 412, 132, fill=TRAIN_BG, stroke=TRAIN, sw=1.5, rx=10)
f.text(196, 72, "PlaceOrder", 13, TRAIN, mono=True)
for i, n in enumerate(names):
    x = 196 + i * 96
    f.rect(x, 88, 90, 40, fill="#1e1e1e", stroke=JUNC, sw=1.5, rx=6)
    f.text(x + 45, 113, n, 12, anchor="middle")
f.text(196, 158, "passes its junctions without stopping", 13, MUTED)
f.arrow("M592,90 L640,64", STATE, 1.5)
f.arrow("M592,130 L640,156", FAIL, 1.5)
f.rect(644, 36, 108, 56, fill=STATE_BG, stroke=STATE, sw=1.5)
f.text(660, 60, "Placed", 15, weight=600)
f.text(660, 80, "on success", 13, MUTED)
f.rect(644, 128, 108, 56, fill=FAIL_BG, stroke=FAIL, sw=1.5, dash="5 4")
f.text(660, 152, "Failed", 15, FAIL, 600)
f.text(660, 172, "on failure", 13, MUTED)
f.text(16, 206, "A train also runs on its own: from your code, over GraphQL, on a schedule, or on a worker.", 13, MUTED)
f.write()

# ---------------------------------------------------------------- propose / decide
f = Fig("propose-decide", 372, "Outside Trax, a browser, a script and an AI agent each send a move, never the new state; the client code is yours or generated by Trax. Everything inside the dashed line is Trax. The move arrives through Trax's API, which knows who is calling. Trax then checks it against the stored state: is the move allowed from here, which Trax decides alone; may this caller make it, by rules you write; and do the data and evidence hold up, by guards you write. All three pass and Trax saves the new state and runs the work you wrote. Any one fails and the client gets a code such as GUARD_FAILED or FORBIDDEN with your reason, and nothing changes.")

f.label(16, 40, "Outside Trax")
for i, n in enumerate(["A browser", "A script", "An AI agent"]):
    y = 56 + i * 68
    f.rect(16, y, 140, 48)
    f.text(30, y + 29, n, 15)
f.lines(16, 266, ["Your client code, or", "one Trax generates.", "Each sends a move,", "never the new state."], 12, MOVE, 16)
f.rect(172, 16, 580, 314, fill="#191919", stroke=LINE, sw=1.5, rx=12, dash="6 5")
f.label(188, 40, "Inside Trax")
f.arrow("M156,80 C174,80 172,148 190,148", MOVE, 1.5, head=False)
f.arrow("M156,148 L190,148", MOVE, 1.5, head=False)
f.arrow("M156,216 C174,216 172,148 190,148", MOVE, 1.5, head=False)
f.rect(190, 118, 80, 60)
f.text(230, 143, "API + auth", 12, weight=600, anchor="middle")
f.text(230, 162, "who calls?", 12, MUTED, anchor="middle")
f.arrow("M270,148 L288,148", MOVE, 1.5)
f.rect(292, 56, 264, 232, fill="#1b1a14", stroke=GUARD, sw=1.5, rx=10)
f.text(308, 82, "Trax checks every move", 15, weight=600)
f.text(308, 100, "against the state it stored", 12, MUTED)
checks = [("Is this move allowed from here?", None),
          ("May this caller make it?", "you write: who may send it"),
          ("Do the data and evidence hold?", "you write: the guards")]
for i, (q, tag) in enumerate(checks):
    y = 132 + i * 54
    f.circle(316, y - 4, 10, GUARD)
    f.text(316, y, str(i + 1), 12, "#151515", 600, "middle")
    f.text(334, y, q, 13)
    if tag:
        f.yours(334, y + 9, [tag])
    else:
        f.text(334, y + 23, "Trax does this", 12, MUTED, mono=True)
f.arrow("M556,116 L572,116", STATE)
f.arrow("M556,240 L572,240", FAIL, dash="4 3")
f.rect(572, 56, 168, 124, fill=STATE_BG, stroke=STATE, sw=1.5)
f.text(586, 80, "Accepted", 15, STATE, 600)
f.lines(586, 100, ["Trax saves the", "new state."], 12, MUTED, 16)
f.yours(586, 128, ["you write:", "the work it runs"], 128)
f.rect(572, 192, 168, 124, fill=FAIL_BG, stroke=FAIL, sw=1.5, dash="5 4")
f.text(586, 216, "Refused", 15, FAIL, 600)
f.lines(586, 238, ["GUARD_FAILED", "FORBIDDEN"], 12, FAIL, 16, mono=True)
f.lines(586, 278, ["A code and your reason.", "Nothing changes."], 12, MUTED, 16)
f.yours(16, 344, ["you write"], 78)
f.text(106, 359, "Code you write, as are the clients. Everything else inside the line is Trax.", 13, TEXT)
f.write()

# ---------------------------------------------------------------- many drivers
f = Fig("many-drivers", 300, "One workflow, defined once on the server, driven by four kinds of client. Web users get steps in order and valid data at each step. Schedules and queues get retries, dead letters and a record of every run. Scripts and services in any language get the same rules. AI agents get only the moves they are allowed, and evidence the server reruns.")
for d in ["M246,72 C290,72 300,130 332,130", "M246,228 C290,228 300,170 332,170", "M522,72 C478,72 468,130 436,130", "M522,228 C478,228 468,170 436,170"]:
    f.arrow(d, LINE, head=False)
f.rect(276, 110, 216, 80, fill=STATE_BG, stroke=STATE, sw=2, rx=10)
f.text(384, 140, "defined once, on the server", 13, STATE, anchor="middle")
f.text(384, 168, "Your workflow", 18, weight=600, anchor="middle")
for (x, y, head, sub, enf) in [(16, 20, "Web users", "Forms and checkouts.", ["Steps in order, valid", "data at each step."]),
                               (16, 176, "Schedules and queues", "Cron, delayed runs, workers.", ["Retries, dead letters, and", "a record of every run."]),
                               (522, 20, "Scripts and services", "Integrations, any language.", ["The same rules, whatever", "they are written in."]),
                               (522, 176, "AI agents", "Working inside a sandbox.", ["Only allowed moves;", "evidence the server reruns."])]:
    f.rect(x, y, 230, 104)
    f.text(x + 16, y + 28, head, 15, weight=600)
    f.text(x + 16, y + 52, sub, 13, MUTED)
    f.lines(x + 16, y + 76, enf, 13, TEXT, 17)
f.write()

# ---------------------------------------------------------------- agent sandbox
f = Fig("agent-sandbox", 290, "Two layers around an AI agent. The sandbox lets the agent run code, edit files, call tools and read data, but nothing in it reaches real systems. The only way out is through Trax, which checks that the move is allowed in this state, allowed for this agent, and that its evidence holds up, by rules you write. Only verified moves reach the real systems: tickets, orders, data.")
f.rect(16, 16, 250, 200, fill="none", stroke=LINE, sw=1.5, dash="6 5", rx=12)
f.label(32, 40, "Sandbox")
f.rect(36, 56, 210, 140, fill=CARD, stroke=EDGE)
f.text(52, 82, "The agent", 15, weight=600)
f.lines(52, 106, ["runs code", "edits files", "calls tools", "reads data"], 13, MUTED, 19)
f.arrow("M266,116 L314,116", MOVE, 2)
f.text(290, 106, "moves", 12, MOVE, anchor="middle")
f.rect(318, 36, 220, 160, fill=GUARD_BG, stroke=GUARD, sw=1.5, rx=10)
f.text(334, 62, "Trax checks", 15, weight=600)
for i, q in enumerate(["Allowed in this state?", "Allowed for this agent?", "Evidence holds up?"]):
    y = 92 + i * 28
    f.circle(344, y - 5, 9, GUARD)
    f.text(344, y, str(i + 1), 12, "#151515", 600, "middle")
    f.text(362, y, q, 14)
f.yours(334, 162, ["you write: the rules"])
f.arrow("M538,116 L586,116", STATE, 2)
f.text(562, 106, "verified", 12, STATE, anchor="middle")
f.rect(590, 56, 162, 120, fill=STATE_BG, stroke=STATE, sw=1.5)
f.text(606, 84, "Real systems", 15, weight=600)
f.lines(606, 108, ["tickets", "orders", "data"], 13, MUTED, 19)
f.text(16, 252, "The sandbox limits what the agent can do.", 15, TEXT)
f.text(16, 276, "Trax limits what the agent can change.", 15, GUARD, 600)
f.write()

# ---------------------------------------------------------------- board lanes
f = Fig("board-lanes", 300, "The Board sample. A bug ticket moves through five lanes, each owned by one client: Triage by triage-bot, Reproduce by repro-bot, Fix by fix-bot, QA by qa-bot and Review by Sam, a person. The ticket is in Fix, held by fix-bot. An agent submits its evidence, a check train the sample's author wrote reruns it on the server, and the ticket moves only if the check accepts it; otherwise it stays put.")
lanes = [("Triage", "triage-bot"), ("Reproduce", "repro-bot"), ("Fix", "fix-bot"), ("QA", "qa-bot"), ("Review", "Sam, a person")]
for i, (lane, owner) in enumerate(lanes):
    x = 16 + i * 148
    f.rect(x, 16, 144, 132, fill=STATE_BG if lane == "Fix" else CARD, stroke=STATE if lane == "Fix" else EDGE, sw=2 if lane == "Fix" else 1.5)
    f.text(x + 14, 42, lane, 15, STATE, 600)
    f.text(x + 14, 62, owner, 12, MUTED, mono=True)
    if lane == "Fix":
        f.rect(x + 12, 80, 120, 54, fill=CARD, stroke=MUTED, sw=1.2, rx=6)
        f.text(x + 24, 103, "A bug ticket", 13, weight=600)
        f.text(x + 24, 122, "held by fix-bot", 12, MUTED)
f.label(16, 184, "How a ticket moves")
f.rect(16, 198, 190, 52, fill=CARD, stroke=MOVE)
f.text(32, 229, "Agent submits evidence", 13)
f.arrow("M206,224 L254,224", MOVE, 2)
f.rect(258, 198, 200, 52, fill=TRAIN_BG, stroke=TRAIN)
f.text(274, 229, "A check train reruns it", 13)
f.yours(258, 260, ["you write: the check"])
f.arrow("M458,214 L506,202", STATE)
f.arrow("M458,234 L506,256", FAIL)
f.rect(510, 180, 242, 40, fill=STATE_BG, stroke=STATE)
f.text(526, 205, "Accepted: the ticket moves", 13)
f.rect(510, 238, 242, 40, fill=FAIL_BG, stroke=FAIL)
f.text(526, 263, "Rejected: it stays put", 13)
f.write()

# ---------------------------------------------------------------- run record
f = Fig("run-record", 250, "A run's timeline: it starts, PriceCart and ReserveStock finish, a checkpoint is saved, the process crashes, and the run resumes from the checkpoint instead of starting over, then finishes. Every step is recorded in your Postgres and shown in the operations console.")
f.line(40, 80, 728, 80, LINE, 2)
f.line(438, 80, 560, 80, BG, 4, "4 6")
f.circle(48, 80, 8, TEXT); f.text(48, 56, "Started", 14, anchor="middle")
f.circle(160, 80, 8, JUNC); f.text(160, 112, "PriceCart done", 13, MUTED, anchor="middle")
f.circle(280, 80, 8, JUNC); f.text(280, 112, "ReserveStock done", 13, MUTED, anchor="middle")
f.rect(371, 71, 18, 18, fill=TRAIN, stroke=TRAIN, rx=2); f.text(380, 56, "Checkpoint", 14, TRAIN, 600, "middle")
f.add(f'<path d="M486,68 L510,92 M510,68 L486,92" stroke="{FAIL}" stroke-width="3.5"/>'); f.text(498, 56, "Crash", 14, FAIL, 600, "middle")
f.circle(610, 80, 9, BG, TRAIN, 3); f.text(610, 112, "Resumed here", 14, TRAIN, 600, "middle")
f.circle(720, 80, 9, STATE); f.text(720, 56, "Finished", 14, anchor="middle")
for i, (h, ls) in enumerate([("What is recorded", ["Start, end, input, output, and", "which junction failed and why."]),
                             ("The operations console", ["Find a run, read it, requeue it.", "Retries end in dead letters."]),
                             ("Recovery", ["A resume skips to the checkpoint.", "Recorded decisions are replayed."])]):
    x = 16 + i * 250
    f.rect(x, 146, 236, 90)
    f.text(x + 16, 172, h, 15, weight=600)
    f.lines(x + 16, 196, ls, 13, MUTED, 17)
f.write()

# ---------------------------------------------------------------- train callers
f = Fig("train-callers", 260, "Five callers run the same train: your code, a GraphQL client, the scheduler, a worker process and a state machine. Each run of the train leaves a run record, and the operations console shows the records.")
f.label(16, 26, "Callers")
for i, n in enumerate(["your code", "a GraphQL client", "the scheduler", "a worker process", "a state machine"]):
    y = 36 + i * 42
    f.rect(16, y, 168, 34, rx=6)
    f.text(30, y + 22, n, 14)
    if i == 2:
        f.arrow("M184,137 L254,137")
    else:
        f.arrow(f"M184,{y + 17} C220,{y + 17} 222,137 252,137", head=False)
f.label(260, 26, "One train", TRAIN)
f.rect(260, 80, 268, 114, fill=TRAIN_BG, stroke=TRAIN, sw=1.5, rx=10)
for i in range(3):
    x = 276 + i * 84
    f.rect(x, 118, 70, 38, fill=CARD, stroke=JUNC, rx=6)
    f.text(x + 35, 142, "junction", 12, anchor="middle")
f.arrow("M346,137 L358,137 M430,137 L442,137", JUNC, head=False)
f.text(276, 180, "written once, unchanged by its callers", 13, MUTED)
f.arrow("M528,137 L570,137")
f.rect(574, 80, 178, 70)
f.text(590, 106, "a run record", 14, weight=600)
f.lines(590, 126, ["when, how it ended,", "which step failed, why"], 13, MUTED, 16)
f.arrow("M663,150 L663,182")
f.rect(574, 186, 178, 40, fill=STATE_BG, stroke=STATE)
f.text(590, 211, "the operations console", 14)
f.write()
