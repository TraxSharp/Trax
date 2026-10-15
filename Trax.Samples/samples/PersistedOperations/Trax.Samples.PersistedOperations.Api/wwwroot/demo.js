// Plays the sample's three stories against this API, with the requests the console client sends.
// The app and the stranger send no credential; the operator sends the demo key, which this host
// registers only in Development, the only environment that serves this page.

const ENDPOINT = "/trax/graphql/";
const OPERATOR_KEY = "operator-key-do-not-use-in-production";

// greet_v1 as Client/manifest.json ships it, the operator's fix, and the edit that is refused.
const SHIPPED =
  "query Greet($name: String!) { discover { greeting { greet(input: { name: $name, style: CASUAL }) { greeting } } } }";
const FIX = SHIPPED.replace("style: CASUAL", "style: FORMAL");
const RENAME = FIX.replace("{ greeting }", "{ message: greeting }");
const INLINE = 'query { discover { greeting { greet(input: { name: "Eve", style: FORMAL }) { greeting } } } }';

const UPLOAD = `mutation Upload($input: UploadPersistedOperationInput!) {
  persistedOperations { uploadPersistedOperation(input: $input) { success errors { code message } } }
}`;
const READ = `query Stored($id: String!) {
  persistedOperations { persistedOperation(id: $id) { document } }
}`;

/** When the first request goes out, the time between one answer and the next request, and the least a request shows as in flight. */
const PACE = { first: 400, between: 1400, minInFlight: 450 };

const ACTOR = { app: "The app", stranger: "Someone else", operator: "The operator" };
const LABEL = { waiting: "sent", answered: "answered", accepted: "accepted", refused: "refused" };

const STEPS = {
  appCalls: { actor: "app", kind: "call", body: { id: "greet_v1", variables: { name: "Alice" } } },
  someoneElse: { actor: "stranger", kind: "inline", body: { query: INLINE } },
  fix: { actor: "operator", kind: "upload", document: FIX },
  rename: { actor: "operator", kind: "upload", document: RENAME },
};

const STORIES = [
  { key: "calls", title: "A shipped app calls the API", steps: ["appCalls", "someoneElse"] },
  { key: "fix", title: "Fix it without shipping a new app", steps: ["appCalls", "fix", "appCalls"] },
  { key: "refused", title: "A fix that would break the app is refused", steps: ["appCalls", "rename", "appCalls"] },
];

const $ = (id) => document.getElementById(id);
const scenario = $("scenario");
const playButton = $("play");
const resetButton = $("reset");

const uploadBody = (document) => ({
  query: UPLOAD,
  variables: { input: { id: "greet_v1", document, bypassShapeDiff: false } },
});

/** Sends one request and returns the parsed response, its status and how long it took. */
async function send(body, { operator = false } = {}) {
  const headers = { "Content-Type": "application/json" };
  if (operator) headers["X-Api-Key"] = OPERATOR_KEY;
  const started = performance.now();
  const response = await fetch(ENDPOINT, { method: "POST", headers, body: JSON.stringify(body) });
  const text = await response.text();
  const ms = Math.round(performance.now() - started);
  let json;
  try {
    json = JSON.parse(text);
  } catch {
    json = { errors: [{ message: `HTTP ${response.status}` }] };
  }
  return { status: response.status, response: json, ms };
}

async function readStored() {
  const { response } = await send({ query: READ, variables: { id: "greet_v1" } }, { operator: true });
  if (response.errors) throw new Error(response.errors[0].message);
  return response.data?.persistedOperations?.persistedOperation?.document ?? null;
}

/** What came back for a step, in a line. */
function outcomeOf(step, { status, response }) {
  const error = response.errors?.[0]?.message;
  if (step.kind === "upload") {
    const result = response.data?.persistedOperations?.uploadPersistedOperation;
    if (result?.success) return { outcome: "accepted", text: "Accepted" };
    return { outcome: "refused", text: result?.errors?.[0]?.message ?? error ?? `HTTP ${status}` };
  }
  if (error) return { outcome: "refused", text: error };
  const greeting = response.data?.discover?.greeting?.greet?.greeting;
  return greeting != null ? { outcome: "answered", text: greeting } : { outcome: "refused", text: `HTTP ${status}` };
}

function sentText(step) {
  if (step.kind === "call") return `${step.body.id} ${JSON.stringify(step.body.variables)}`;
  if (step.kind === "inline") return step.body.query;
  return `greet_v1 = ${step.document}`;
}

// What the page shows. `run` grows by one each Play or Reset, so a story that is still playing
// stops drawing as soon as the reader resets or starts another.
const view = { cards: [], greeting: null, calling: false, stored: null, proposed: null };
let run = 0;
let playing = false;

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  return node;
}

