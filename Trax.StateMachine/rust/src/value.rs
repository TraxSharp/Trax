// JSON values as the other engines read them: a number is one double (JSON.parse's and System.Text.Json's double),
// a string is UTF-16 that may hold a lone surrogate, and an object's members are ordered by UTF-16 code unit, which is
// the order RFC 8785 writes them in. The parser follows JSON.parse: a duplicate key keeps its last value, and a number
// too large for a double reads as infinity, which the canonical wire writes as null.

use std::cmp::Ordering;
use std::collections::BTreeMap;
use std::fmt;

/// A JSON object: members keyed by [`JsString`], iterated in UTF-16 code-unit order.
pub type Map = BTreeMap<JsString, Value>;

/// A JSON value.
#[derive(Clone, Debug, PartialEq)]
pub enum Value {
    Null,
    Bool(bool),
    /// Every number is a double, as in the TypeScript engine; `1`, `1.0` and `1e0` are one value.
    Number(f64),
    String(JsString),
    Array(Vec<Value>),
    Object(Map),
}

/// A string as JavaScript holds it: UTF-16 code units, which may include a lone surrogate. Most strings are
/// well-formed and are kept as UTF-8; [`JsString::as_str`] returns `None` for one that is not. Ordered by UTF-16
/// code unit, the order RFC 8785 sorts keys in.
#[derive(Clone, PartialEq, Eq, Hash)]
pub struct JsString(Repr);

#[derive(Clone, PartialEq, Eq, Hash)]
enum Repr {
    Text(String),
    // Only for a string with a lone surrogate, so two equal strings always have the same representation.
    Units(Vec<u16>),
}

impl JsString {
    /// The string of these UTF-16 code units.
    pub fn from_units(units: &[u16]) -> JsString {
        match String::from_utf16(units) {
            Ok(text) => JsString(Repr::Text(text)),
            Err(_) => JsString(Repr::Units(units.to_vec())),
        }
    }

    /// The string as UTF-8, or `None` when it holds a lone surrogate.
    pub fn as_str(&self) -> Option<&str> {
        match &self.0 {
            Repr::Text(text) => Some(text),
            Repr::Units(_) => None,
        }
    }

    /// Whether the string has no lone surrogate.
    pub fn is_well_formed(&self) -> bool {
        matches!(self.0, Repr::Text(_))
    }

    /// The string's length in UTF-16 code units, JavaScript's `length`.
    pub fn utf16_len(&self) -> usize {
        match &self.0 {
            Repr::Text(text) => text.encode_utf16().count(),
            Repr::Units(units) => units.len(),
        }
    }

    /// The string's UTF-16 code units.
    pub fn to_units(&self) -> Vec<u16> {
        match &self.0 {
            Repr::Text(text) => text.encode_utf16().collect(),
            Repr::Units(units) => units.clone(),
        }
    }

    /// The string as UTF-8, a lone surrogate replaced with U+FFFD.
    pub fn to_string_lossy(&self) -> String {
        match &self.0 {
            Repr::Text(text) => text.clone(),
            Repr::Units(units) => String::from_utf16_lossy(units),
        }
    }
}

impl From<&str> for JsString {
    fn from(text: &str) -> JsString {
        JsString(Repr::Text(text.to_owned()))
    }
}

impl From<String> for JsString {
    fn from(text: String) -> JsString {
        JsString(Repr::Text(text))
    }
}

impl PartialEq<str> for JsString {
    fn eq(&self, other: &str) -> bool {
        self.as_str() == Some(other)
    }
}

impl Ord for JsString {
    fn cmp(&self, other: &JsString) -> Ordering {
        match (&self.0, &other.0) {
            (Repr::Text(a), Repr::Text(b)) => a.encode_utf16().cmp(b.encode_utf16()),
            _ => self.to_units().cmp(&other.to_units()),
        }
    }
}

impl PartialOrd for JsString {
    fn partial_cmp(&self, other: &JsString) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

impl fmt::Debug for JsString {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&quote(self))
    }
}

/// Why a text is not JSON. The byte offset is where the parser stopped.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ParseError {
    pub offset: usize,
    pub message: &'static str,
}

impl fmt::Display for ParseError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{} at byte {}", self.message, self.offset)
    }
}

impl std::error::Error for ParseError {}

/// How deep arrays and objects may nest. Past it a text is refused rather than read, so no recursion over a value
/// can exhaust the stack.
pub const MAX_DEPTH: usize = 512;

