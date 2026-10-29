//! The [`MachineServer`] over a host's typed `machines` root: each operation is a persisted document of the host's
//! client manifest, sent by its id with its values as variables, and the change stream is the machine's
//! `<machine>Changed` subscription, subscribed by its id over graphql-transport-ws.

use std::collections::{HashMap, HashSet};
use std::future::Future;
use std::pin::Pin;
use std::sync::Arc;
use std::time::Duration;

use futures_util::{SinkExt, StreamExt};
use reqwest::header::HeaderMap;
use tokio::task::JoinHandle;
use tokio_tungstenite::tungstenite::client::IntoClientRequest;
use tokio_tungstenite::tungstenite::http::HeaderValue;
use tokio_tungstenite::tungstenite::protocol::{CloseFrame, WebSocketConfig};
use tokio_tungstenite::tungstenite::Message;
use tokio_tungstenite::{MaybeTlsStream, WebSocketStream};
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

/// What a provider answers with: a future, run once each time the value is needed.
pub type Provided<T> = Pin<Box<dyn Future<Output = Result<T, TransportError>> + Send>>;

/// The `connection_init` payload, asked for on every connection so a rotating token stays fresh; `None` sends none.
/// An error fails that connection, which is tried again after the resubscribe delays.
pub type ConnectionParams = Arc<dyn Fn() -> Provided<Option<Value>> + Send + Sync>;

/// Headers asked for on every request and every socket's upgrade, so a rotating token stays fresh. An error fails
/// that request as a [`TransportError`].
pub type RequestHeaders = Arc<dyn Fn() -> Provided<HeaderMap> + Send + Sync>;

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
    /// The client requests go through. When `None`, one that follows no redirect and, for an `https` endpoint, sends
    /// nothing but `https`. Build a client of your own with `redirect(Policy::none())` too: a redirect would carry the
    /// request's headers to whatever host it names. An answer from another origin than the endpoint's, or a redirect,
    /// fails the request as a [`TransportError`] whichever client sent it.
    pub http: Option<reqwest::Client>,
    /// Headers for each request and each socket's upgrade, asked for every time: how a host authenticates a request.
    pub headers: Option<RequestHeaders>,
    /// The `connection_init` payload, asked for on every connection: how a host authenticates a socket.
    pub connection_params: Option<ConnectionParams>,
    /// Delays before subscribing again after a stream that ended without saying why. The last repeats. A socket the
    /// host refused (closed with 4401 or 4403, or an upgrade answered 401 or 403) is not subscribed again: the stream
    /// delivers [`StreamMessage::Refused`] and ends.
    pub resubscribe_delays: Option<Vec<Duration>>,
    /// How long a socket may take to connect and be acknowledged before the attempt counts as failed and is tried
    /// again after the resubscribe delays. [`DEFAULT_CONNECT_TIMEOUT`] (10 seconds) by default.
    pub connect_timeout: Duration,
    /// How long one request may take, from sending it to reading the last byte of its answer, before it fails as a
    /// [`TransportError`]. [`DEFAULT_REQUEST_TIMEOUT`] (30 seconds) by default.
    pub request_timeout: Duration,
    /// How long an acknowledged socket may stay silent before it is given up and subscribed again after the
    /// resubscribe delays, so a connection dropped without a close (a NAT that forgot it) is noticed. Halfway through
    /// the silence the client sends a `ping`, which a live host answers with a `pong`. [`DEFAULT_IDLE_TIMEOUT`] (60
    /// seconds) by default.
    pub idle_timeout: Duration,
    /// The largest response body, and the largest socket message or frame, the host may send, in bytes. A larger
    /// response fails its request as a [`TransportError`]; a larger message ends the stream, which is subscribed again.
    /// [`DEFAULT_MAX_MESSAGE_SIZE`] (16 MiB) by default.
    pub max_message_size: usize,
    /// Accepts an `http` endpoint or a `ws` socket on a host that is not loopback. Off by default: whatever
    /// authenticates the client would travel in cleartext.
    pub allow_insecure_transport: bool,
    /// The tokio runtime each subscription's task runs on. `None` takes the runtime `new` is called from, or failing
    /// that the one `subscribe` is called from; a subscription asked for outside any is refused rather than panicking.
    pub runtime: Option<tokio::runtime::Handle>,
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
            headers: None,
            connection_params: None,
            resubscribe_delays: None,
            connect_timeout: DEFAULT_CONNECT_TIMEOUT,
            request_timeout: DEFAULT_REQUEST_TIMEOUT,
            idle_timeout: DEFAULT_IDLE_TIMEOUT,
            max_message_size: DEFAULT_MAX_MESSAGE_SIZE,
            allow_insecure_transport: false,
            runtime: None,
        }
    }
}

type Socket = WebSocketStream<MaybeTlsStream<tokio::net::TcpStream>>;

/// The largest response body or socket message a host may send, in bytes, by default: 16 MiB.
pub const DEFAULT_MAX_MESSAGE_SIZE: usize = 16 * 1024 * 1024;

/// How long a socket may take to connect and be acknowledged before the attempt counts as failed, by default.
pub const DEFAULT_CONNECT_TIMEOUT: Duration = Duration::from_secs(10);

/// How long one request may take before it fails, by default.
pub const DEFAULT_REQUEST_TIMEOUT: Duration = Duration::from_secs(30);

