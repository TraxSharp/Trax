//! The [`MachineServer`] over a host's typed `machines` root: each operation is a persisted document of the host's
//! client manifest, sent by its id with its values as variables, and the change stream is the machine's
//! `<machine>Changed` subscription, subscribed by its id over graphql-transport-ws.

use std::collections::{HashMap, HashSet};
use std::sync::Arc;
use std::time::Duration;

use futures_util::{SinkExt, StreamExt};
use tokio::task::JoinHandle;
use tokio_tungstenite::tungstenite::client::IntoClientRequest;
use tokio_tungstenite::tungstenite::http::HeaderValue;
use tokio_tungstenite::tungstenite::Message;
use trax_state_machine::{Map, Value};

use crate::controller::{codes, without_nulls};
use crate::server::{
    DueAt, EventRequest, MachineServer, ServerAnswer, ServerProblem, ServerSnapshot, StreamMessage, StreamSink,
    Subscription, TransportError,
};

/// One persisted document of the host's client manifest.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ManifestDocument {
    /// The id a client sends in place of the document.
    pub id: String,
    /// The operation: `start`, `get`, `discard`, `changed`, or an event's field (`addItem`).
    pub operation: String,
    /// The document's text, which declares the variables it takes.
    pub document: String,
}

/// How a [`GraphQLMachineServer`] reaches its host.
pub struct GraphQLMachineServerOptions {
    /// The host's GraphQL endpoint (`https://host/trax/graphql`). It must be `https` unless its host is loopback or
    /// `allow_insecure_transport` is set.
    pub endpoint: String,
    /// The host's socket endpoint (`wss://host/trax/graphql`). It must be `wss` unless its host is loopback or
    /// `allow_insecure_transport` is set.
    pub socket: String,
    /// The machine's id (`checkout`).
    pub machine: String,
    /// The machine's persisted documents, from the host's manifest, the push (`changed`) included.
    pub documents: Vec<ManifestDocument>,
    /// The client requests go through. Set default headers on it for authentication. A plain one when `None`.
    pub http: Option<reqwest::Client>,
    /// The `connection_init` payload, how a host authenticates a socket. None sends none.
    pub connection_params: Option<Value>,
    /// Delays before subscribing again after a stream that ended without saying why. The last repeats.
    pub resubscribe_delays: Option<Vec<Duration>>,
    /// Accepts an `http` endpoint or a `ws` socket on a host that is not loopback. Off by default: whatever
    /// authenticates the client would travel in cleartext.
    pub allow_insecure_transport: bool,
}

impl GraphQLMachineServerOptions {
    /// Options for one machine's documents at `endpoint` and `socket`, with every other choice left to its default.
    pub fn new(
        endpoint: impl Into<String>,
        socket: impl Into<String>,
        machine: impl Into<String>,
        documents: Vec<ManifestDocument>,
    ) -> Self {
        GraphQLMachineServerOptions {
            endpoint: endpoint.into(),
            socket: socket.into(),
            machine: machine.into(),
            documents,
            http: None,
            connection_params: None,
            resubscribe_delays: None,
            allow_insecure_transport: false,
        }
    }
}

/// The default delays before subscribing again after a stream ended without a reason.
pub const DEFAULT_RESUBSCRIBE_DELAYS: [Duration; 3] =
    [Duration::from_secs(1), Duration::from_secs(2), Duration::from_secs(5)];

/// Why a [`GraphQLMachineServer`] could not be built.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ConfigError(pub String);

impl std::fmt::Display for ConfigError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for ConfigError {}

struct Document {
    id: String,
    variables: HashSet<String>,
}

/// The [`MachineServer`] over a host's typed `machines` root. A refusal is an answer, and so is `CLIENT_OUTDATED` or
/// `SERVER_OUTDATED`, the host's answer to a document of another version; any other GraphQL error, a status that is
/// not success, or a request that does not complete is a [`TransportError`].
pub struct GraphQLMachineServer {
    inner: Arc<Inner>,
}

struct Inner {
    http: reqwest::Client,
    endpoint: String,
    socket: String,
    machine: String,
    field: String,
    push_field: String,
    documents: HashMap<String, Document>,
    changed: String,
    connection_params: Option<Value>,
    resubscribe_delays: Vec<Duration>,
}

