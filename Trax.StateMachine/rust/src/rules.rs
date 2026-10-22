// The interpreter of the IR's declarative rules, reductions and schemas, the Rust twin of ts/src/rules/interpreter.ts
// and of the C# RuleEvaluator, ReductionEvaluator and SchemaValidator. Total: a missing or wrong-typed field is a
// false rule or a refused schema, never a panic.

use std::cmp::Ordering;

use crate::formats::{is_one_of, matches_format};
use crate::ir::{CompareOp, FieldSchema, FieldType, Reduction, Rule, Schema, Source, Test, ValueSchema, ValueSource};
use crate::machine::CustomRules;
use crate::value::{JsString, Map, Value};

/// How a custom rule with no bound handler counts: false wherever the engine predicts (it cannot tell that the rule
/// holds), true when it reads a context the server already checked.
#[derive(Clone, Copy, PartialEq, Eq)]
pub(crate) enum Unhandled {
    Fails,
    Passes,
}

pub(crate) fn evaluate(
    rule: &Rule,
    context: &Map,
    input: Option<&Value>,
    custom: &CustomRules,
    unhandled: Unhandled,
) -> bool {
    match rule {
        Rule::All(rules) => rules.iter().all(|r| evaluate(r, context, input, custom, unhandled)),
        Rule::Any(rules) => rules.iter().any(|r| evaluate(r, context, input, custom, unhandled)),
        Rule::Custom(name) => match custom.guards.get(name) {
            Some(guard) => guard(context, input),
            None => unhandled == Unhandled::Passes,
        },
        Rule::Leaf { source, field, test } => {
            let value = read(*source, field, context, input);
            leaf(test, value)
        }
    }
}

fn leaf(test: &Test, value: Option<&Value>) -> bool {
    match test {
        Test::Present => !is_nullish(value),
        Test::Absent => is_nullish(value),
        Test::OfType(ty) => value.is_some_and(|v| matches_type(v, *ty)),
        Test::NonEmpty => match value {
            Some(Value::Array(items)) => !items.is_empty(),
            Some(Value::String(s)) => s.utf16_len() > 0,
            _ => false,
        },
        Test::OneOf(values) => match value {
            Some(Value::String(s)) => values.contains(s),
            _ => false,
        },
        Test::Compare(op, target) => match value {
            Some(Value::Number(n)) => compare(*n, *op, *target),
            _ => false,
        },
        Test::Count(op, target) => match value {
            Some(Value::Array(items)) => compare(items.len() as f64, *op, *target),
            _ => false,
        },
        Test::Length(op, target) => match value {
            Some(Value::String(s)) => compare(s.utf16_len() as f64, *op, *target),
            _ => false,
        },
        Test::BoolEquals(expected) => matches!(value, Some(Value::Bool(b)) if b == expected),
        Test::ArrayOf(ty) => match value {
            Some(Value::Array(items)) => items.iter().all(|v| matches_type(v, *ty)),
            _ => false,
        },
    }
}

// A field of the context, or of the input when the input is an object; own members only.
fn read<'a>(source: Source, field: &JsString, context: &'a Map, input: Option<&'a Value>) -> Option<&'a Value> {
    match source {
        Source::Context => context.get(field),
        Source::Input => match input {
            Some(Value::Object(map)) => map.get(field),
            _ => None,
        },
    }
}

fn is_nullish(value: Option<&Value>) -> bool {
    matches!(value, None | Some(Value::Null))
}

fn compare(actual: f64, op: CompareOp, target: f64) -> bool {
    match op {
        CompareOp::Gt => actual > target,
        CompareOp::Gte => actual >= target,
        CompareOp::Lt => actual < target,
        CompareOp::Lte => actual <= target,
        CompareOp::Eq => actual == target,
    }
}

