//! The Rust client as a client-conformance testee (`Trax.StateMachine/conformance/client/PROTOCOL.md`): a
//! `ServerMachineController` over a `GraphQLMachineServer`, predicting with the machine built from the IR the
//! transcript gives, driven through the crate's public API only. Each request is handed to the client as it arrives
//! and answered when it settles.

use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;
use std::time::Duration;

use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::sync::Mutex;
use trax_state_machine::{Map, PlanEvent, PlanInvalid, Value};
use trax_state_machine_client::{
    GraphQLMachineServer, GraphQLMachineServerOptions, MachineStatus, MachineView, ManifestDocument, SendOutcome,
    ServerMachineController, ServerMachineOptions, SyncOutcome,
};

type Out = Arc<Mutex<tokio::io::Stdout>>;

#[tokio::main]
async fn main() {
    let out: Out = Arc::new(Mutex::new(tokio::io::stdout()));
    let mut lines = BufReader::new(tokio::io::stdin()).lines();
    let mut controller: Option<ServerMachineController> = None;

    while let Ok(Some(line)) = lines.next_line().await {
        let Ok(request) = Value::parse(&line) else { continue };
        let id = request.get("id").cloned().unwrap_or(Value::Null);
        let op = request.get("op").and_then(|o| o.as_str()).unwrap_or("").to_owned();
        match op.as_str() {
            "open" => {
                controller = None;
                let answer = match open(&request) {
                    Ok(opened) => {
                        controller = Some(opened);
                        object([("opened", Value::Bool(true))])
                    }
                    // A client refusing cleartext to another host refuses to be built, before it sends anything.
                    Err(error) if error.contains("not over TLS") => object([
                        ("opened", Value::Bool(false)),
                        ("code", Value::from("INSECURE_TRANSPORT")),
                    ]),
                    Err(error) => object([("error", Value::from(error))]),
                };
                write(&out, id, answer).await;
            }
            "dispose" | "end" => {
                controller = None;
                let answer = if op == "dispose" {
                    object([("disposed", Value::Bool(true))])
                } else {
                    Map::new()
                };
                write(&out, id, answer).await;
                if op == "end" {
                    return;
                }
            }
            _ => {
                let Some(client) = &controller else {
                    write(&out, id, object([("error", Value::from("no client is open"))])).await;
                    continue;
                };
                match op.as_str() {
                    "view" => write(&out, id, view_of(&client.view())).await,
                    "start" => {
                        let started = client.start();
                        let out = out.clone();
                        tokio::spawn(async move {
                            started.await;
                            write(&out, id, object([("settled", Value::Bool(true))])).await;
                        });
                    }
                    "send" | "retry" => {
                        let settled: std::pin::Pin<Box<dyn std::future::Future<Output = SendOutcome> + Send>> =
                            if op == "send" {
                                let trigger = request.get("trigger").and_then(|t| t.as_str()).unwrap_or("").to_owned();
                                let input = request.get("input").filter(|i| !i.is_null()).cloned();
                                Box::pin(client.send(trigger, input))
                            } else {
                                Box::pin(client.retry())
                            };
                        let out = out.clone();
                        tokio::spawn(async move {
                            let outcome = settled.await;
                            write(&out, id, outcome_of(&outcome)).await;
                        });
                    }
                    "sync" => {
                        let target = request.get("target").and_then(|t| t.as_str()).unwrap_or("").to_owned();
                        let fields = request.get("fields").filter(|f| !f.is_null()).map(|f| {
                            PlanEvent::new(
                                f.get("trigger").and_then(|t| t.as_str()).unwrap_or(""),
                                f.get("input").cloned(),
                            )
                        });
                        let navigation = request
                            .get("navigation")
                            .and_then(|n| n.as_array())
                            .map(|n| n.iter().filter_map(|t| t.as_str().map(str::to_owned)).collect())
                            .unwrap_or_default();
                        let settled = client.sync_to(target, fields, navigation);
                        let out = out.clone();
                        tokio::spawn(async move {
                            let outcome = settled.await;
                            write(&out, id, sync_outcome_of(&outcome)).await;
                        });
                    }
                    _ => write(&out, id, object([("unsupported", Value::Bool(true))])).await,
                }
            }
        }
    }
}

fn open(request: &Value) -> Result<ServerMachineController, String> {
    let text = |key: &str| request.get(key).and_then(|v| v.as_str()).map(str::to_owned);
    let delays = |key: &str| -> Vec<Duration> {
        request
            .get(key)
            .and_then(|d| d.as_array())
            .map(|d| {
                d.iter()
                    .filter_map(|n| n.as_f64())
                    .map(|ms| Duration::from_millis(ms as u64))
                    .collect()
            })
            .unwrap_or_default()
    };
    let documents = request
        .get("documents")
        .and_then(|d| d.as_array())
        .ok_or("no documents")?
        .iter()
        .map(|d| ManifestDocument {
            id: d.get("id").and_then(|v| v.as_str()).unwrap_or("").to_owned(),
            operation: d.get("operation").and_then(|v| v.as_str()).unwrap_or("").to_owned(),
            document: d.get("document").and_then(|v| v.as_str()).unwrap_or("").to_owned(),
        })
        .collect();
    let mut server_options = GraphQLMachineServerOptions::new(
        text("endpoint").ok_or("no endpoint")?,
        text("socket").ok_or("no socket")?,
        text("machine").ok_or("no machine")?,
        documents,
    );
    server_options.resubscribe_delays = Some(delays("resubscribeDelays"));
    if let Some(max) = request.get("maxResponseSize").and_then(|v| v.as_f64()) {
        // One limit holds an HTTP answer and a socket message alike.
        server_options.max_message_size = max as usize;
    }
    if let Some(ms) = request.get("requestTimeout").and_then(|v| v.as_f64()) {
        server_options.request_timeout = Duration::from_millis(ms as u64);
    }
    server_options.allow_insecure_transport =
        request.get("allowInsecureTransport").and_then(|v| v.as_bool()) == Some(true);
    let server = GraphQLMachineServer::new(server_options).map_err(|e| e.to_string())?;
    // Request ids count r1, r2, ... from each open, so the host can tell which request is which.
    let count = Arc::new(AtomicU64::new(0));
    let mut options = ServerMachineOptions::new(
        request.get("ir").cloned().ok_or("no ir")?,
        text("irHash").ok_or("no irHash")?,
        server,
    );
    options.id = text("instance");
    options.start_input = request.get("startInput").filter(|i| !i.is_null()).cloned();
    options.new_request_id = Some(Box::new(move || {
        format!("r{}", count.fetch_add(1, Ordering::Relaxed) + 1)
    }));
    options.retry_delays = Some(delays("retryDelays"));
    ServerMachineController::new(options).map_err(|e| e.to_string())
}