/// How long an acknowledged socket may stay silent before it is given up, by default.
pub const DEFAULT_IDLE_TIMEOUT: Duration = Duration::from_secs(60);

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
    endpoint_url: reqwest::Url,
    socket: String,
    machine: String,
    field: String,
    push_field: String,
    documents: HashMap<String, Document>,
    changed: String,
    headers: Option<RequestHeaders>,
    connection_params: Option<ConnectionParams>,
    resubscribe_delays: Vec<Duration>,
    connect_timeout: Duration,
    request_timeout: Duration,
    idle_timeout: Duration,
    max_message_size: usize,
    runtime: Option<tokio::runtime::Handle>,
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
        let endpoint_url = reqwest::Url::parse(&options.endpoint)
            .map_err(|e| ConfigError(format!("The endpoint is not an absolute URL: {e}.")))?;
        let http = match options.http {
            Some(http) => http,
            None => reqwest::Client::builder()
                .redirect(reqwest::redirect::Policy::none())
                .https_only(!options.allow_insecure_transport && endpoint_url.scheme() == "https")
                .build()
                .map_err(|e| ConfigError(format!("The HTTP client could not be built: {}", e.without_url())))?,
        };
        let delays = match options.resubscribe_delays {
            Some(delays) if !delays.is_empty() => delays,
            _ => DEFAULT_RESUBSCRIBE_DELAYS.to_vec(),
        };
        Ok(GraphQLMachineServer {
            inner: Arc::new(Inner {
                http,
                endpoint: options.endpoint,
                endpoint_url,
                socket: options.socket,
                push_field: format!("{field}Changed"),
                field,
                machine: options.machine,
                documents,
                changed,
                headers: options.headers,
                connection_params: options.connection_params,
                resubscribe_delays: delays,
                connect_timeout: options.connect_timeout,
                request_timeout: options.request_timeout,
                idle_timeout: options.idle_timeout,
                max_message_size: options.max_message_size,
                runtime: options.runtime.or_else(|| tokio::runtime::Handle::try_current().ok()),
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

        // The whole exchange, asking for the headers, sending and reading the answer, within the request timeout: a
        // host or a token refresh that never answers fails the request rather than holding it forever.
        let exchange = async {
            let mut request = self
                .http
                .post(&self.endpoint)
                .header("content-type", "application/json")
                .header("accept", "application/json");
            if let Some(headers) = &self.headers {
                request = request.headers(headers().await?);
            }
            let request = request.body(Value::Object(body).to_json());
            let mut response = request.send().await.map_err(transport_error)?;
            let status = response.status();
            // A redirect is not followed, and an answer a client reached by following one is not the endpoint's.
            if status.is_redirection() {
                return Err(TransportError(format!(
                    "The server answered with a redirect ({}), which is not followed.",
                    status.as_u16()
                )));
            }
            if response.url().origin() != self.endpoint_url.origin() {
                return Err(TransportError(
                    "The request was redirected to another origin than the endpoint's, so its answer is not read."
                        .to_owned(),
                ));
            }
            // Read up to the limit, whether or not the host said the length up front.
            let too_large = || {
                TransportError(format!(
                    "The server answered with a body larger than {} bytes.",
                    self.max_message_size
                ))
            };
            if response
                .content_length()
                .is_some_and(|n| n > self.max_message_size as u64)
            {
                return Err(too_large());
            }
            let mut bytes = Vec::new();
            while let Some(chunk) = response.chunk().await.map_err(transport_error)? {
                if bytes.len() + chunk.len() > self.max_message_size {
                    return Err(too_large());
                }
                bytes.extend_from_slice(&chunk);
            }
            Ok::<_, TransportError>((status, bytes))
        };
        let (status, bytes) = tokio::time::timeout(self.request_timeout, exchange)
            .await
            .map_err(|_| {
                TransportError(format!(
                    "The server did not answer within {} ms.",
                    self.request_timeout.as_millis()
                ))
            })??;
        let text = String::from_utf8_lossy(&bytes);
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
    let snapshot = snapshot_of(answer)?;
    Ok(ServerAnswer::Stored {
        id: answer.get("id").and_then(|i| i.as_str()).map(str::to_owned),
        snapshot,
    })
}

fn snapshot_of(value: &Value) -> Result<ServerSnapshot, TransportError> {
    let state = value
        .get("state")
        .and_then(|s| s.as_str())
        .ok_or_else(|| TransportError("The server answered a snapshot without a state.".to_owned()))?
        .to_owned();
    // A revision the store does not record stays `None`: it is not revision 0, which would order every later answer
    // below it. One that is not an integer a double holds exactly is refused, not rounded: a revision read as larger
    // than it is would make every later snapshot look stale.
    let revision = match value.get("revision") {
        None | Some(Value::Null) => None,
        Some(revision) => Some(revision_of(revision).ok_or_else(|| {
            TransportError(format!(
                "The server answered a revision that is not an integer: {}.",
                revision.to_json()
            ))
        })?),
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
    Ok(ServerSnapshot {
        row_id,
        revision,
        state,
        context,
        due_at,
    })
}

// The largest integer a double holds exactly, 2^53 - 1: past it a revision cannot be told from its neighbours.
const MAX_SAFE_INTEGER: f64 = 9_007_199_254_740_991.0;

fn revision_of(value: &Value) -> Option<i64> {
    match value {
        Value::Number(n) if n.fract() == 0.0 && n.abs() <= MAX_SAFE_INTEGER => Some(*n as i64),
        _ => None,
    }
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
        let Some(runtime) = self
            .inner
            .runtime
            .clone()
            .or_else(|| tokio::runtime::Handle::try_current().ok())
        else {
            sink(StreamMessage::Refused(ServerProblem {
                code: codes::TRANSPORT_ERROR.to_owned(),
                message: "The stream was asked for outside a tokio runtime, and the server's options name none."
                    .to_owned(),
            }));
            return Box::new(Stream(None));
        };
        let inner = self.inner.clone();
        Box::new(Stream(Some(runtime.spawn(async move { inner.stream(id, sink).await }))))
    }
}

// The task that follows one instance; dropping the handle stops it and closes its socket.
struct Stream(Option<JoinHandle<()>>);

impl Subscription for Stream {}

impl Drop for Stream {
    fn drop(&mut self) {
        if let Some(task) = &self.0 {
            task.abort();
        }
    }
}

// How one subscription over one socket ended.
enum Ended {
    // For good: the instance is gone, this client is outdated, or the host refused the socket. Nothing is subscribed
    // again.
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

    // Opens a socket and has it acknowledged. `Err(Some)` when the host refused it (a 401 or 403 upgrade, or a close
    // with 4401 or 4403 before the acknowledgement), `Err(None)` when it could not be opened for any other reason.
    async fn connect(&self) -> Result<Socket, Option<ServerProblem>> {
        let Ok(mut request) = self.socket.as_str().into_client_request() else {
            return Err(None);
        };
        if let Some(headers) = &self.headers {
            let Ok(headers) = headers().await else {
                return Err(None);
            };
            request.headers_mut().extend(headers);
        }
        request.headers_mut().insert(
            "Sec-WebSocket-Protocol",
            HeaderValue::from_static("graphql-transport-ws"),
        );
        let config = WebSocketConfig::default()
            .max_message_size(Some(self.max_message_size))
            .max_frame_size(Some(self.max_message_size));
        let mut socket = match tokio_tungstenite::connect_async_with_config(request, Some(config), false).await {
            Ok((socket, _)) => socket,
            Err(tokio_tungstenite::tungstenite::Error::Http(response)) => {
                return Err(refused_upgrade(response.status().as_u16()));
            }
            Err(_) => return Err(None),
        };

        // Asked for on every connection, so a token that expired since the last one is not sent again.
        let mut init = Map::new();
        init.insert("type".into(), Value::from("connection_init"));
        if let Some(params) = &self.connection_params {
            match params().await {
                Ok(Some(payload)) => {
                    init.insert("payload".into(), payload);
                }
                Ok(None) => {}
                Err(_) => {
                    let _ = socket.close(None).await;
                    return Err(None);
                }
            }
        }
        if socket.send(Message::text(Value::Object(init).to_json())).await.is_err() {
            return Err(None);
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
                Some(Ok(Message::Close(frame))) => return Err(refused_close(frame.as_ref())),
                _ => return Err(None),
            }
        }
        Ok(socket)
    }

    async fn subscribe_once(&self, id: &str, sink: &StreamSink, attempt: &mut usize) -> Ended {
        // A host that accepts the connection but never acknowledges it is tried again; one that refused it is not.
        let mut socket = match tokio::time::timeout(self.connect_timeout, self.connect()).await {
            Ok(Ok(socket)) => socket,
            Ok(Err(Some(refusal))) => {
                sink(StreamMessage::Refused(refusal));
                return Ended::ForGood;
            }
            Ok(Err(None)) | Err(_) => return Ended::Again,
        };

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

        // A host that says nothing for half the idle limit is pinged; one that says nothing for the rest of it is gone.
        let mut pinged = false;
        let ended = loop {
            let next = match tokio::time::timeout(self.idle_timeout / 2, socket.next()).await {
                Ok(next) => {
                    pinged = false;
                    next
                }
                Err(_) if !pinged => {
                    pinged = true;
                    if socket.send(Message::text(r#"{"type":"ping"}"#)).await.is_err() {
                        break Ended::Again;
                    }
                    continue;
                }
                Err(_) => break Ended::Again,
            };
            let text = match next {
                Some(Ok(Message::Text(text))) => text,
                Some(Ok(Message::Ping(_) | Message::Pong(_) | Message::Binary(_) | Message::Frame(_))) => continue,
                Some(Ok(Message::Close(frame))) => match refused_close(frame.as_ref()) {
                    Some(refusal) => {
                        sink(StreamMessage::Refused(refusal));
                        break Ended::ForGood;
                    }
                    None => break Ended::Again,
                },
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
        let snapshot = snapshot_of(change).ok().filter(|s| s.revision.is_some())?;
        *attempt = 0;
        sink(StreamMessage::Changed(snapshot));
        None
    }
}

// The refusal a close frame is when the host turned the caller away with it: 4401 is `UNAUTHENTICATED`, 4403
// `FORBIDDEN`. Subscribing again would get the same answer. Any other close, or none, is `None`.
fn refused_close(frame: Option<&CloseFrame>) -> Option<ServerProblem> {
    let frame = frame?;
    let close = u16::from(frame.code);
    let code = refusal_code(close, 4401, 4403)?;
    let reason: &str = &frame.reason;
    let message = if reason.is_empty() {
        format!("The host refused the socket: {close}.")
    } else {
        format!("The host refused the socket: {close} {reason}.")
    };
    Some(ServerProblem {
        code: code.to_owned(),
        message,
    })
}

// The refusal an upgrade's status is when the host turned the caller away with it: 401 or 403.
fn refused_upgrade(status: u16) -> Option<ServerProblem> {
    let code = refusal_code(status, 401, 403)?;
    Some(ServerProblem {
        code: code.to_owned(),
        message: format!("The host refused the socket's upgrade: {status}."),
    })
}

fn refusal_code(status: u16, unauthorized: u16, forbidden: u16) -> Option<&'static str> {
    if status == unauthorized {
        Some(codes::UNAUTHENTICATED)
    } else if status == forbidden {
        Some(codes::FORBIDDEN)
    } else {
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
                // By character, not byte, so a part that starts with a character of several bytes is upper-cased whole.
                let mut chars = p.chars();
                chars
                    .next()
                    .map(|c| c.to_uppercase().chain(chars).collect())
                    .unwrap_or_default()
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

    // A loopback HTTP host that answers every request with `answer` and counts the requests it received.
    async fn http_host(answer: String) -> (u16, Arc<std::sync::atomic::AtomicUsize>) {
        use std::sync::atomic::Ordering;
        use tokio::io::{AsyncReadExt, AsyncWriteExt};

        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let received = Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let counted = received.clone();
        tokio::spawn(async move {
            loop {
                let Ok((mut tcp, _)) = listener.accept().await else {
                    return;
                };
                let counted = counted.clone();
                let answer = answer.clone();
                tokio::spawn(async move {
                    let mut read = Vec::new();
                    let mut buffer = [0u8; 4096];
                    let head = loop {
                        if let Some(at) = read.windows(4).position(|w| w == b"\r\n\r\n") {
                            break at;
                        }
                        match tcp.read(&mut buffer).await {
                            Ok(0) | Err(_) => return,
                            Ok(n) => read.extend_from_slice(&buffer[..n]),
                        }
                    };
                    let length = String::from_utf8_lossy(&read[..head])
                        .lines()
                        .filter_map(|line| line.split_once(':'))
                        .find(|(name, _)| name.trim().eq_ignore_ascii_case("content-length"))
                        .and_then(|(_, value)| value.trim().parse::<usize>().ok())
                        .unwrap_or(0);
                    while read.len() < head + 4 + length {
                        match tcp.read(&mut buffer).await {
                            Ok(0) | Err(_) => return,
                            Ok(n) => read.extend_from_slice(&buffer[..n]),
                        }
                    }
                    counted.fetch_add(1, Ordering::SeqCst);
                    let _ = tcp.write_all(answer.as_bytes()).await;
                    let _ = tcp.shutdown().await;
                });
            }
        });
        (port, received)
    }

    fn json_answer(body: &str) -> String {
        format!(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}",
            body.len()
        )
    }

    fn redirect_to(port: u16) -> String {
        format!(
            "HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{port}/graphql\r\nContent-Length: 0\r\n\
             Connection: close\r\n\r\n"
        )
    }

    fn at(endpoint: String) -> GraphQLMachineServerOptions {
        with_get(options(&endpoint, "ws://127.0.0.1:9/graphql"))
    }

    const NOT_FOUND: &str =
        r#"{"data":{"machines":{"turnstile":{"get":{"problem":{"code":"NOT_FOUND","message":"m"}}}}}}"#;

    #[tokio::test]
    async fn a_redirect_is_not_followed_and_the_other_host_receives_nothing() {
        use std::sync::atomic::Ordering;

        let (other, other_received) = http_host(json_answer(NOT_FOUND)).await;
        let (endpoint, endpoint_received) = http_host(redirect_to(other)).await;
        let server = GraphQLMachineServer::new(at(format!("http://127.0.0.1:{endpoint}/graphql"))).unwrap();

        let Err(error) = server.get("i-1".into()).await else {
            panic!("the redirect was answered");
        };

        assert!(error.0.contains("redirect"), "{}", error.0);
        assert_eq!(endpoint_received.load(Ordering::SeqCst), 1);
        assert_eq!(other_received.load(Ordering::SeqCst), 0);
    }

    #[tokio::test]
    async fn an_answer_a_client_reached_through_a_redirect_is_a_failure() {
        let (other, _) = http_host(json_answer(NOT_FOUND)).await;
        let (endpoint, _) = http_host(redirect_to(other)).await;
        let mut given = at(format!("http://127.0.0.1:{endpoint}/graphql"));
        // A client of the caller's own that follows redirects.
        given.http = Some(reqwest::Client::new());
        let server = GraphQLMachineServer::new(given).unwrap();

        let Err(error) = server.get("i-1".into()).await else {
            panic!("the answer from another origin was read");
        };

        assert!(error.0.contains("another origin"), "{}", error.0);
    }

    #[tokio::test]
    async fn an_answer_from_the_endpoint_itself_is_read() {
        let (endpoint, _) = http_host(json_answer(NOT_FOUND)).await;
        let server = GraphQLMachineServer::new(at(format!("http://127.0.0.1:{endpoint}/graphql"))).unwrap();

        let answer = server.get("i-1".into()).await.unwrap();

        assert!(matches!(answer, ServerAnswer::Refused(ServerProblem { code, .. }) if code == "NOT_FOUND"));
    }

    #[test]
    fn a_machine_id_whose_part_starts_with_a_character_of_several_bytes_is_named_without_a_panic() {
        assert_eq!(machine_field("topic-\u{e9}t\u{e9}"), "topic\u{c9}t\u{e9}");
        assert_eq!(machine_field("a-\u{1f600}x"), "a\u{1f600}x");
        assert_eq!(machine_field("map-2d"), "map2d");
    }

    // How the host answers one connection's connection_init.
    #[derive(Clone, Copy)]
    enum Reply {
        // Closes it with 4403, as a host refuses an expired token.
        Refuse,
        // Closes it with 1011, as a host that goes away does.
        Drop,
        // Answers the upgrade 403, before any connection_init.
        RejectUpgrade,
        Acknowledge,
        // Says nothing and holds the connection open.
        Silent,
        // Acknowledges it, then sends a 64 KiB message.
        Flood,
        // Acknowledges it, then answers every ping with a pong and says nothing else.
        Pong,
    }

    type Heard = tokio::sync::mpsc::UnboundedReceiver<(Option<String>, Option<String>)>;

    // A host on a loopback port that records each connection's authorization header and connection_init payload and
    // answers connection n (from 0) as `reply(n)` says.
    async fn recording_host(reply: fn(usize) -> Reply) -> (u16, Heard) {
        use tokio_tungstenite::tungstenite::handshake::server::{ErrorResponse, Request, Response};
        use tokio_tungstenite::tungstenite::http::StatusCode;
        use tokio_tungstenite::tungstenite::protocol::frame::coding::CloseCode;
        use tokio_tungstenite::tungstenite::protocol::CloseFrame;

        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let (seen, heard) = tokio::sync::mpsc::unbounded_channel();
        tokio::spawn(async move {
            for n in 0.. {
                let (tcp, _) = listener.accept().await.unwrap();
                let seen = seen.clone();
                let reply = reply(n);
                tokio::spawn(async move {
                    let mut authorization = None;
                    let accepted =
                        tokio_tungstenite::accept_hdr_async(tcp, |request: &Request, mut response: Response| {
                            authorization = request
                                .headers()
                                .get("authorization")
                                .map(|v| v.to_str().unwrap().to_owned());
                            if let Reply::RejectUpgrade = reply {
                                let mut rejected = ErrorResponse::new(Some("Forbidden".to_owned()));
                                *rejected.status_mut() = StatusCode::FORBIDDEN;
                                return Err(rejected);
                            }
                            response.headers_mut().insert(
                                "Sec-WebSocket-Protocol",
                                HeaderValue::from_static("graphql-transport-ws"),
                            );
                            Ok(response)
                        })
                        .await;
                    let Ok(mut socket) = accepted else {
                        let _ = seen.send((authorization, None));
                        return;
                    };
                    let Some(Ok(Message::Text(init))) = socket.next().await else {
                        return;
                    };
                    let token = Value::parse(init.as_str())
                        .unwrap()
                        .get("payload")
                        .and_then(|p| p.get("token"))
                        .and_then(|t| t.as_str())
                        .map(str::to_owned);
                    let _ = seen.send((authorization, token));
                    match reply {
                        Reply::RejectUpgrade => unreachable!("the upgrade was answered 403"),
                        Reply::Drop => {
                            let _ = socket
                                .close(Some(CloseFrame {
                                    code: CloseCode::Error,
                                    reason: "Going away".into(),
                                }))
                                .await;
                            return;
                        }
                        Reply::Acknowledge => {
                            let _ = socket.send(Message::text(r#"{"type":"connection_ack"}"#)).await;
                        }
                        Reply::Silent => {}
                        Reply::Pong => {
                            let _ = socket.send(Message::text(r#"{"type":"connection_ack"}"#)).await;
                            while let Some(Ok(message)) = socket.next().await {
                                if let Message::Text(text) = message {
                                    if text.as_str().contains(r#""type":"ping""#) {
                                        let _ = socket.send(Message::text(r#"{"type":"pong"}"#)).await;
                                    }
                                }
                            }
                            return;
                        }
                        Reply::Flood => {
                            let _ = socket.send(Message::text(r#"{"type":"connection_ack"}"#)).await;
                            let large = format!(r#"{{"type":"ping","payload":"{}"}}"#, "x".repeat(64 * 1024));
                            let _ = socket.send(Message::text(large)).await;
                        }
                        Reply::Refuse => {
                            let _ = socket
                                .close(Some(CloseFrame {
                                    code: CloseCode::Library(4403),
                                    reason: "Forbidden".into(),
                                }))
                                .await;
                            return;
                        }
                    }
                    while let Some(Ok(_)) = socket.next().await {}
                });
            }
        });
        (port, heard)
    }

    #[tokio::test]
    async fn every_connection_asks_for_its_headers_and_params_again() {
        use std::sync::atomic::{AtomicU32, Ordering};

        let (port, mut heard) = recording_host(|n| if n == 0 { Reply::Drop } else { Reply::Acknowledge }).await;
        let mut given = options(
            &format!("http://127.0.0.1:{port}/graphql"),
            &format!("ws://127.0.0.1:{port}/graphql"),
        );
        let headers_asked = Arc::new(AtomicU32::new(0));
        let params_asked = Arc::new(AtomicU32::new(0));
        let asked = headers_asked.clone();
        given.headers = Some(Arc::new(move || {
            let n = asked.fetch_add(1, Ordering::SeqCst) + 1;
            Box::pin(async move {
                let mut headers = HeaderMap::new();
                headers.insert("authorization", HeaderValue::from_str(&format!("Bearer {n}")).unwrap());
                Ok(headers)
            })
        }));
        let asked = params_asked.clone();
        given.connection_params = Some(Arc::new(move || {
            let n = asked.fetch_add(1, Ordering::SeqCst) + 1;
            Box::pin(async move {
                let mut payload = Map::new();
                payload.insert("token".into(), Value::from(format!("t{n}")));
                Ok(Some(Value::Object(payload)))
            })
        }));
        given.resubscribe_delays = Some(vec![Duration::from_millis(10)]);
        let server = GraphQLMachineServer::new(given).unwrap();

        let _stream = server.subscribe("i-1".into(), Arc::new(|_| {}));
        let wait = Duration::from_secs(10);
        let first = tokio::time::timeout(wait, heard.recv()).await.unwrap().unwrap();
        let second = tokio::time::timeout(wait, heard.recv()).await.unwrap().unwrap();

        assert_eq!(first, (Some("Bearer 1".into()), Some("t1".into())));
        assert_eq!(second, (Some("Bearer 2".into()), Some("t2".into())));
    }

    // Follows "i-1" on a host that answers each connection as `reply` says, and hands back what the stream delivers.
    async fn follow_refused(
        reply: fn(usize) -> Reply,
    ) -> (
        Heard,
        tokio::sync::mpsc::UnboundedReceiver<StreamMessage>,
        Box<dyn Subscription>,
    ) {
        let (port, heard) = recording_host(reply).await;
        let mut given = options(
            &format!("http://127.0.0.1:{port}/graphql"),
            &format!("ws://127.0.0.1:{port}/graphql"),
        );
        given.resubscribe_delays = Some(vec![Duration::from_millis(10)]);
        let server = GraphQLMachineServer::new(given).unwrap();
        let (deliver, delivered) = tokio::sync::mpsc::unbounded_channel();
        let stream = server.subscribe(
            "i-1".into(),
            Arc::new(move |message| {
                let _ = deliver.send(message);
            }),
        );
        (heard, delivered, stream)
    }

    #[tokio::test]
    async fn a_socket_the_host_closes_with_4403_ends_the_stream_for_good() {
        let (mut heard, mut delivered, _stream) = follow_refused(|_| Reply::Refuse).await;
        let wait = Duration::from_secs(10);

        tokio::time::timeout(wait, heard.recv()).await.unwrap().unwrap();
        let message = tokio::time::timeout(wait, delivered.recv()).await.unwrap().unwrap();
        let again = tokio::time::timeout(Duration::from_millis(200), heard.recv()).await;

        assert_eq!(
            message,
            StreamMessage::Refused(ServerProblem {
                code: "FORBIDDEN".into(),
                message: "The host refused the socket: 4403 Forbidden.".into(),
            })
        );
        assert!(again.is_err(), "the refused socket was opened again");
    }

    #[tokio::test]
    async fn an_upgrade_the_host_answers_403_ends_the_stream_for_good() {
        let (mut heard, mut delivered, _stream) = follow_refused(|_| Reply::RejectUpgrade).await;
        let wait = Duration::from_secs(10);

        tokio::time::timeout(wait, heard.recv()).await.unwrap().unwrap();
        let message = tokio::time::timeout(wait, delivered.recv()).await.unwrap().unwrap();
        let again = tokio::time::timeout(Duration::from_millis(200), heard.recv()).await;

        assert_eq!(
            message,
            StreamMessage::Refused(ServerProblem {
                code: "FORBIDDEN".into(),
                message: "The host refused the socket's upgrade: 403.".into(),
            })
        );
        assert!(again.is_err(), "the refused socket was opened again");
    }

    #[tokio::test]
    async fn a_socket_the_host_closes_with_another_code_is_opened_again() {
        let (mut heard, mut delivered, _stream) =
            follow_refused(|n| if n == 0 { Reply::Drop } else { Reply::Silent }).await;
        let wait = Duration::from_secs(10);

        tokio::time::timeout(wait, heard.recv()).await.unwrap().unwrap();
        let again = tokio::time::timeout(wait, heard.recv()).await;

        assert!(again.is_ok(), "the dropped socket was never opened again");
        assert!(delivered.try_recv().is_err(), "a dropped socket delivered a message");
    }

    #[test]
    fn only_4401_and_4403_are_refusals() {
        use tokio_tungstenite::tungstenite::protocol::frame::coding::CloseCode;
        let frame = |code: u16| CloseFrame {
            code: CloseCode::from(code),
            reason: "".into(),
        };

        assert_eq!(
            refused_close(Some(&frame(4401))).map(|p| p.code),
            Some("UNAUTHENTICATED".to_owned())
        );
        assert_eq!(
            refused_close(Some(&frame(4403))),
            Some(ServerProblem {
                code: "FORBIDDEN".into(),
                message: "The host refused the socket: 4403.".into(),
            })
        );
        assert_eq!(refused_close(Some(&frame(1011))), None);
        assert_eq!(refused_close(Some(&frame(4500))), None);
        assert_eq!(refused_close(None), None);
        assert_eq!(refused_upgrade(401).map(|p| p.code), Some("UNAUTHENTICATED".to_owned()));
        assert_eq!(refused_upgrade(502), None);
    }

    #[tokio::test]
    async fn a_connection_never_acknowledged_is_given_up_after_the_connect_timeout() {
        let (port, mut heard) = recording_host(|_| Reply::Silent).await;
        let mut given = options(
            &format!("http://127.0.0.1:{port}/graphql"),
            &format!("ws://127.0.0.1:{port}/graphql"),
        );
        given.connect_timeout = Duration::from_millis(100);
        given.resubscribe_delays = Some(vec![Duration::from_millis(10)]);
        let server = GraphQLMachineServer::new(given).unwrap();

        let _stream = server.subscribe("i-1".into(), Arc::new(|_| {}));
        let wait = Duration::from_secs(10);
        tokio::time::timeout(wait, heard.recv()).await.unwrap().unwrap();
        let again = tokio::time::timeout(wait, heard.recv()).await;

        assert!(again.is_ok(), "the first connection was never given up");
    }

    #[test]
    fn the_connect_timeout_is_ten_seconds_by_default() {
        let given = options("https://api.example.com/graphql", "wss://api.example.com/graphql");
        assert_eq!(given.connect_timeout, Duration::from_secs(10));
    }

    // A host on a loopback port that answers every request with a body of `size` bytes, with or without its length.
    async fn large_answer_host(size: usize, say_length: bool) -> u16 {
        use tokio::io::{AsyncReadExt, AsyncWriteExt};

        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        tokio::spawn(async move {
            loop {
                let (mut tcp, _) = listener.accept().await.unwrap();
                tokio::spawn(async move {
                    let mut request = Vec::new();
                    let mut buffer = [0u8; 4096];
                    while !request.windows(4).any(|w| w == b"\r\n\r\n") {
                        let Ok(n) = tcp.read(&mut buffer).await else { return };
                        if n == 0 {
                            return;
                        }
                        request.extend_from_slice(&buffer[..n]);
                    }
                    let body = format!(r#"{{"data":{{"pad":"{}"}}}}"#, "x".repeat(size));
                    let head = if say_length {
                        format!(
                            "HTTP/1.1 200 OK\r\ncontent-length: {}\r\nconnection: close\r\n\r\n",
                            body.len()
                        )
                    } else {
                        "HTTP/1.1 200 OK\r\nconnection: close\r\n\r\n".to_owned()
                    };
                    let _ = tcp.write_all(head.as_bytes()).await;
                    let _ = tcp.write_all(body.as_bytes()).await;
                    let _ = tcp.shutdown().await;
                });
            }
        });
        port
    }

    fn with_get(mut given: GraphQLMachineServerOptions) -> GraphQLMachineServerOptions {
        given.documents.push(ManifestDocument {
            id: "get-1".into(),
            operation: "get".into(),
            document: "query Get($id: UUID!) { x }".into(),
        });
        given
    }

    #[tokio::test]
    async fn a_response_larger_than_the_limit_fails_the_request() {
        for say_length in [true, false] {
            let port = large_answer_host(64 * 1024, say_length).await;
            let mut given = with_get(options(
                &format!("http://127.0.0.1:{port}/graphql"),
                &format!("ws://127.0.0.1:{port}/graphql"),
            ));
            given.max_message_size = 1024;
            let server = GraphQLMachineServer::new(given).unwrap();

            let Err(error) = server.get("i-1".into()).await else {
                panic!("a body over the limit was read");
            };

            assert!(error.0.contains("larger than 1024"), "{}", error.0);
        }
    }

    #[tokio::test]
    async fn a_response_within_the_limit_is_read() {
        let port = large_answer_host(512, false).await;
        let mut given = with_get(options(
            &format!("http://127.0.0.1:{port}/graphql"),
            &format!("ws://127.0.0.1:{port}/graphql"),
        ));
        given.max_message_size = 1024;
        let server = GraphQLMachineServer::new(given).unwrap();

        let answer = server.get("i-1".into()).await.unwrap();

        assert!(matches!(answer, ServerAnswer::Refused(p) if p.code == codes::NOT_FOUND));
    }

    #[tokio::test]
    async fn a_socket_message_larger_than_the_limit_ends_the_stream() {
        let (port, mut heard) = recording_host(|n| if n == 0 { Reply::Flood } else { Reply::Silent }).await;
        let mut given = options(
            &format!("http://127.0.0.1:{port}/graphql"),
            &format!("ws://127.0.0.1:{port}/graphql"),
        );
        given.max_message_size = 1024;
        given.resubscribe_delays = Some(vec![Duration::from_millis(10)]);
        let server = GraphQLMachineServer::new(given).unwrap();

        let _stream = server.subscribe("i-1".into(), Arc::new(|_| {}));
        let wait = Duration::from_secs(10);
        tokio::time::timeout(wait, heard.recv()).await.unwrap().unwrap();
        let again = tokio::time::timeout(wait, heard.recv()).await;

        assert!(again.is_ok(), "the stream outlived a message over the limit");
    }

    // A host on a loopback port that reads each request and never finishes answering it: with `send_head`, it sends
    // the status line and headers of a long body and then nothing more.
    async fn stalled_host(send_head: bool) -> u16 {
        use tokio::io::{AsyncReadExt, AsyncWriteExt};

        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        tokio::spawn(async move {
            loop {
                let (mut tcp, _) = listener.accept().await.unwrap();
                tokio::spawn(async move {
                    let mut buffer = [0u8; 4096];
                    let _ = tcp.read(&mut buffer).await;
                    if send_head {
                        let _ = tcp.write_all(b"HTTP/1.1 200 OK\r\ncontent-length: 1000\r\n\r\n{").await;
                    }
                    tokio::time::sleep(Duration::from_secs(60)).await;
                    drop(tcp);
                });
            }
        });
        port
    }

    #[tokio::test]
    async fn a_request_the_host_never_answers_fails_after_the_request_timeout() {
        for send_head in [false, true] {
            let port = stalled_host(send_head).await;
            let mut given = with_get(options(
                &format!("http://127.0.0.1:{port}/graphql"),
                &format!("ws://127.0.0.1:{port}/graphql"),
            ));
            given.request_timeout = Duration::from_millis(200);
            let server = GraphQLMachineServer::new(given).unwrap();

            let answered = tokio::time::timeout(Duration::from_secs(10), server.get("i-1".into())).await;

            let Ok(Err(error)) = answered else {
                panic!("the request was not given up (head sent: {send_head})");
            };
            assert!(error.0.contains("did not answer within 200 ms"), "{}", error.0);
        }
    }

    #[tokio::test]
    async fn a_headers_provider_that_never_answers_fails_the_request_within_the_request_timeout() {
        let mut given = with_get(options("http://127.0.0.1:9/graphql", "ws://127.0.0.1:9/graphql"));
        given.headers = Some(Arc::new(|| Box::pin(std::future::pending())));
        given.request_timeout = Duration::from_millis(200);
        let server = GraphQLMachineServer::new(given).unwrap();

        let answered = tokio::time::timeout(Duration::from_secs(10), server.get("i-1".into())).await;

        let Ok(Err(error)) = answered else {
            panic!("the request outlived its timeout while the headers were asked for");
        };
        assert!(error.0.contains("did not answer within 200 ms"), "{}", error.0);
    }

    #[test]
    fn a_revision_is_read_only_as_an_integer() {
        let read = |revision: &str| {
            let answer = Value::parse(&format!(
                r#"{{"id":"i-1","revision":{revision},"state":"Locked","context":{{}}}}"#
            ))
            .unwrap();
            answer_of(&answer).map(|a| match a {
                ServerAnswer::Stored { snapshot, .. } => snapshot.revision,
                ServerAnswer::Refused(p) => panic!("{p:?}"),
            })
        };

        assert_eq!(read("7"), Ok(Some(7)));
        assert_eq!(read("9007199254740991"), Ok(Some(9_007_199_254_740_991)));
        for revision in ["4.5", "1e19", "-1e19", "\"7\"", "true"] {
            let Err(error) = read(revision) else {
                panic!("the revision {revision} was read");
            };
            assert!(error.0.contains("revision"), "{}", error.0);
        }
    }

    #[tokio::test]
    async fn an_acknowledged_socket_that_goes_silent_is_opened_again() {
        let (port, mut heard) = recording_host(|_| Reply::Acknowledge).await;
        let mut given = options(
            &format!("http://127.0.0.1:{port}/graphql"),
            &format!("ws://127.0.0.1:{port}/graphql"),
        );
        given.idle_timeout = Duration::from_millis(200);
        given.resubscribe_delays = Some(vec![Duration::from_millis(10)]);
        let server = GraphQLMachineServer::new(given).unwrap();

        let _stream = server.subscribe("i-1".into(), Arc::new(|_| {}));
        let wait = Duration::from_secs(10);
        tokio::time::timeout(wait, heard.recv()).await.unwrap().unwrap();
        let again = tokio::time::timeout(wait, heard.recv()).await;

        assert!(again.is_ok(), "the silent socket was never given up");
    }

    #[tokio::test]
    async fn a_socket_whose_host_answers_the_ping_is_kept() {
        let (port, mut heard) = recording_host(|_| Reply::Pong).await;
        let mut given = options(
            &format!("http://127.0.0.1:{port}/graphql"),
            &format!("ws://127.0.0.1:{port}/graphql"),
        );
        given.idle_timeout = Duration::from_millis(200);
        given.resubscribe_delays = Some(vec![Duration::from_millis(10)]);
        let server = GraphQLMachineServer::new(given).unwrap();

        let _stream = server.subscribe("i-1".into(), Arc::new(|_| {}));
        tokio::time::timeout(Duration::from_secs(10), heard.recv())
            .await
            .unwrap()
            .unwrap();
        let again = tokio::time::timeout(Duration::from_millis(1000), heard.recv()).await;

        assert!(again.is_err(), "a socket that answered every ping was opened again");
    }

    #[test]
    fn a_stream_asked_for_outside_a_runtime_is_refused_not_panicked() {
        let server = GraphQLMachineServer::new(options(
            "https://api.example.com/graphql",
            "wss://api.example.com/graphql",
        ))
        .unwrap();
        let delivered = Arc::new(std::sync::Mutex::new(Vec::new()));
        let into = delivered.clone();

        let stream = server.subscribe("i-1".into(), Arc::new(move |m| into.lock().unwrap().push(m)));
        drop(stream);

        let delivered = delivered.lock().unwrap();
        assert!(
            matches!(&delivered[..], [StreamMessage::Refused(p)] if p.code == codes::TRANSPORT_ERROR),
            "{delivered:?}"
        );
    }

    #[test]
    fn a_stream_runs_on_the_runtime_the_options_name() {
        let runtime = tokio::runtime::Runtime::new().unwrap();
        let mut given = options("https://api.example.com/graphql", "wss://api.example.com/graphql");
        given.runtime = Some(runtime.handle().clone());
        let server = GraphQLMachineServer::new(given).unwrap();
        let delivered = Arc::new(std::sync::Mutex::new(Vec::new()));
        let into = delivered.clone();

        let stream = server.subscribe("i-1".into(), Arc::new(move |m| into.lock().unwrap().push(m)));
        drop(stream);

        assert!(delivered.lock().unwrap().is_empty());
    }

    #[test]
    fn the_idle_timeout_is_sixty_seconds_by_default() {
        let given = options("https://api.example.com/graphql", "wss://api.example.com/graphql");
        assert_eq!(given.idle_timeout, Duration::from_secs(60));
    }

    #[test]
    fn the_request_timeout_is_thirty_seconds_by_default() {
        let given = options("https://api.example.com/graphql", "wss://api.example.com/graphql");
        assert_eq!(given.request_timeout, Duration::from_secs(30));
    }

    #[test]
    fn the_limit_is_sixteen_mebibytes_by_default() {
        let given = options("https://api.example.com/graphql", "wss://api.example.com/graphql");
        assert_eq!(given.max_message_size, 16 * 1024 * 1024);
    }
}