// A value of the JSON type, as both other engines decide it: a number is finite, an integer is a number with no
// fractional part, and a string is well-formed UTF-16.
fn matches_type(value: &Value, ty: FieldType) -> bool {
    match (ty, value) {
        (FieldType::String, Value::String(s)) => s.is_well_formed(),
        (FieldType::Number, Value::Number(n)) => n.is_finite(),
        (FieldType::Integer, Value::Number(n)) => n.is_finite() && n.fract() == 0.0,
        (FieldType::Boolean, Value::Bool(_)) => true,
        (FieldType::Array, Value::Array(_)) => true,
        (FieldType::Object, Value::Object(_)) => true,
        _ => false,
    }
}

/// The destination context of a reduction. `initial` is the machine's initial context.
pub(crate) fn reduce(
    reduction: &Reduction,
    context: &Map,
    input: Option<&Value>,
    initial: &Map,
    custom: &CustomRules,
) -> Map {
    match reduction {
        Reduction::Keep => context.clone(),
        Reduction::Clear => Map::new(),
        Reduction::Reset => initial.clone(),
        Reduction::Set(steps) => {
            let mut next = context.clone();
            for (field, source) in steps {
                let value = match source {
                    ValueSource::Input(name) => match input {
                        Some(Value::Object(map)) => map.get(name).cloned().unwrap_or(Value::Null),
                        _ => Value::Null,
                    },
                    ValueSource::Const(value) => value.clone(),
                };
                next.insert(field.clone(), value);
            }
            next
        }
        Reduction::Custom(name) => match custom.reducers.get(name) {
            Some(reducer) => reducer(context, input),
            None => context.clone(),
        },
    }
}

/// Why a context does not match its state's schema, or `None`.
pub(crate) fn validate_schema(schema: &Schema, context: &Map) -> Option<String> {
    validate_object(&schema.fields, context, "")
}

/// Why an input (or an invoked train's output) does not match the schema it declares, or `None`. No input reads as
/// an empty object; anything but an object is refused.
pub(crate) fn validate_input(schema: &Schema, input: Option<&Value>) -> Option<String> {
    match input {
        None | Some(Value::Null) => validate_object(&schema.fields, &Map::new(), ""),
        Some(Value::Object(map)) => validate_object(&schema.fields, map, ""),
        Some(_) => Some("The input must be a JSON object.".to_owned()),
    }
}

fn validate_object(fields: &[FieldSchema], obj: &Map, path: &str) -> Option<String> {
    let no_custom = CustomRules::default();
    for field in fields {
        let name = format!("{path}{}", field.name.to_string_lossy());
        let value = match obj.get(&field.name) {
            None | Some(Value::Null) => {
                if !field.value.nullable {
                    return Some(format!("'{name}' is required."));
                }
                continue;
            }
            Some(value) => value,
        };
        if let Some(problem) = check_value(&field.value, value, &name) {
            return Some(problem);
        }
        // A constraint names a field of the object being validated, whichever side it says that object lives on, so
        // both read this object. It is evaluated with no custom handlers, as the TypeScript engine evaluates it.
        let as_input = (!field.constraints.is_empty()).then(|| Value::Object(obj.clone()));
        for constraint in &field.constraints {
            if !evaluate(constraint, obj, as_input.as_ref(), &no_custom, Unhandled::Fails) {
                return Some(format!("'{name}' failed a constraint."));
            }
        }
        if let Some(problem) = check_nested(&field.value, value, &name) {
            return Some(problem);
        }
    }
    for key in obj.keys() {
        if !fields.iter().any(|f| &f.name == key) {
            return Some(format!("unexpected field '{path}{}'.", key.to_string_lossy()));
        }
    }
    None
}

// The checks a field and a value share before the field's constraints: type, range and shape.
fn check_value(schema: &ValueSchema, value: &Value, name: &str) -> Option<String> {
    if !matches_type(value, schema.ty) {
        return Some(format!("'{name}' must be a {}.", type_name(schema.ty)));
    }
    if !in_range(value, schema) {
        return Some(format!("'{name}' is out of range."));
    }
    if let Value::String(s) = value {
        if let Some(problem) = shapeless(s, schema) {
            return Some(format!("'{name}' {problem}."));
        }
    }
    None
}