impl Value {
    /// Reads JSON text as `JSON.parse` does. Never panics.
    pub fn parse(text: &str) -> Result<Value, ParseError> {
        let mut parser = Parser {
            bytes: text.as_bytes(),
            pos: 0,
        };
        parser.whitespace();
        let value = parser.value(0)?;
        parser.whitespace();
        if parser.pos != parser.bytes.len() {
            return Err(parser.error("unexpected text after the value"));
        }
        Ok(value)
    }

    /// The value as canonical JSON (RFC 8785): members sorted by UTF-16 code unit, numbers as ECMAScript writes them,
    /// strings escaped as `JSON.stringify` escapes them. A number that is not finite is written as `null`.
    pub fn to_json(&self) -> String {
        let mut out = String::new();
        write_value(self, &mut out);
        out
    }

    /// The member `key` of an object, or `None` when this is not an object or has no such member.
    pub fn get(&self, key: &str) -> Option<&Value> {
        match self {
            Value::Object(map) => map.get(&JsString::from(key)),
            _ => None,
        }
    }

    pub fn as_str(&self) -> Option<&str> {
        match self {
            Value::String(s) => s.as_str(),
            _ => None,
        }
    }

    pub fn as_f64(&self) -> Option<f64> {
        match self {
            Value::Number(n) => Some(*n),
            _ => None,
        }
    }

    pub fn as_bool(&self) -> Option<bool> {
        match self {
            Value::Bool(b) => Some(*b),
            _ => None,
        }
    }

    pub fn as_array(&self) -> Option<&Vec<Value>> {
        match self {
            Value::Array(items) => Some(items),
            _ => None,
        }
    }

    pub fn as_object(&self) -> Option<&Map> {
        match self {
            Value::Object(map) => Some(map),
            _ => None,
        }
    }

    pub fn is_null(&self) -> bool {
        matches!(self, Value::Null)
    }
}

impl From<&str> for Value {
    fn from(text: &str) -> Value {
        Value::String(JsString::from(text))
    }
}

impl From<String> for Value {
    fn from(text: String) -> Value {
        Value::String(JsString::from(text))
    }
}

impl From<f64> for Value {
    fn from(n: f64) -> Value {
        Value::Number(n)
    }
}

impl From<bool> for Value {
    fn from(b: bool) -> Value {
        Value::Bool(b)
    }
}

impl From<Map> for Value {
    fn from(map: Map) -> Value {
        Value::Object(map)
    }
}

impl From<Vec<Value>> for Value {
    fn from(items: Vec<Value>) -> Value {
        Value::Array(items)
    }
}

struct Parser<'a> {
    bytes: &'a [u8],
    pos: usize,
}