impl GraphQLMachineServer {
    /// Builds the server for one machine. Refuses a machine with no documents, or none for `changed`, and an endpoint
    /// or socket that is not over TLS on a host that is not loopback unless `allow_insecure_transport` is set.
    pub fn new(options: GraphQLMachineServerOptions) -> Result<Self, ConfigError> {
        // The socket's TLS needs a process-wide crypto provider; another one already installed is kept.
        let _ = rustls::crypto::ring::default_provider().install_default();
        require_secure(&options.endpoint, "https", options.allow_insecure_transport)?;
        require_secure(&options.socket, "wss", options.allow_insecure_transport)?;
        if options.documents.is_empty() {
            return Err(ConfigError(format!(
                "The machine '{}' has no persisted documents. Export the host's client manifest and pass its \
                 documents: a client sends only the ids the host registered.",
                options.machine
            )));
        }
        let documents: HashMap<String, Document> = options
            .documents
            .iter()
            .map(|d| {
                (
                    d.operation.clone(),
                    Document {
                        id: d.id.clone(),
                        variables: declared_variables(&d.document),
                    },
                )
            })
            .collect();
        let changed = documents.get("changed").map(|d| d.id.clone()).ok_or_else(|| {
            ConfigError(format!(
                "The documents of '{}' have no changed document. Export them again from the host's manifest.",
                options.machine
            ))
        })?;
        let field = machine_field(&options.machine);
        let delays = match options.resubscribe_delays {
            Some(delays) if !delays.is_empty() => delays,
            _ => DEFAULT_RESUBSCRIBE_DELAYS.to_vec(),
        };
        Ok(GraphQLMachineServer {
            inner: Arc::new(Inner {
                http: options.http.unwrap_or_default(),
                endpoint: options.endpoint,
                socket: options.socket,
                push_field: format!("{field}Changed"),
                field,
                machine: options.machine,
                documents,
                changed,
                connection_params: options.connection_params,
                resubscribe_delays: delays,
            }),
        })
    }

    /// Deletes one of the caller's instances. One already gone, or never the caller's, is answered discarded too, so
    /// a retry is harmless. `Ok(None)` when it is gone; otherwise the refusal.
    pub async fn discard(&self, id: &str, request_id: &str) -> Result<Option<ServerProblem>, TransportError> {
        match self
            .inner
            .post(
                "discard",
                &[
                    ("id", Some(Value::from(id))),
                    ("requestId", Some(Value::from(request_id))),
                ],
            )
            .await?
        {
            Posted::Refused(problem) => Ok(Some(problem)),
            Posted::Answer(answer) => Ok(answer.as_ref().and_then(|a| a.get("problem")).and_then(problem_of)),
        }
    }
}

enum Posted {
    Answer(Option<Value>),
    Refused(ServerProblem),
}

impl Inner {
    // One operation, by its persisted id, with the values its document declares as variables.
    async fn post(&self, operation: &str, values: &[(&str, Option<Value>)]) -> Result<Posted, TransportError> {
        let Some(document) = self.documents.get(operation) else {
            return Ok(Posted::Refused(ServerProblem {
                code: codes::CLIENT_OUTDATED.to_owned(),
                message: format!(
                    "There is no document for '{}.{operation}': the documents are of another version of the machine.",
                    self.machine
                ),
            }));
        };
        let mut variables = Map::new();
        for (name, value) in values {
            if let Some(value) = value {
                if document.variables.contains(*name) {
                    variables.insert((*name).into(), value.clone());
                }
            }
        }
        let mut body = Map::new();
        body.insert("id".into(), Value::from(document.id.as_str()));
        body.insert("variables".into(), Value::Object(variables));

        let response = self
            .http
            .post(&self.endpoint)
            .header("content-type", "application/json")
            .header("accept", "application/json")
            .body(Value::Object(body).to_json())
            .send()
            .await
            .map_err(transport_error)?;
        let status = response.status();
        let text = response.text().await.map_err(transport_error)?;
        let answered = Value::parse(&text).ok();

        // A GraphQL error with a code is the host's to answer with whatever status it chose.
        if let Some(error) = answered
            .as_ref()
            .and_then(|a| a.get("errors"))
            .and_then(|e| e.as_array())
            .and_then(|e| e.first())
        {
            let code = error
                .get("extensions")
                .and_then(|x| x.get("code"))
                .and_then(|c| c.as_str());
            let message = error
                .get("message")
                .and_then(|m| m.as_str())
                .unwrap_or("The server refused the request.");
            if let Some(code) = code.filter(|c| *c == codes::CLIENT_OUTDATED || *c == codes::SERVER_OUTDATED) {
                return Ok(Posted::Refused(ServerProblem {
                    code: code.to_owned(),
                    message: message.to_owned(),
                }));
            }
            return Err(TransportError(message.to_owned()));
        }
        if !status.is_success() {
            return Err(TransportError(format!("The server answered {status}.")));
        }
        let answered = answered.ok_or_else(|| TransportError("The server's answer is not JSON.".to_owned()))?;
        let answer = answered
            .get("data")
            .and_then(|d| d.get("machines"))
            .and_then(|m| m.get(&self.field))
            .and_then(|f| f.get(operation))
            .filter(|a| !a.is_null())
            .cloned();
        Ok(Posted::Answer(answer))
    }

