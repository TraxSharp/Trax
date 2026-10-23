// What a stored snapshot may not hold although the JSON parser reads it. The C# engine is the source of truth for a
// rehydrate's refusal code, and its reader (System.Text.Json) refuses more than `Value::parse` does: a document nested
// deeper than 64 containers, a key written twice in one object, and a string or key holding a lone surrogate, which it
// cannot decode. It only reads the members it uses, so a duplicate or a lone surrogate inside a member it ignores goes
// unnoticed there, and this reports it per member to match. Then, once the state is known, a value no store can hold
// (a NUL character, a number outside the range of a double) is refused as MALFORMED too (C# `StorableJson`). The
// TypeScript engine's `storedText.ts` is the same check.

use std::collections::HashSet;

use crate::value::{Map, Value};

/// The deepest nesting of objects and arrays the C# reader accepts, the root counting as one.
pub(crate) const MAX_STORED_DEPTH: usize = 64;

pub(crate) struct StoredProblems {
    /// Deeper than [`MAX_STORED_DEPTH`], or a top-level key written twice or holding a lone surrogate.
    pub root: bool,
    /// The top-level members whose value holds a key written twice or a string with a lone surrogate.
    pub members: HashSet<String>,
}

fn has_lone_surrogate(units: &[u16]) -> bool {
    char::decode_utf16(units.iter().copied()).any(|c| c.is_err())
}

// The string whose opening quote is at `start`, as UTF-16 code units, and the index just past its closing quote.
fn read_string(bytes: &[u8], text: &str, start: usize) -> (Vec<u16>, usize) {
    let mut units = Vec::new();
    let mut i = start + 1;
    while i < bytes.len() && bytes[i] != b'"' {
        if bytes[i] == b'\\' {
            let escape = bytes.get(i + 1).copied().unwrap_or(b'"');
            if escape == b'u' {
                let hex = text.get(i + 2..i + 6).unwrap_or("0");
                units.push(u16::from_str_radix(hex, 16).unwrap_or(0));
                i += 6;
            } else {
                units.push(match escape {
                    b'b' => 0x08,
                    b'f' => 0x0c,
                    b'n' => 0x0a,
                    b'r' => 0x0d,
                    b't' => 0x09,
                    other => u16::from(other),
                });
                i += 2;
            }
        } else {
            let c = text[i..].chars().next().unwrap_or('\u{fffd}');
            let mut buf = [0u16; 2];
            units.extend_from_slice(c.encode_utf16(&mut buf));
            i += c.len_utf8();
        }
    }
    (units, i + 1)
}

/// Scans text the parser has already accepted for what the C# reader refuses. Iterative, so a deep document cannot
/// overflow the stack.
pub(crate) fn stored_text_problems(text: &str) -> StoredProblems {
    let bytes = text.as_bytes();
    let mut members = HashSet::new();
    // Each open container: the keys seen so far in an object, None for an array.
    let mut open: Vec<Option<HashSet<Vec<u16>>>> = Vec::new();
    let mut member: Option<String> = None;
    let mut root = false;
    let mut i = 0;
    while i < bytes.len() {
        match bytes[i] {
            b'{' | b'[' => {
                open.push((bytes[i] == b'{').then(HashSet::new));
                if open.len() > MAX_STORED_DEPTH {
                    return StoredProblems { root: true, members };
                }
                i += 1;
            }
            b'}' | b']' => {
                open.pop();
                i += 1;
            }
            b'"' => {
                let (value, end) = read_string(bytes, text, i);
                i = end;
                let mut next = i;
                while next < bytes.len() && matches!(bytes[next], b' ' | b'\t' | b'\n' | b'\r') {
                    next += 1;
                }
                let depth = open.len();
                let is_key = bytes.get(next) == Some(&b':') && matches!(open.last(), Some(Some(_)));
                let mut bad = has_lone_surrogate(&value);
                if is_key {
                    if let Some(Some(keys)) = open.last_mut() {
                        bad |= !keys.insert(value.clone());
                    }
                }
                if is_key && depth == 1 {
                    member = Some(String::from_utf16_lossy(&value));
                    root |= bad;
                } else if bad {
                    if let Some(member) = &member {
                        members.insert(member.clone());
                    }
                }
            }
            _ => i += 1,
        }
    }
    StoredProblems { root, members }
}

/// A message naming the first value in the context no store can hold (a NUL character in a string or a key, a number
/// that is not finite), or `None` when every value can be stored.
pub(crate) fn unstorable(context: &Map) -> Option<&'static str> {
    let mut pending: Vec<&Value> = Vec::new();
    if let Some(problem) = keys_of(context, &mut pending) {
        return Some(problem);
    }
    while let Some(value) = pending.pop() {
        match value {
            Value::String(s) if s.to_units().contains(&0) => {
                return Some("A string holds a NUL character, which cannot be stored.");
            }
            Value::Number(n) if !n.is_finite() => return Some("A number is outside the range of a double."),
            Value::Array(items) => pending.extend(items.iter().rev()),
            Value::Object(map) => {
                if let Some(problem) = keys_of(map, &mut pending) {
                    return Some(problem);
                }
            }
            _ => {}
        }
    }
    None
}

// Checks an object's keys and queues its values.
fn keys_of<'a>(map: &'a Map, pending: &mut Vec<&'a Value>) -> Option<&'static str> {
    for (key, value) in map.iter().rev() {
        if key.to_units().contains(&0) {
            return Some("A key holds a NUL character, which cannot be stored.");
        }
        pending.push(value);
    }
    None
}