// The checks after the constraints: nested fields, values and keys.
fn check_nested(schema: &ValueSchema, value: &Value, name: &str) -> Option<String> {
    if let Some(shape) = &schema.fields {
        if let Some(problem) = validate_shape(shape, value, name) {
            return Some(problem);
        }
    }
    if let Some(values) = &schema.values {
        if let Some(problem) = validate_values(values, value, name) {
            return Some(problem);
        }
    }
    if let Some(keys) = &schema.keys {
        return validate_keys(keys, value, name);
    }
    None
}

fn type_name(ty: FieldType) -> &'static str {
    match ty {
        FieldType::String => "string",
        FieldType::Number => "number",
        FieldType::Integer => "integer",
        FieldType::Boolean => "boolean",
        FieldType::Array => "array",
        FieldType::Object => "object",
    }
}

fn validate_values(schema: &ValueSchema, value: &Value, name: &str) -> Option<String> {
    match value {
        Value::Array(items) => items
            .iter()
            .enumerate()
            .find_map(|(i, item)| validate_value(schema, item, &format!("{name}[{i}]"))),
        Value::Object(map) => map
            .iter()
            .find_map(|(k, v)| validate_value(schema, v, &format!("{name}.{}", k.to_string_lossy()))),
        _ => None,
    }
}

fn validate_value(schema: &ValueSchema, value: &Value, name: &str) -> Option<String> {
    if value.is_null() {
        return (!schema.nullable).then(|| format!("'{name}' is required."));
    }
    check_value(schema, value, name).or_else(|| check_nested(schema, value, name))
}

fn validate_keys(keys: &ValueSchema, value: &Value, name: &str) -> Option<String> {
    let Value::Object(map) = value else {
        return None;
    };
    map.keys().find_map(|key| {
        let problem = if keys.ty == FieldType::Integer {
            (!is_integer_key(key, keys)).then(|| "must be an integer in range".to_owned())
        } else {
            shapeless(key, keys)
        };
        problem.map(|p| format!("'{name}' has a key '{}' that {p}.", key.to_string_lossy()))
    })
}

// An integer key is its digits as System.Text.Json writes them (no sign but a leading minus, no leading zero), within
// the key type's range compared exactly: a key is text, never a double.
fn is_integer_key(key: &JsString, keys: &ValueSchema) -> bool {
    let Some(text) = key.as_str() else {
        return false;
    };
    let digits = text.strip_prefix('-').unwrap_or(text);
    let well_formed = text.len() <= 40
        && !digits.is_empty()
        && digits.bytes().all(|b| b.is_ascii_digit())
        && (text == "0" || !digits.starts_with('0'));
    if !well_formed {
        return false;
    }
    let negative = text.starts_with('-');
    let at_least = keys
        .minimum
        .is_none_or(|min| compare_integer(negative, digits, min).is_some_and(|o| o != Ordering::Less));
    let at_most = keys
        .maximum
        .is_none_or(|max| compare_integer(negative, digits, max).is_some_and(|o| o != Ordering::Greater));
    at_least && at_most
}

// How the integer `-digits` (when `negative`) or `digits` compares with `bound`, exactly. `None` when the bound is not
// an integer, which the TypeScript engine cannot compare either (BigInt refuses it); the key is then refused.
fn compare_integer(negative: bool, digits: &str, bound: f64) -> Option<Ordering> {
    if !bound.is_finite() || bound.fract() != 0.0 {
        return None;
    }
    let bound_negative = bound < 0.0;
    let bound_digits = integer_digits(bound.abs());
    let magnitude = digits
        .len()
        .cmp(&bound_digits.len())
        .then_with(|| digits.cmp(bound_digits.as_str()));
    let zero = digits == "0" && bound_digits == "0";
    Some(match (negative, bound_negative) {
        _ if zero => Ordering::Equal,
        (false, false) => magnitude,
        (true, true) => magnitude.reverse(),
        (false, true) => Ordering::Greater,
        (true, false) => Ordering::Less,
    })
}