fn view_of(view: &MachineView) -> Map {
    let opt = |s: &Option<String>| s.as_deref().map(Value::from).unwrap_or(Value::Null);
    object([
        (
            "status",
            Value::from(match view.status {
                MachineStatus::Loading => "loading",
                MachineStatus::Ready => "ready",
                MachineStatus::Stopped => "stopped",
            }),
        ),
        ("instance", opt(&view.id)),
        (
            "state",
            view.snapshot
                .as_ref()
                .map(|s| Value::from(s.state.as_str()))
                .unwrap_or(Value::Null),
        ),
        (
            "context",
            view.snapshot
                .as_ref()
                .map(|s| Value::Object(s.context.clone()))
                .unwrap_or(Value::Null),
        ),
        (
            "confirmed",
            view.confirmed
                .as_ref()
                .map(|c| {
                    Value::Object(object([
                        ("state", Value::from(c.state.as_str())),
                        ("context", Value::Object(c.context.clone())),
                    ]))
                })
                .unwrap_or(Value::Null),
        ),
        ("rowId", opt(&view.row_id)),
        (
            "revision",
            view.revision.map(|r| Value::Number(r as f64)).unwrap_or(Value::Null),
        ),
        ("dueAt", opt(&view.due_at)),
        ("pending", Value::Bool(view.pending)),
        (
            "problem",
            view.problem
                .as_ref()
                .map(|p| Value::from(p.code.as_str()))
                .unwrap_or(Value::Null),
        ),
        (
            "error",
            view.error
                .as_ref()
                .map(|e| Value::from(e.code.as_str()))
                .unwrap_or(Value::Null),
        ),
        (
            "deleted",
            view.error
                .as_ref()
                .and_then(|e| e.deleted)
                .map(Value::Bool)
                .unwrap_or(Value::Null),
        ),
        ("unsaved", Value::Bool(view.unsaved)),
    ])
}

fn outcome_of(outcome: &SendOutcome) -> Map {
    match outcome {
        SendOutcome::Confirmed(s) => object([
            ("outcome", Value::from("confirmed")),
            ("state", Value::from(s.state.as_str())),
            (
                "revision",
                s.revision.map(|r| Value::Number(r as f64)).unwrap_or(Value::Null),
            ),
        ]),
        SendOutcome::Refused(p) => object([
            ("outcome", Value::from("refused")),
            ("code", Value::from(p.code.as_str())),
        ]),
        SendOutcome::Failed(p) => object([
            ("outcome", Value::from("failed")),
            ("code", Value::from(p.code.as_str())),
        ]),
        SendOutcome::Stopped(e) => object([
            ("outcome", Value::from("stopped")),
            ("code", Value::from(e.code.as_str())),
        ]),
    }
}

fn sync_outcome_of(outcome: &SyncOutcome) -> Map {
    let named = |name: &str| object([("outcome", Value::from(name))]);
    let coded = |name: &str, code: &str| object([("outcome", Value::from(name)), ("code", Value::from(code))]);
    match outcome {
        SyncOutcome::Synced { snapshot, revision } => object([
            ("outcome", Value::from("synced")),
            ("state", Value::from(snapshot.state.as_str())),
            (
                "revision",
                revision.map(|r| Value::Number(r as f64)).unwrap_or(Value::Null),
            ),
        ]),
        SyncOutcome::Superseded => named("superseded"),
        SyncOutcome::Unreachable => named("unreachable"),
        SyncOutcome::Exceeded => named("exceeded"),
        SyncOutcome::Invalid(PlanInvalid::Target(target)) => object([
            ("outcome", Value::from("invalid")),
            ("target", Value::from(target.as_str())),
        ]),
        SyncOutcome::Invalid(PlanInvalid::Trigger(trigger)) => object([
            ("outcome", Value::from("invalid")),
            ("trigger", Value::from(trigger.as_str())),
        ]),
        SyncOutcome::Refused(p) => coded("refused", &p.code),
        SyncOutcome::Failed(p) => coded("failed", &p.code),
        SyncOutcome::Stopped(e) => coded("stopped", &e.code),
    }
}

fn object<const N: usize>(members: [(&str, Value); N]) -> Map {
    members.into_iter().map(|(k, v)| (k.into(), v)).collect()
}

async fn write(out: &Out, id: Value, mut answer: Map) {
    answer.insert("id".into(), id);
    let line = Value::Object(answer).to_json() + "\n";
    let mut out = out.lock().await;
    let _ = out.write_all(line.as_bytes()).await;
    let _ = out.flush().await;
}