impl Parser<'_> {
    fn error(&self, message: &'static str) -> ParseError {
        ParseError {
            offset: self.pos,
            message,
        }
    }

    fn peek(&self) -> Option<u8> {
        self.bytes.get(self.pos).copied()
    }

    // JSON's whitespace only: no BOM, no other Unicode space.
    fn whitespace(&mut self) {
        while matches!(self.peek(), Some(b' ' | b'\t' | b'\n' | b'\r')) {
            self.pos += 1;
        }
    }

    fn literal(&mut self, word: &[u8], value: Value) -> Result<Value, ParseError> {
        if self.bytes[self.pos..].starts_with(word) {
            self.pos += word.len();
            Ok(value)
        } else {
            Err(self.error("unexpected token"))
        }
    }

    fn value(&mut self, depth: usize) -> Result<Value, ParseError> {
        match self.peek() {
            None => Err(self.error("unexpected end of text")),
            Some(b'n') => self.literal(b"null", Value::Null),
            Some(b't') => self.literal(b"true", Value::Bool(true)),
            Some(b'f') => self.literal(b"false", Value::Bool(false)),
            Some(b'"') => Ok(Value::String(self.string()?)),
            Some(b'[') => self.array(depth + 1),
            Some(b'{') => self.object(depth + 1),
            Some(b'-' | b'0'..=b'9') => self.number(),
            Some(_) => Err(self.error("unexpected token")),
        }
    }

    fn array(&mut self, depth: usize) -> Result<Value, ParseError> {
        if depth > MAX_DEPTH {
            return Err(self.error("nested too deeply"));
        }
        self.pos += 1;
        let mut items = Vec::new();
        self.whitespace();
        if self.peek() == Some(b']') {
            self.pos += 1;
            return Ok(Value::Array(items));
        }
        loop {
            self.whitespace();
            items.push(self.value(depth)?);
            self.whitespace();
            match self.peek() {
                Some(b',') => self.pos += 1,
                Some(b']') => {
                    self.pos += 1;
                    return Ok(Value::Array(items));
                }
                _ => return Err(self.error("expected ',' or ']'")),
            }
        }
    }

    fn object(&mut self, depth: usize) -> Result<Value, ParseError> {
        if depth > MAX_DEPTH {
            return Err(self.error("nested too deeply"));
        }
        self.pos += 1;
        let mut map = Map::new();
        self.whitespace();
        if self.peek() == Some(b'}') {
            self.pos += 1;
            return Ok(Value::Object(map));
        }
        loop {
            self.whitespace();
            if self.peek() != Some(b'"') {
                return Err(self.error("expected a member name"));
            }
            let key = self.string()?;
            self.whitespace();
            if self.peek() != Some(b':') {
                return Err(self.error("expected ':'"));
            }
            self.pos += 1;
            self.whitespace();
            let value = self.value(depth)?;
            // As JSON.parse: a repeated name keeps the last value.
            map.insert(key, value);
            self.whitespace();
            match self.peek() {
                Some(b',') => self.pos += 1,
                Some(b'}') => {
                    self.pos += 1;
                    return Ok(Value::Object(map));
                }
                _ => return Err(self.error("expected ',' or '}'")),
            }
        }
    }

    fn digits(&mut self) -> usize {
        let start = self.pos;
        while matches!(self.peek(), Some(b'0'..=b'9')) {
            self.pos += 1;
        }
        self.pos - start
    }

    fn number(&mut self) -> Result<Value, ParseError> {
        let start = self.pos;
        if self.peek() == Some(b'-') {
            self.pos += 1;
        }
        match self.peek() {
            Some(b'0') => self.pos += 1,
            Some(b'1'..=b'9') => {
                self.digits();
            }
            _ => return Err(self.error("expected a digit")),
        }
        if self.peek() == Some(b'.') {
            self.pos += 1;
            if self.digits() == 0 {
                return Err(self.error("expected a digit after '.'"));
            }
        }
        if matches!(self.peek(), Some(b'e' | b'E')) {
            self.pos += 1;
            if matches!(self.peek(), Some(b'+' | b'-')) {
                self.pos += 1;
            }
            if self.digits() == 0 {
                return Err(self.error("expected a digit in the exponent"));
            }
        }
        // The grammar above is a subset of what f64's parser reads, and it rounds correctly, as JSON.parse does; a
        // number past the largest double is infinity.
        let text = std::str::from_utf8(&self.bytes[start..self.pos]).map_err(|_| self.error("bad number"))?;
        text.parse::<f64>()
            .map(Value::Number)
            .map_err(|_| self.error("bad number"))
    }

    fn hex4(&mut self) -> Result<u16, ParseError> {
        let mut unit: u16 = 0;
        for _ in 0..4 {
            let digit = match self.peek() {
                Some(b @ b'0'..=b'9') => b - b'0',
                Some(b @ b'a'..=b'f') => b - b'a' + 10,
                Some(b @ b'A'..=b'F') => b - b'A' + 10,
                _ => return Err(self.error("bad \\u escape")),
            };
            unit = (unit << 4) | u16::from(digit);
            self.pos += 1;
        }
        Ok(unit)
    }

    // A string's code units: an escape may name a lone surrogate, which JSON.parse keeps.
    fn string(&mut self) -> Result<JsString, ParseError> {
        self.pos += 1;
        let mut units: Vec<u16> = Vec::new();
        loop {
            let start = self.pos;
            while let Some(b) = self.peek() {
                if b == b'"' || b == b'\\' || b < 0x20 {
                    break;
                }
                self.pos += 1;
            }
            // The run is whole UTF-8 characters: it stops only at ASCII bytes.
            if let Ok(run) = std::str::from_utf8(&self.bytes[start..self.pos]) {
                units.extend(run.encode_utf16());
            }
            match self.peek() {
                None => return Err(self.error("unterminated string")),
                Some(b'"') => {
                    self.pos += 1;
                    return Ok(JsString::from_units(&units));
                }
                Some(b'\\') => {
                    self.pos += 1;
                    let unit = match self.peek() {
                        Some(b'"') => 0x22,
                        Some(b'\\') => 0x5c,
                        Some(b'/') => 0x2f,
                        Some(b'b') => 0x08,
                        Some(b'f') => 0x0c,
                        Some(b'n') => 0x0a,
                        Some(b'r') => 0x0d,
                        Some(b't') => 0x09,
                        Some(b'u') => {
                            self.pos += 1;
                            units.push(self.hex4()?);
                            continue;
                        }
                        _ => return Err(self.error("bad escape")),
                    };
                    self.pos += 1;
                    units.push(unit);
                }
                Some(_) => return Err(self.error("control character in a string")),
            }
        }
    }
}