function pill(outcome) {
  return el("span", `pill ${outcome}`, LABEL[outcome]);
}

function renderCards() {
  const box = $("cards");
  box.replaceChildren(
    ...view.cards.map((card) => {
      const node = el("div", "card");
      const head = el("div", "card-head");
      head.append(el("span", "actor", ACTOR[card.actor]), pill(card.outcome));
      const answer = el("div", "answer");
      if (card.outcome === "waiting") {
        answer.append(el("span", "text waiting", "Waiting for the server…"));
      } else {
        answer.append(el("span", `text ${card.outcome}`, `← ${card.answer}`));
        if (card.ms != null) answer.append(el("span", "ms", `${card.ms} ms`));
      }
      node.append(head, el("p", "sent", card.sent), answer);
      return node;
    }),
  );
  // Scroll the panel itself, never the page.
  box.scrollTop = box.scrollHeight;
}

function renderPhone() {
  const node = $("greeting");
  node.textContent = view.greeting ?? "…";
  node.className = `greeting${view.greeting ? "" : " empty"}${view.calling ? " calling" : ""}`;
}

function documentNode(label, text, outcome, against) {
  const node = el("div", "document");
  const head = el("div", "document-head", label);
  if (outcome) head.append(pill(outcome));
  const body = el("p", "document-text");
  // Words that differ from what the server held when the edit was sent are lit.
  const was = new Set((against ?? "").split(/(\s+)/));
  for (const word of (text ?? "nothing stored").split(/(\s+)/)) {
    if (against !== undefined && word.trim() && !was.has(word)) body.append(el("span", `changed ${outcome ?? ""}`, word));
    else body.append(document.createTextNode(word));
  }
  node.append(head, body);
  return node;
}

function renderStored() {
  const nodes = [documentNode("On the server", view.stored)];
  if (view.proposed)
    nodes.push(documentNode("Sent by the operator", view.proposed.document, view.proposed.outcome, view.proposed.against));
  $("documents").replaceChildren(...nodes);
}

function render() {
  renderCards();
  renderPhone();
  renderStored();
  scenario.disabled = playing;
  playButton.disabled = playing;
  resetButton.disabled = !playing && view.cards.length === 0;
}

const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function play() {
  const mine = ++run;
  const story = STORIES.find((s) => s.key === scenario.value);
  Object.assign(view, { cards: [], greeting: null, calling: false, proposed: null });
  playing = true;
  render();

  try {
    // Every story starts where the app shipped: greet_v1 as the manifest has it. Not shown.
    const reset = await send(uploadBody(SHIPPED), { operator: true });
    if (outcomeOf({ kind: "upload" }, reset).outcome !== "accepted") throw new Error("could not restore greet_v1");
    const stored = await readStored();
    if (mine !== run) return;
    view.stored = stored;
    render();
    await wait(PACE.first);

    for (const name of story.steps) {
      if (mine !== run) return;
      const step = STEPS[name];
      const against = view.stored;
      const card = { actor: step.actor, sent: sentText(step), outcome: "waiting" };
      view.cards.push(card);
      if (step.actor === "app") view.calling = true;
      if (step.kind === "upload") view.proposed = { document: step.document, outcome: "waiting", against };
      render();

      const shown = performance.now();
      const result =
        step.kind === "upload"
          ? await send(uploadBody(step.document), { operator: true })
          : await send(step.body);
      const storedAfter = step.kind === "upload" ? await readStored() : undefined;
      await wait(Math.max(0, PACE.minInFlight - (performance.now() - shown)));
      if (mine !== run) return;

      const { outcome, text } = outcomeOf(step, result);
      Object.assign(card, { outcome, answer: text, ms: result.ms });
      if (step.actor === "app") {
        view.calling = false;
        if (outcome === "answered") view.greeting = text;
      }
      if (step.kind === "upload") {
        view.proposed.outcome = outcome;
        view.stored = storedAfter;
      }
      render();
      if (name !== story.steps.at(-1)) await wait(PACE.between);
    }
  } catch (error) {
    if (mine !== run) return;
    view.cards.push({ actor: "operator", sent: "", outcome: "refused", answer: String(error.message ?? error) });
  }
  if (mine !== run) return;
  playing = false;
  view.calling = false;
  render();
}

function reset() {
  run++;
  playing = false;
  Object.assign(view, { cards: [], greeting: null, calling: false, proposed: null });
  render();
}

for (const story of STORIES) scenario.append(new Option(story.title, story.key));
scenario.addEventListener("change", reset);
playButton.addEventListener("click", play);
resetButton.addEventListener("click", reset);

render();
readStored()
  .then((stored) => {
    view.stored = stored;
    renderStored();
  })
  .catch(() => {});
