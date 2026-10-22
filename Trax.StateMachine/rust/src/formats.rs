// The string formats a field's CLR type gives it, and the ISO-8601 durations a timer reads, checked as the C# engine's
// ValueFormats and IsoDuration check them and as ts/src/rules/formats.ts and ts/src/duration.ts do. Every pattern
// names ASCII digits and letters only, so the checks read bytes; a non-ASCII byte fails every class. Never panics.

use crate::value::JsString;

/// Whether `value` has `format`; an unknown format accepts anything.
pub(crate) fn matches_format(format: &str, value: &JsString) -> bool {
    if format == "char" {
        return value.utf16_len() == 1;
    }
    let Some(text) = value.as_str() else {
        // A lone surrogate is not ASCII, so no other known format accepts it.
        return !KNOWN.contains(&format);
    };
    match format {
        "uuid" => is_uuid(text.as_bytes()),
        "date-time" => is_date_time(text.as_bytes(), true),
        "date-time-offset" => is_date_time(text.as_bytes(), false),
        "date" => is_date(text.as_bytes()),
        "time" => is_time(text.as_bytes()),
        "duration" => duration_ticks(text).is_some(),
        "uri" => is_uri(text, value.utf16_len()),
        _ => true,
    }
}

const KNOWN: [&str; 8] = [
    "uuid",
    "date-time",
    "date-time-offset",
    "date",
    "time",
    "duration",
    "uri",
    "char",
];

/// Whether `value` is one of an enum's `names`: exactly for a name in `exact` (a member renamed with
/// `[JsonStringEnumMemberName]`), else ignoring ASCII case only.
pub(crate) fn is_one_of(value: &JsString, names: &[JsString], exact: Option<&[JsString]>) -> bool {
    names.iter().any(|name| {
        if exact.is_some_and(|exact| exact.contains(name)) {
            value == name
        } else {
            match (value.as_str(), name.as_str()) {
                (Some(a), Some(b)) => a.eq_ignore_ascii_case(b),
                _ => false,
            }
        }
    })
}

fn is_digit(b: u8) -> bool {
    b.is_ascii_digit()
}

fn is_hex(b: u8) -> bool {
    b.is_ascii_hexdigit()
}

// The number the ASCII digits spell; the callers pass at most nine, so it cannot overflow.
fn number(digits: &[u8]) -> u64 {
    digits.iter().fold(0u64, |n, d| n * 10 + u64::from(d - b'0'))
}

fn is_uuid(b: &[u8]) -> bool {
    b.len() == 36
        && b.iter().enumerate().all(|(i, &c)| match i {
            8 | 13 | 18 | 23 => c == b'-',
            _ => is_hex(c),
        })
}

// ---- ISO-8601 durations ----

// The longest duration .NET's TimeSpan holds, in ticks of 100 ns.
const MAX_TICKS: u128 = i64::MAX as u128;

/// The duration `text` holds, in ticks of 100 ns, or `None` when it is not one the subset accepts: days, hours,
/// minutes and seconds (`P1DT2H30M`, `PT0.5S`), never negative, each component at most nine digits and a fraction of
/// at most seven, no longer than `TimeSpan.MaxValue`.
pub fn duration_ticks(text: &str) -> Option<u128> {
    let b = text.as_bytes();
    if b.first() != Some(&b'P') || b.len() == 1 {
        return None;
    }
    let mut pos = 1;
    let mut seconds: u128 = 0;
    let mut fraction: u128 = 0;

    // A run of one to nine digits at `pos` followed by `unit`.
    let component = |pos: usize, unit: u8| -> Option<(u128, usize)> {
        let run = b[pos..].iter().take_while(|c| is_digit(**c)).count();
        if (1..=9).contains(&run) && b.get(pos + run) == Some(&unit) {
            Some((u128::from(number(&b[pos..pos + run])), pos + run + 1))
        } else {
            None
        }
    };

    if let Some((days, next)) = component(pos, b'D') {
        seconds += days * 86_400;
        pos = next;
    }
    if b.get(pos) == Some(&b'T') {
        pos += 1;
        if !b.get(pos).is_some_and(|c| is_digit(*c)) {
            return None;
        }
        if let Some((hours, next)) = component(pos, b'H') {
            seconds += hours * 3_600;
            pos = next;
        }
        if let Some((minutes, next)) = component(pos, b'M') {
            seconds += minutes * 60;
            pos = next;
        }
        let run = b[pos..].iter().take_while(|c| is_digit(**c)).count();
        if (1..=9).contains(&run) {
            let whole = u128::from(number(&b[pos..pos + run]));
            let mut end = pos + run;
            let mut digits: &[u8] = &[];
            if b.get(end) == Some(&b'.') {
                let fraction_run = b[end + 1..].iter().take_while(|c| is_digit(**c)).count();
                if (1..=7).contains(&fraction_run) {
                    digits = &b[end + 1..end + 1 + fraction_run];
                    end += 1 + fraction_run;
                } else {
                    end = usize::MAX;
                }
            }
            if end != usize::MAX && b.get(end) == Some(&b'S') {
                seconds += whole;
                let mut padded = [b'0'; 7];
                padded[..digits.len()].copy_from_slice(digits);
                fraction = u128::from(number(&padded));
                pos = end + 1;
            }
        }
    }
    if pos != b.len() {
        return None;
    }
    let ticks = seconds * 10_000_000 + fraction;
    (ticks <= MAX_TICKS).then_some(ticks)
}