fn write_value(value: &Value, out: &mut String) {
    match value {
        Value::Null => out.push_str("null"),
        Value::Bool(true) => out.push_str("true"),
        Value::Bool(false) => out.push_str("false"),
        Value::Number(n) => out.push_str(&number_text(*n)),
        Value::String(s) => write_string(s, out),
        Value::Array(items) => {
            out.push('[');
            for (i, item) in items.iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                write_value(item, out);
            }
            out.push(']');
        }
        Value::Object(map) => write_object(map, out),
    }
}

pub(crate) fn write_object(map: &Map, out: &mut String) {
    out.push('{');
    for (i, (key, value)) in map.iter().enumerate() {
        if i > 0 {
            out.push(',');
        }
        write_string(key, out);
        out.push(':');
        write_value(value, out);
    }
    out.push('}');
}

/// A string as `JSON.stringify` writes it.
pub(crate) fn quote(s: &JsString) -> String {
    let mut out = String::new();
    write_string(s, &mut out);
    out
}

pub(crate) fn write_string(s: &JsString, out: &mut String) {
    out.push('"');
    match &s.0 {
        Repr::Text(text) => {
            for c in text.chars() {
                write_char(c, out);
            }
        }
        Repr::Units(units) => {
            for c in char::decode_utf16(units.iter().copied()) {
                match c {
                    Ok(c) => write_char(c, out),
                    Err(lone) => out.push_str(&format!("\\u{:04x}", lone.unpaired_surrogate())),
                }
            }
        }
    }
    out.push('"');
}

fn write_char(c: char, out: &mut String) {
    match c {
        '"' => out.push_str("\\\""),
        '\\' => out.push_str("\\\\"),
        '\u{08}' => out.push_str("\\b"),
        '\u{0c}' => out.push_str("\\f"),
        '\n' => out.push_str("\\n"),
        '\r' => out.push_str("\\r"),
        '\t' => out.push_str("\\t"),
        c if (c as u32) < 0x20 => out.push_str(&format!("\\u{:04x}", c as u32)),
        c => out.push(c),
    }
}

/// A number as ECMAScript's Number::toString writes it (RFC 8785 §3.2.2.3): the shortest digits that read back as the
/// same double, in plain notation from 1e-6 up to 1e21 and in exponent notation (`1e+21`, `1e-7`) outside it. `-0` is
/// `0`, and a number that is not finite is `null`, as `JSON.stringify` writes it.
pub fn number_text(n: f64) -> String {
    if !n.is_finite() {
        return "null".to_owned();
    }
    if n == 0.0 {
        return "0".to_owned();
    }
    let (digits, exponent) = shortest_digits(n.abs());
    let k = digits.len() as i32;
    let point = exponent + 1;

    let mut out = String::new();
    if n < 0.0 {
        out.push('-');
    }
    if k <= point && point <= 21 {
        out.push_str(&digits);
        out.extend(std::iter::repeat_n('0', (point - k) as usize));
    } else if 0 < point && point <= 21 {
        let (whole, fraction) = digits.split_at(point as usize);
        out.push_str(whole);
        out.push('.');
        out.push_str(fraction);
    } else if -6 < point && point <= 0 {
        out.push_str("0.");
        out.extend(std::iter::repeat_n('0', (-point) as usize));
        out.push_str(&digits);
    } else {
        let (first, rest) = digits.split_at(1);
        out.push_str(first);
        if !rest.is_empty() {
            out.push('.');
            out.push_str(rest);
        }
        out.push('e');
        out.push(if point - 1 < 0 { '-' } else { '+' });
        out.push_str(&(point - 1).abs().to_string());
    }
    out
}