    async fn run(&self, operation: &str, values: &[(&str, Option<Value>)]) -> Result<ServerAnswer, TransportError> {
        match self.post(operation, values).await? {
            Posted::Refused(problem) => Ok(ServerAnswer::Refused(problem)),
            Posted::Answer(None) => Err(TransportError(
                "The server answered without a snapshot or a problem.".to_owned(),
            )),
            Posted::Answer(Some(answer)) => answer_of(&answer),
        }
    }
}

// A request that failed, said without its URL: a query can carry a token.
fn transport_error(error: reqwest::Error) -> TransportError {
    TransportError(error.without_url().to_string())
}

fn answer_of(answer: &Value) -> Result<ServerAnswer, TransportError> {
    if let Some(problem) = answer.get("problem").and_then(problem_of) {
        return Ok(ServerAnswer::Refused(problem));
    }
    let snapshot = snapshot_of(answer)
        .ok_or_else(|| TransportError("The server answered a snapshot without a state.".to_owned()))?;
    Ok(ServerAnswer::Stored {
        id: answer.get("id").and_then(|i| i.as_str()).map(str::to_owned),
        snapshot,
    })
}

fn snapshot_of(value: &Value) -> Option<ServerSnapshot> {
    let state = value.get("state")?.as_str()?.to_owned();
    // A revision the store does not record stays `None`: it is not revision 0, which would order every later answer
    // below it.
    let revision = match value.get("revision") {
        None | Some(Value::Null) => None,
        Some(revision) => Some(revision.as_f64()? as i64),
    };
    let context = value
        .get("context")
        .and_then(|c| c.as_object())
        .map(without_nulls)
        .unwrap_or_default();
    // A due time the server says it cannot know, or does not carry, stays unknown, so it is never read as "no timer".
    let due_at = if value.get("dueAtKnown").and_then(|k| k.as_bool()) == Some(false) {
        DueAt::Unknown
    } else {
        match value.get("dueAt") {
            None => DueAt::Unknown,
            Some(Value::String(s)) => DueAt::At(s.to_string_lossy()),
            Some(_) => DueAt::None,
        }
    };
    let row_id = match value.get("rowId") {
        Some(Value::String(s)) => Some(s.to_string_lossy()),
        Some(Value::Number(n)) => Some(trax_state_machine::number_text(*n)),
        _ => None,
    };
    Some(ServerSnapshot {
        row_id,
        revision,
        state,
        context,
        due_at,
    })
}

fn problem_of(value: &Value) -> Option<ServerProblem> {
    let code = value.get("code")?.as_str()?.to_owned();
    let message = value.get("message").and_then(|m| m.as_str()).unwrap_or("").to_owned();
    Some(ServerProblem { code, message })
}

impl MachineServer for GraphQLMachineServer {
    async fn start(&self, input: Option<Value>, request_id: String) -> Result<ServerAnswer, TransportError> {
        self.inner
            .run(
                "start",
                &[("input", input), ("requestId", Some(Value::from(request_id)))],
            )
            .await
    }

    async fn event(&self, request: EventRequest) -> Result<ServerAnswer, TransportError> {
        self.inner
            .run(
                &event_field(&request.trigger),
                &[
                    ("id", Some(Value::from(request.id))),
                    ("input", request.input),
                    ("requestId", Some(Value::from(request.request_id))),
                    ("clientResult", request.client_result.map(Value::from)),
                ],
            )
            .await
    }

    async fn get(&self, id: String) -> Result<ServerAnswer, TransportError> {
        match self.inner.post("get", &[("id", Some(Value::from(id)))]).await? {
            Posted::Refused(problem) => Ok(ServerAnswer::Refused(problem)),
            Posted::Answer(None) => Ok(ServerAnswer::Refused(ServerProblem {
                code: codes::NOT_FOUND.to_owned(),
                message: "No instance with that id.".to_owned(),
            })),
            Posted::Answer(Some(answer)) => answer_of(&answer),
        }
    }