// ---- dates and times ----

fn is_leap(year: u64) -> bool {
    year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)
}

// A year, month and day that name a real day of the proleptic Gregorian calendar from year 1.
fn is_day(year: u64, month: u64, day: u64) -> bool {
    if year < 1 || !(1..=12).contains(&month) || day < 1 {
        return false;
    }
    let days = [
        31,
        if is_leap(year) { 29 } else { 28 },
        31,
        30,
        31,
        30,
        31,
        31,
        30,
        31,
        30,
        31,
    ];
    day <= days[(month - 1) as usize]
}

// The day a valid date is, counted from 0001-01-01.
fn day_of(year: u64, month: u64, day: u64) -> i64 {
    let y = (year - 1) as i64;
    let before = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];
    let leap_day = if is_leap(year) && month > 2 { 1 } else { 0 };
    365 * y + y / 4 - y / 100 + y / 400 + before[(month - 1) as usize] + leap_day + day as i64 - 1
}

// The minute 9999-12-31T23:59 is from 0001-01-01T00:00, the last a DateTime holds.
fn last_minute() -> i64 {
    day_of(9999, 12, 31) * 24 * 60 + 23 * 60 + 59
}

// Exactly `n` digits at `pos`, as a number.
fn fixed(b: &[u8], pos: usize, n: usize) -> Option<u64> {
    let digits = b.get(pos..pos + n)?;
    digits.iter().all(|c| is_digit(*c)).then(|| number(digits))
}

// `YYYY-MM-DD` at `pos`: the year, month and day.
fn ymd(b: &[u8], pos: usize) -> Option<(u64, u64, u64)> {
    let year = fixed(b, pos, 4)?;
    (b.get(pos + 4) == Some(&b'-')).then_some(())?;
    let month = fixed(b, pos + 5, 2)?;
    (b.get(pos + 7) == Some(&b'-')).then_some(())?;
    let day = fixed(b, pos + 8, 2)?;
    Some((year, month, day))
}

// `hh:mm:ss` with an optional fraction of one to seven digits at `pos`: the hour, minute and second, and where it ends.
fn hms(b: &[u8], pos: usize) -> Option<(u64, u64, u64, usize)> {
    let hour = fixed(b, pos, 2)?;
    (b.get(pos + 2) == Some(&b':')).then_some(())?;
    let minute = fixed(b, pos + 3, 2)?;
    (b.get(pos + 5) == Some(&b':')).then_some(())?;
    let second = fixed(b, pos + 6, 2)?;
    let mut end = pos + 8;
    if b.get(end) == Some(&b'.') {
        let run = b[end + 1..].iter().take_while(|c| is_digit(**c)).count();
        if !(1..=7).contains(&run) {
            return None;
        }
        end += 1 + run;
    }
    Some((hour, minute, second, end))
}

fn is_clock(hour: u64, minute: u64, second: u64) -> bool {
    hour < 24 && minute < 60 && second < 60
}

fn is_date(b: &[u8]) -> bool {
    b.len() == 10 && ymd(b, 0).is_some_and(|(y, m, d)| is_day(y, m, d))
}

fn is_time(b: &[u8]) -> bool {
    hms(b, 0).is_some_and(|(h, m, s, end)| end == b.len() && is_clock(h, m, s))
}