// The shortest digits that read back as `x` (positive and finite), and the exponent of the first, as ECMAScript's
// Number::toString picks them: when two digit strings of that length are equally close to `x`, the even one. Rust's
// `{:e}` writes the shortest digits too but rounds such a tie up (1125899906842624.25 comes out as ...624.3, where
// ECMAScript writes ...624.2), so a tie is found from the exact expansion and settled here.
fn shortest_digits(x: f64) -> (String, i32) {
    let split = |text: &str| -> (String, i32) {
        let (mantissa, exponent) = text.split_once('e').unwrap_or((text, "0"));
        let digits = mantissa.chars().filter(|c| *c != '.').collect();
        (digits, exponent.parse().unwrap_or(0))
    };
    let (digits, exponent) = split(&format!("{x:e}"));
    let last = digits.as_bytes()[digits.len() - 1] - b'0';
    if last % 2 == 0 {
        return (digits, exponent);
    }
    // A tie means `x` is exactly the shortest length's digits plus a half in the last place: its exact expansion,
    // which a double always has in at most 767 significant digits, is those digits and a 5, then nothing.
    let (exact, exact_exponent) = split(&format!("{x:.800e}"));
    let k = digits.len();
    let tail = exact[k..].trim_end_matches('0');
    if exact_exponent != exponent || tail != "5" {
        return (digits, exponent);
    }
    let truncated = &exact[..k];
    let other = if truncated == digits {
        // Rounded down to an odd digit; one up is even. Past a 9 it would carry into a shorter string, which would
        // have been found first.
        if last == 9 {
            return (digits, exponent);
        }
        let mut bytes = digits.clone().into_bytes();
        bytes[k - 1] += 1;
        String::from_utf8(bytes).unwrap_or_default()
    } else {
        // Rounded up to an odd digit; one down is even and is the truncation.
        truncated.to_owned()
    };
    let reads_back = format!("{other}e{}", exponent - (k as i32 - 1))
        .parse::<f64>()
        .is_ok_and(|y| y == x);
    if reads_back && !other.ends_with('0') {
        (other, exponent)
    } else {
        (digits, exponent)
    }
}

/// The value as `JSON.parse(JSON.stringify(value))` returns it: a number that is not finite becomes null. The engine
/// copies a context this way wherever the TypeScript engine does, so both carry the same value forward.
pub(crate) fn json_copy(value: &Value) -> Value {
    match value {
        Value::Number(n) if !n.is_finite() => Value::Null,
        Value::Number(n) if *n == 0.0 => Value::Number(0.0),
        Value::Array(items) => Value::Array(items.iter().map(json_copy).collect()),
        Value::Object(map) => Value::Object(json_copy_map(map)),
        other => other.clone(),
    }
}

pub(crate) fn json_copy_map(map: &Map) -> Map {
    map.iter().map(|(k, v)| (k.clone(), json_copy(v))).collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn numbers_are_written_as_ecmascript_writes_them() {
        for (n, text) in [
            (0.1, "0.1"),
            (1e21, "1e+21"),
            (1e20, "100000000000000000000"),
            (1e-7, "1e-7"),
            (1e-6, "0.000001"),
            (5e-324, "5e-324"),
            (-1.5, "-1.5"),
            (123.456, "123.456"),
            (-0.0, "0"),
            (2f64.powi(53) + 2.0, "9007199254740994"),
            (2f64.powi(50) + 0.25, "1125899906842624.2"),
            (2f64.powi(50) + 0.75, "1125899906842624.8"),
            (2f64.powi(51) - 0.75, "2251799813685247.2"),
            (1.5e300, "1.5e+300"),
            (f64::INFINITY, "null"),
        ] {
            assert_eq!(number_text(n), text, "{n}");
        }
    }

    #[test]
    fn a_lone_surrogate_survives_a_round_trip() {
        let value = Value::parse(r#"{"\ud800":"a\udfffb"}"#).unwrap();
        assert_eq!(value.to_json(), r#"{"\ud800":"a\udfffb"}"#);
    }

    #[test]
    fn keys_sort_by_utf16_code_unit() {
        // U+FF61 sorts after U+1F600 by code point but before it by UTF-16 code unit.
        let value = Value::parse("{\"\u{FF61}\":1,\"\u{1F600}\":2}").unwrap();
        assert_eq!(value.to_json(), "{\"\u{1F600}\":2,\"\u{FF61}\":1}");
    }

    #[test]
    fn what_json_parse_refuses_is_refused() {
        for text in [
            "",
            "01",
            "1.",
            ".5",
            "+1",
            "[1,]",
            "{\"a\":1,}",
            "'a'",
            "\u{feff}{}",
            "NaN",
            "\"a\tb\"",
            "{} x",
            "[",
            "\"\\x\"",
        ] {
            assert!(Value::parse(text).is_err(), "{text:?}");
        }
    }

    #[test]
    fn nesting_past_the_limit_is_refused_not_overflowed() {
        let deep = "[".repeat(100_000);
        assert!(Value::parse(&deep).is_err());
    }
}