    fn subscribe(&self, id: String, sink: StreamSink) -> Box<dyn Subscription> {
        let inner = self.inner.clone();
        Box::new(Stream(tokio::spawn(async move { inner.stream(id, sink).await })))
    }
}

// The task that follows one instance; dropping the handle stops it and closes its socket.
struct Stream(JoinHandle<()>);

impl Subscription for Stream {}

impl Drop for Stream {
    fn drop(&mut self) {
        self.0.abort();
    }
}

// How one subscription over one socket ended.
enum Ended {
    // For good: the instance is gone, or this client is outdated. Nothing is subscribed again.
    ForGood,
    // Without saying why: subscribed again after the next delay.
    Again,
}

impl Inner {
    // Subscribes, and subscribes again after each delay whenever a stream ends without saying why. Each subscription
    // has a socket of its own, opened when it starts and closed when it ends.
    async fn stream(&self, id: String, sink: StreamSink) {
        let mut attempt = 0usize;
        loop {
            match self.subscribe_once(&id, &sink, &mut attempt).await {
                Ended::ForGood => return,
                Ended::Again => {
                    let delay = self.resubscribe_delays[attempt.min(self.resubscribe_delays.len() - 1)];
                    attempt += 1;
                    tokio::time::sleep(delay).await;
                }
            }
        }
    }