// A date-time whose UTC instant is inside years 1 to 9999. One with an offset is read as that offset says; one with
// none is read in the server's zone by a DateTimeOffset, so it is refused on the range's first and last day. A
// DateTime (`zoneless`) holds one with no offset as it is.
fn is_date_time(b: &[u8], zoneless: bool) -> bool {
    let Some((year, month, day)) = ymd(b, 0) else {
        return false;
    };
    if b.get(10) != Some(&b'T') {
        return false;
    }
    let Some((hour, minute, second, end)) = hms(b, 11) else {
        return false;
    };
    if !is_day(year, month, day) || !is_clock(hour, minute, second) {
        return false;
    }
    let local = day_of(year, month, day) * 24 * 60 + (hour * 60 + minute) as i64;
    let last = last_minute();
    match &b[end..] {
        [] => zoneless || (local >= 24 * 60 && local <= last - 24 * 60),
        [b'Z'] => true,
        [sign @ (b'+' | b'-'), rest @ ..] if rest.len() == 5 && rest[2] == b':' => {
            let (Some(hours), Some(minutes)) = (fixed(rest, 0, 2), fixed(rest, 3, 2)) else {
                return false;
            };
            if minutes >= 60 || hours * 60 + minutes > 14 * 60 {
                return false;
            }
            let offset = (hours * 60 + minutes) as i64 * if *sign == b'-' { -1 } else { 1 };
            local - offset >= 0 && local - offset <= last
        }
        _ => false,
    }
}

// ---- URIs ----

// The longest string System.Uri reads.
const MAX_URI_LENGTH: usize = 65519;

// The schemes System.Uri parses with rules of its own.
const KNOWN_SCHEMES: [&str; 16] = [
    "http", "https", "ws", "wss", "ftp", "file", "gopher", "nntp", "news", "mailto", "uuid", "telnet", "ldap",
    "net.tcp", "net.pipe", "vsmacros",
];

// RFC 3986 unreserved and reserved characters, and well-formed %HH escapes.
fn is_uri_text(s: &str) -> bool {
    let b = s.as_bytes();
    let mut i = 0;
    while i < b.len() {
        let c = b[i];
        if c == b'%' {
            if !(b.get(i + 1).is_some_and(|c| is_hex(*c)) && b.get(i + 2).is_some_and(|c| is_hex(*c))) {
                return false;
            }
            i += 3;
        } else if c.is_ascii_alphanumeric() || b"-._~!$&'()*+,;=:@/?".contains(&c) {
            i += 1;
        } else {
            return false;
        }
    }
    true
}

// An absolute URI System.Uri reads the same on every platform, as ValueFormats checks it: a scheme of two or more
// characters, then an authority and a path, or a path alone; a query and a fragment after either. See formats.ts
// `isUri` for each rule's reason.
fn is_uri(value: &str, utf16_len: usize) -> bool {
    if utf16_len > MAX_URI_LENGTH {
        return false;
    }
    let b = value.as_bytes();
    if !b.first().is_some_and(|c| c.is_ascii_alphabetic()) {
        return false;
    }
    let scheme_len = 1 + b[1..]
        .iter()
        .take_while(|c| c.is_ascii_alphanumeric() || matches!(c, b'+' | b'.' | b'-'))
        .count();
    if scheme_len < 2 || b.get(scheme_len) != Some(&b':') {
        return false;
    }
    let name = value[..scheme_len].to_ascii_lowercase();
    let mut rest = &value[scheme_len + 1..];

    if name == "mailto" {
        let (address, query) = match rest.find('?') {
            Some(q) => (&rest[..q], Some(&rest[q + 1..])),
            None => (rest, None),
        };
        let Some(at) = address.find('@') else {
            return false;
        };
        return at > 0 && is_mailbox(&address[..at]) && is_host(&address[at + 1..]) && query.is_none_or(is_uri_text);
    }
    if let Some(after) = rest.strip_prefix("//") {
        let end = after.find(['/', '?', '#']).unwrap_or(after.len());
        if !is_authority(&after[..end], &name) {
            return false;
        }
        let path = &after[end..];
        // A file URI's path starting "//" would be read as a UNC share, and a ':' in it as a drive.
        if name == "file" && (path.starts_with("//") || path.contains(':')) {
            return false;
        }
        rest = path;
    } else if KNOWN_SCHEMES.contains(&name.as_str()) {
        return false;
    }
    match rest.find('#') {
        None => is_uri_text(rest),
        Some(hash) => is_uri_text(&rest[..hash]) && is_uri_text(&rest[hash + 1..]),
    }
}

fn is_mailbox(s: &str) -> bool {
    !s.is_empty()
        && s.bytes()
            .all(|c| c.is_ascii_alphanumeric() || matches!(c, b'.' | b'_' | b'+' | b'-'))
}

fn is_authority(authority: &str, name: &str) -> bool {
    if name == "file" {
        return authority.is_empty() || is_host(authority);
    }
    if name == "net.pipe" {
        return is_host(authority);
    }
    let colon = if authority.starts_with('[') {
        let Some(close) = authority.find(']') else {
            return false;
        };
        authority[close + 1..].find(':').map(|c| c + close + 1)
    } else {
        authority.find(':')
    };
    let (host, port) = match colon {
        Some(c) => (&authority[..c], Some(&authority[c + 1..])),
        None => (authority, None),
    };
    is_host(host)
        && port.is_none_or(|p| (1..=5).contains(&p.len()) && p.bytes().all(is_digit) && number(p.as_bytes()) <= 65535)
}