// The exact decimal digits of a non-negative integral double.
fn integer_digits(n: f64) -> String {
    let bits = n.to_bits();
    let exponent = ((bits >> 52) & 0x7ff) as i32;
    let fraction = bits & ((1u64 << 52) - 1);
    if exponent == 0 {
        // Zero, or a subnormal, which is not an integer and never reaches here.
        return "0".to_owned();
    }
    let mantissa = fraction | (1u64 << 52);
    let shift = exponent - 1075;
    if shift <= 0 {
        return (mantissa >> (-shift).min(63)).to_string();
    }
    // mantissa * 2^shift, in base-10 digits, least significant first.
    let mut digits: Vec<u8> = mantissa.to_string().bytes().rev().map(|b| b - b'0').collect();
    for _ in 0..shift {
        let mut carry = 0;
        for d in digits.iter_mut() {
            let doubled = *d * 2 + carry;
            *d = doubled % 10;
            carry = doubled / 10;
        }
        if carry > 0 {
            digits.push(carry);
        }
    }
    digits.iter().rev().map(|d| char::from(b'0' + d)).collect()
}

// Why a string is not a value of the schema's C# type: not one of its enum's names, or not of its format.
fn shapeless(value: &JsString, schema: &ValueSchema) -> Option<String> {
    if let Some(names) = &schema.names {
        if !is_one_of(value, names, schema.exact.as_deref()) {
            let list: Vec<String> = names.iter().map(JsString::to_string_lossy).collect();
            return Some(format!("must be one of {}", list.join(", ")));
        }
    }
    if let Some(format) = &schema.format {
        if !matches_format(format, value) {
            return Some(format!("must be a {format}"));
        }
    }
    None
}

// A number within the schema's range, compared as a double. A schema with no range accepts any value of its type;
// one with a range refuses anything but a finite number.
fn in_range(value: &Value, schema: &ValueSchema) -> bool {
    if schema.minimum.is_none() && schema.maximum.is_none() {
        return true;
    }
    let Value::Number(n) = value else {
        return false;
    };
    n.is_finite() && schema.minimum.is_none_or(|min| *n >= min) && schema.maximum.is_none_or(|max| *n <= max)
}

// A nested record's fields, or every element of a list of records.
fn validate_shape(shape: &[FieldSchema], value: &Value, name: &str) -> Option<String> {
    match value {
        Value::Array(items) => items.iter().enumerate().find_map(|(i, item)| match item {
            Value::Object(map) => validate_object(shape, map, &format!("{name}[{i}].")),
            _ => Some(format!("'{name}[{i}]' must be an object.")),
        }),
        Value::Object(map) => validate_object(shape, map, &format!("{name}.")),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn integer_digits_are_exact() {
        assert_eq!(integer_digits(0.0), "0");
        assert_eq!(integer_digits(2147483647.0), "2147483647");
        assert_eq!(integer_digits(2f64.powi(64)), "18446744073709551616");
        assert_eq!(integer_digits(1e21), "1000000000000000000000");
        assert_eq!(
            integer_digits(1.7014118346046923e38),
            "170141183460469231731687303715884105728"
        );
    }

    #[test]
    fn integer_keys_compare_exactly() {
        assert_eq!(
            compare_integer(false, "2147483648", 2147483647.0),
            Some(Ordering::Greater)
        );
        assert_eq!(
            compare_integer(true, "2147483648", -2147483648.0),
            Some(Ordering::Equal)
        );
        assert_eq!(compare_integer(false, "0", -0.0), Some(Ordering::Equal));
        assert_eq!(compare_integer(false, "1", 0.5), None);
    }
}