    async fn subscribe_once(&self, id: &str, sink: &StreamSink, attempt: &mut usize) -> Ended {
        let Ok(mut request) = self.socket.as_str().into_client_request() else {
            return Ended::Again;
        };
        request.headers_mut().insert(
            "Sec-WebSocket-Protocol",
            HeaderValue::from_static("graphql-transport-ws"),
        );
        let Ok((mut socket, _)) = tokio_tungstenite::connect_async(request).await else {
            return Ended::Again;
        };

        let mut init = Map::new();
        init.insert("type".into(), Value::from("connection_init"));
        if let Some(params) = &self.connection_params {
            init.insert("payload".into(), params.clone());
        }
        if socket.send(Message::text(Value::Object(init).to_json())).await.is_err() {
            return Ended::Again;
        }
        // Acknowledged, or closed before: a host that turns the caller away closes here.
        loop {
            match socket.next().await {
                Some(Ok(Message::Text(text))) => match Value::parse(text.as_str())
                    .ok()
                    .as_ref()
                    .and_then(|m| m.get("type"))
                    .and_then(|t| t.as_str())
                {
                    Some("connection_ack") => break,
                    Some("ping") => {
                        let _ = socket.send(Message::text(r#"{"type":"pong"}"#)).await;
                    }
                    _ => {}
                },
                Some(Ok(Message::Ping(_) | Message::Pong(_) | Message::Binary(_) | Message::Frame(_))) => {}
                _ => return Ended::Again,
            }
        }

        let mut variables = Map::new();
        variables.insert("id".into(), Value::from(id));
        let mut payload = Map::new();
        payload.insert("id".into(), Value::from(self.changed.as_str()));
        payload.insert("variables".into(), Value::Object(variables));
        let mut subscribe = Map::new();
        subscribe.insert("id".into(), Value::from("1"));
        subscribe.insert("type".into(), Value::from("subscribe"));
        subscribe.insert("payload".into(), Value::Object(payload));
        if socket
            .send(Message::text(Value::Object(subscribe).to_json()))
            .await
            .is_err()
        {
            return Ended::Again;
        }

        let ended = loop {
            let text = match socket.next().await {
                Some(Ok(Message::Text(text))) => text,
                Some(Ok(Message::Ping(_) | Message::Pong(_) | Message::Binary(_) | Message::Frame(_))) => continue,
                _ => break Ended::Again,
            };
            let Ok(message) = Value::parse(text.as_str()) else {
                continue;
            };
            match message.get("type").and_then(|t| t.as_str()) {
                Some("ping") => {
                    let _ = socket.send(Message::text(r#"{"type":"pong"}"#)).await;
                }
                Some("next") => {
                    if let Some(ended) = self.on_next(message.get("payload"), sink, attempt) {
                        break ended;
                    }
                }
                Some("error") | Some("complete") => break Ended::Again,
                _ => {}
            }
        };
        let _ = socket.close(None).await;
        ended
    }

    // One event of the stream: a message for the controller, nothing, or the end of this subscription.
    fn on_next(&self, payload: Option<&Value>, sink: &StreamSink, attempt: &mut usize) -> Option<Ended> {
        let payload = payload?;
        if let Some(error) = payload.get("errors").and_then(|e| e.as_array()).and_then(|e| e.first()) {
            let code = error
                .get("extensions")
                .and_then(|x| x.get("code"))
                .and_then(|c| c.as_str());
            if code == Some(codes::CLIENT_OUTDATED) {
                sink(StreamMessage::ClientOutdated);
                return Some(Ended::ForGood);
            }
            return Some(Ended::Again);
        }
        let change = payload
            .get("data")
            .and_then(|d| d.get(&self.push_field))
            .filter(|c| !c.is_null())?;
        if let Some(problem) = change.get("problem").and_then(problem_of) {
            if problem.code == codes::CLIENT_OUTDATED {
                sink(StreamMessage::ClientOutdated);
                return Some(Ended::ForGood);
            }
            if problem.code == codes::NOT_FOUND {
                sink(StreamMessage::Gone { not_found: true });
                return Some(Ended::ForGood);
            }
            return Some(Ended::Again);
        }
        if change.get("gone").and_then(|g| g.as_bool()) == Some(true) {
            sink(StreamMessage::Gone { not_found: false });
            return Some(Ended::ForGood);
        }
        if change.get("reload").and_then(|r| r.as_bool()) == Some(true) {
            sink(StreamMessage::Reload);
            return None;
        }
        let snapshot = snapshot_of(change).filter(|s| s.revision.is_some())?;
        *attempt = 0;
        sink(StreamMessage::Changed(snapshot));
        None
    }
}

// An endpoint must be over TLS unless its host is this machine (localhost, 127.0.0.0/8, ::1) or the caller accepts
// cleartext. The error names the scheme and host only, never the path or the query.
fn require_secure(endpoint: &str, secure_scheme: &str, allow_insecure: bool) -> Result<(), ConfigError> {
    let url =
        reqwest::Url::parse(endpoint).map_err(|e| ConfigError(format!("The endpoint is not an absolute URL: {e}.")))?;
    if allow_insecure || url.scheme() == secure_scheme {
        return Ok(());
    }
    let host = url.host_str().unwrap_or("");
    let loopback = host.eq_ignore_ascii_case("localhost")
        || host.parse::<std::net::Ipv4Addr>().is_ok_and(|ip| ip.is_loopback())
        || host
            .strip_prefix('[')
            .and_then(|h| h.strip_suffix(']'))
            .and_then(|h| h.parse::<std::net::Ipv6Addr>().ok())
            .is_some_and(|ip| ip.is_loopback());
    if loopback {
        return Ok(());
    }
    let port = url.port().map(|p| format!(":{p}")).unwrap_or_default();
    Err(ConfigError(format!(
        "The endpoint {}://{host}{port} is not over TLS, so whatever authenticates the client would travel in \
         cleartext. Use {secure_scheme}://, a loopback host, or set allow_insecure_transport when the network is \
         trusted.",
        url.scheme()
    )))
}

/// The variables a document declares (`$id: UUID!` declares `id`).
fn declared_variables(document: &str) -> HashSet<String> {
    let mut names = HashSet::new();
    let mut rest = document;
    while let Some(at) = rest.find('$') {
        rest = &rest[at + 1..];
        let name: String = rest
            .chars()
            .take_while(|c| c.is_ascii_alphanumeric() || *c == '_')
            .collect();
        if !name.is_empty() && rest[name.len()..].trim_start().starts_with(':') {
            names.insert(name);
        }
    }
    names
}

/// The machine's field under `machines`: its kebab-case id in camelCase (`topic-map` is `topicMap`).
pub fn machine_field(id: &str) -> String {
    id.split('-')
        .filter(|p| !p.is_empty())
        .enumerate()
        .map(|(i, p)| {
            if i == 0 {
                p.to_owned()
            } else {
                p[..1].to_uppercase() + &p[1..]
            }
        })
        .collect()
}

/// An event's field: the trigger in camelCase, as the server names it. The leading run of capitals is lowered, keeping
/// the last one of a run a lowercase letter follows (`ID` is `id`, `URLPath` is `urlPath`).
pub fn event_field(trigger: &str) -> String {
    let upper = trigger.chars().take_while(|c| c.is_ascii_uppercase()).count();
    if upper == 0 {
        return trigger.to_owned();
    }
    if upper == 1 || upper == trigger.len() {
        return trigger[..upper].to_lowercase() + &trigger[upper..];
    }
    trigger[..upper - 1].to_lowercase() + &trigger[upper - 1..]
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fields_are_named_as_the_server_names_them() {
        assert_eq!(machine_field("topic-map"), "topicMap");
        assert_eq!(event_field("CancelBuild"), "cancelBuild");
        assert_eq!(event_field("ID"), "id");
        assert_eq!(event_field("URLPath"), "urlPath");
        assert_eq!(event_field("coin"), "coin");
    }

    #[test]
    fn an_answer_without_a_revision_has_none() {
        let answer = Value::parse(r#"{"id":"i-1","revision":null,"state":"Locked","context":{}}"#).unwrap();
        let ServerAnswer::Stored { snapshot, .. } = answer_of(&answer).unwrap() else {
            panic!("expected a snapshot");
        };
        assert_eq!(snapshot.revision, None);
    }

    #[test]
    fn a_document_declares_its_variables() {
        let declared =
            declared_variables("mutation Op($id: UUID!, $input: CoinInput!, $requestId: String!) { x(id: $id) }");
        assert_eq!(
            declared,
            ["id", "input", "requestId"].iter().map(|s| s.to_string()).collect()
        );
    }

    fn options(endpoint: &str, socket: &str) -> GraphQLMachineServerOptions {
        GraphQLMachineServerOptions::new(
            endpoint,
            socket,
            "turnstile",
            vec![ManifestDocument {
                id: "changed-1".into(),
                operation: "changed".into(),
                document: "subscription Changed($id: UUID!) { x }".into(),
            }],
        )
    }

    #[test]
    fn a_cleartext_endpoint_on_another_host_is_refused() {
        for (endpoint, socket) in [
            ("http://api.example.com/graphql", "wss://api.example.com/graphql"),
            ("https://api.example.com/graphql", "ws://api.example.com/graphql"),
            ("http://10.0.0.5/graphql", "wss://10.0.0.5/graphql"),
            ("https://[2001:db8::1]/graphql", "ws://[2001:db8::1]/graphql"),
        ] {
            let Err(error) = GraphQLMachineServer::new(options(endpoint, socket)) else {
                panic!("{endpoint} {socket} was accepted");
            };
            assert!(error.0.contains("not over TLS"), "{}", error.0);
        }
    }

    #[test]
    fn the_refusal_names_the_host_but_not_the_query() {
        let Err(error) = GraphQLMachineServer::new(options(
            "http://api.example.com:8080/graphql?access_token=SECRET",
            "wss://api.example.com/graphql",
        )) else {
            panic!("accepted");
        };
        assert!(error.0.contains("http://api.example.com:8080"), "{}", error.0);
        assert!(!error.0.contains("SECRET"), "{}", error.0);
    }

    #[test]
    fn tls_or_a_loopback_host_is_accepted() {
        for (endpoint, socket) in [
            ("https://api.example.com/graphql", "wss://api.example.com/graphql"),
            ("http://localhost:5000/graphql", "ws://localhost:5000/graphql"),
            ("http://127.0.0.1:5000/graphql", "ws://127.0.0.1:5000/graphql"),
            ("http://127.4.5.6/graphql", "ws://127.4.5.6/graphql"),
            ("http://[::1]:5000/graphql", "ws://[::1]:5000/graphql"),
        ] {
            assert!(
                GraphQLMachineServer::new(options(endpoint, socket)).is_ok(),
                "{endpoint} {socket}"
            );
        }
    }

    #[test]
    fn the_caller_may_accept_cleartext_explicitly() {
        let mut given = options("http://api.example.com/graphql", "ws://api.example.com/graphql");
        given.allow_insecure_transport = true;
        assert!(GraphQLMachineServer::new(given).is_ok());
    }

    #[tokio::test]
    async fn a_failed_request_never_says_its_url() {
        let mut given = options(
            "http://127.0.0.1:9/graphql?access_token=SECRET123",
            "ws://127.0.0.1:9/graphql",
        );
        given.documents.push(ManifestDocument {
            id: "get-1".into(),
            operation: "get".into(),
            document: "query Get($id: UUID!) { x }".into(),
        });
        let server = GraphQLMachineServer::new(given).unwrap();

        let Err(error) = server.get("i-1".into()).await else {
            panic!("nothing listens on port 9");
        };

        assert!(!error.0.contains("SECRET123"), "{}", error.0);
        assert!(!error.0.contains("127.0.0.1"), "{}", error.0);
    }
}