// A bracketed IPv6 address, a dotted IPv4 address, or letter-digit-hyphen labels whose last is not all digits.
fn is_host(host: &str) -> bool {
    if host.len() >= 2 && host.starts_with('[') && host.ends_with(']') {
        return is_ipv6(&host[1..host.len() - 1]);
    }
    is_ipv4(host) || is_host_name(host)
}

fn is_label(label: &str) -> bool {
    let b = label.as_bytes();
    (1..=63).contains(&b.len())
        && b[0].is_ascii_alphanumeric()
        && b[b.len() - 1].is_ascii_alphanumeric()
        && b.iter().all(|c| c.is_ascii_alphanumeric() || *c == b'-')
}

fn is_host_name(host: &str) -> bool {
    let mut labels = host.split('.').peekable();
    while let Some(label) = labels.next() {
        if !is_label(label) {
            return false;
        }
        if labels.peek().is_none() && !label.bytes().any(|c| c.is_ascii_alphabetic()) {
            return false;
        }
    }
    true
}

// 0 to 255 with no leading zero.
fn is_octet(s: &str) -> bool {
    let b = s.as_bytes();
    (1..=3).contains(&b.len()) && b.iter().all(|c| is_digit(*c)) && (b.len() == 1 || b[0] != b'0') && number(b) <= 255
}

fn is_ipv4(host: &str) -> bool {
    let parts: Vec<&str> = host.split('.').collect();
    parts.len() == 4 && parts.iter().all(|p| is_octet(p))
}

// Eight groups of one to four hex digits, or fewer around one "::", the last two of which may be a dotted IPv4
// address. No zone.
fn is_ipv6(address: &str) -> bool {
    let halves: Vec<&str> = address.split("::").collect();
    if halves.len() > 2 {
        return false;
    }
    let mut groups = 0;
    for (h, half) in halves.iter().enumerate() {
        if half.is_empty() {
            continue;
        }
        let parts: Vec<&str> = half.split(':').collect();
        for (i, part) in parts.iter().enumerate() {
            let last = h == halves.len() - 1 && i == parts.len() - 1;
            if last && is_ipv4(part) {
                groups += 2;
            } else if (1..=4).contains(&part.len()) && part.bytes().all(is_hex) {
                groups += 1;
            } else {
                return false;
            }
        }
    }
    if halves.len() == 2 {
        groups <= 7
    } else {
        groups == 8
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn has(format: &str, value: &str) -> bool {
        matches_format(format, &JsString::from(value))
    }

    #[test]
    fn durations_read_as_the_typescript_engine_reads_them() {
        assert_eq!(duration_ticks("PT5M"), Some(5 * 60 * 10_000_000));
        assert_eq!(duration_ticks("PT0.5S"), Some(5_000_000));
        assert_eq!(duration_ticks("P1DT2H30M"), Some((86_400 + 9_000) * 10_000_000));
        assert_eq!(duration_ticks("P10675199DT2H48M5.4775807S"), Some(i64::MAX as u128));
        for text in [
            "P",
            "PT",
            "P1DT",
            "PT5",
            "P1W",
            "-PT5M",
            "PT1.S",
            "PT1.12345678S",
            "P1234567890D",
            "PT5M1H",
            "P1D2H",
            "P10675199DT2H48M5.4775808S",
            "pt5m",
            "PT5M ",
        ] {
            assert_eq!(duration_ticks(text), None, "{text}");
        }
    }

    #[test]
    fn uris_follow_the_typescript_engine() {
        for ok in [
            "urn:isbn:0451450523",
            "ab:",
            "http://[::1]:8080/a",
            "file:///tmp/a",
            "mailto:a.b@example.com",
        ] {
            assert!(has("uri", ok), "{ok}");
        }
        for bad in [
            "/a/b",
            "https:",
            "c:foo",
            "http://[::1",
            "http://a..b/",
            "http://u@a/",
            "net.pipe://a:1/",
        ] {
            assert!(!has("uri", bad), "{bad}");
        }
    }

    #[test]
    fn char_counts_utf16_code_units() {
        assert!(has("char", "é"));
        assert!(!has("char", "😀"));
        assert!(matches_format("char", &JsString::from_units(&[0xd800])));
        assert!(!matches_format("uuid", &JsString::from_units(&[0xd800])));
        assert!(matches_format("something-new", &JsString::from_units(&[0xd800])));
    }
}
