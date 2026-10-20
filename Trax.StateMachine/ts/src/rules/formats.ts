import { parseDuration } from "../duration";

// The string formats a field's CLR type gives it, checked as the C# engine's ValueFormats checks them: every pattern
// names ASCII digits and letters only, and each value is checked for what a pattern cannot say, such as February 30.
// A format accepts what System.Text.Json writes for its type and refuses what the type cannot hold. Never throws.

/** The formats the exported schema names. */
export type ValueFormat =
  | "uuid"
  | "date-time"
  | "date-time-offset"
  | "date"
  | "time"
  | "duration"
  | "uri"
  | "char";

const uuid = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const date = /^([0-9]{4})-([0-9]{2})-([0-9]{2})$/;
const time = /^([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\.[0-9]{1,7})?$/;
const dateTime =
  /^([0-9]{4})-([0-9]{2})-([0-9]{2})T([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\.[0-9]{1,7})?(?:(Z)|([+-])([0-9]{2}):([0-9]{2}))?$/;
const scheme = /^([A-Za-z][A-Za-z0-9+.-]+):/;
const mailbox = /^[A-Za-z0-9._+-]+$/;
const label = "[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?";
const hostName = new RegExp(`^(?:${label}\\.)*(?=[A-Za-z0-9-]*[A-Za-z])${label}$`);
const port = /^[0-9]{1,5}$/;
const hexGroup = /^[0-9A-Fa-f]{1,4}$/;
const octet = "(?:25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9]?[0-9])";
const ipv4 = new RegExp(`^(?:${octet}\\.){3}${octet}$`);
const uriText = /^(?:[A-Za-z0-9\-._~!$&'()*+,;=:@/?]|%[0-9A-Fa-f]{2})*$/;

/** Whether `value` has `format`; an unknown format accepts anything. */
export function matchesFormat(format: string, value: string): boolean {
  switch (format) {
    case "uuid":
      return uuid.test(value);
    case "date-time":
      return isDateTime(value, true);
    case "date-time-offset":
      return isDateTime(value, false);
    case "date": {
      const m = date.exec(value);
      return m !== null && isDay(m, 1);
    }
    case "time": {
      const m = time.exec(value);
      return m !== null && isClock(m, 1);
    }
    case "duration":
      return parseDuration(value) !== null;
    case "uri":
      return isUri(value);
    case "char":
      return value.length === 1;
    default:
      return true;
  }
}

/**
 * Whether `value` is one of an enum's `names`, as the C# engine compares them: exactly, for a name in `exact` (a
 * member renamed with `[JsonStringEnumMemberName]`, which System.Text.Json reads case-sensitively), else ignoring
 * ASCII case only.
 */
export function isOneOf(value: string, names: readonly string[], exact?: readonly string[]): boolean {
  return names.some((name) => (exact?.includes(name) ? value === name : asciiEqualsIgnoreCase(value, name)));
}

function asciiEqualsIgnoreCase(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (asciiLower(a.charCodeAt(i)) !== asciiLower(b.charCodeAt(i))) return false;
  return true;
}

const asciiLower = (unit: number): number => (unit >= 0x41 && unit <= 0x5a ? unit + 32 : unit);

// A date-time whose UTC instant is inside years 1 to 9999, which is all a C# DateTime or DateTimeOffset holds. One
// with an offset is read as that offset says; one with none is read in the server's zone by a DateTimeOffset, whose
// offset is not known here, so it is refused on the range's first and last day, where some zone would push it out. A
// DateTime holds one with no offset as it is (zoneless).
function isDateTime(value: string, zoneless: boolean): boolean {
  const m = dateTime.exec(value);
  if (m === null || !isDay(m, 1) || !isClock(m, 4)) return false;
  const local = minuteOf(m);
  if (m[8] !== undefined) {
    const hours = Number(m[9]);
    const minutes = Number(m[10]);
    if (minutes >= 60 || hours * 60 + minutes > 14 * 60) return false;
    const offset = (hours * 60 + minutes) * (m[8] === "-" ? -1 : 1);
    return local - offset >= 0 && local - offset <= lastMinute;
  }
  return zoneless || m[7] !== undefined || (local >= 24 * 60 && local <= lastMinute - 24 * 60);
}

// The day a valid date is in the proleptic Gregorian calendar, counted from 0001-01-01.
function dayOf(year: number, month: number, day: number): number {
  const y = year - 1;
  const days = 365 * y + Math.floor(y / 4) - Math.floor(y / 100) + Math.floor(y / 400);
  const before = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  return days + before[month - 1] + (leap && month > 2 ? 1 : 0) + day - 1;
}

// The minute 9999-12-31T23:59 is from 0001-01-01T00:00, the last a DateTime holds.
const lastMinute = dayOf(9999, 12, 31) * 24 * 60 + 23 * 60 + 59;

// The minute groups 1 to 5 (a valid year, month, day, hour and minute) name, counted from 0001-01-01T00:00.
function minuteOf(m: RegExpExecArray): number {
  return dayOf(Number(m[1]), Number(m[2]), Number(m[3])) * 24 * 60 + Number(m[4]) * 60 + Number(m[5]);
}

// Groups first, first + 1 and first + 2 hold a year, month and day that name a real day of the proleptic Gregorian
// calendar from year 1.
function isDay(m: RegExpExecArray, first: number): boolean {
  const year = Number(m[first]);
  const month = Number(m[first + 1]);
  const day = Number(m[first + 2]);
  if (year < 1 || month < 1 || month > 12 || day < 1) return false;
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  return day <= days[month - 1];
}

// Groups first, first + 1 and first + 2 hold an hour, minute and second of one day, with no leap second.
function isClock(m: RegExpExecArray, first: number): boolean {
  return Number(m[first]) < 24 && Number(m[first + 1]) < 60 && Number(m[first + 2]) < 60;
}

// An absolute URI that .NET's System.Uri reads, in a form it reads the same on every platform, checked as the C#
// engine's ValueFormats checks it: a scheme of two or more characters (one letter would be a Windows drive), then an
// authority and a path, or a path alone; a query and a fragment after either. The authority is a host
// (dot-separated letter-digit-hyphen labels, a dotted IPv4 address or a bracketed IPv6 one) and an optional port of at
// most 65535, with no user; a file URI's may be empty, and neither it nor a net.pipe one has a port; a file URI's path
// holds no ':'. A scheme
// System.Uri parses with rules of its own (knownSchemes) takes an authority, except mailto, which takes one plain
// address. Every other character is an RFC 3986 unreserved or reserved one, or a well-formed %HH escape.
function isUri(value: string): boolean {
  if (value.length > maxUriLength) return false;
  const match = scheme.exec(value);
  if (match === null) return false;
  const name = match[1].toLowerCase();
  let rest = value.slice(match[0].length);
  if (name === "mailto") {
    const query = rest.indexOf("?");
    const address = query < 0 ? rest : rest.slice(0, query);
    const at = address.indexOf("@");
    return (
      at > 0 &&
      mailbox.test(address.slice(0, at)) &&
      isHost(address.slice(at + 1)) &&
      (query < 0 || uriText.test(rest.slice(query + 1)))
    );
  }
  if (rest.startsWith("//")) {
    let end = rest.slice(2).search(/[/?#]/);
    end = end < 0 ? rest.length : end + 2;
    if (!isAuthority(rest.slice(2, end), name)) return false;
    // A file URI's path starting "//" would be read as a UNC share, and a ':' in it as a drive.
    if (name === "file" && (rest.slice(end).startsWith("//") || rest.slice(end).includes(":"))) return false;
    rest = rest.slice(end);
  } else if (knownSchemes.has(name)) return false;
  const hash = rest.indexOf("#");
  return hash < 0 ? uriText.test(rest) : uriText.test(rest.slice(0, hash)) && uriText.test(rest.slice(hash + 1));
}

// The longest string System.Uri reads.
const maxUriLength = 65519;

// The schemes System.Uri parses with rules of its own.
const knownSchemes = new Set([
  "http",
  "https",
  "ws",
  "wss",
  "ftp",
  "file",
  "gopher",
  "nntp",
  "news",
  "mailto",
  "uuid",
  "telnet",
  "ldap",
  "net.tcp",
  "net.pipe",
  "vsmacros",
]);

function isAuthority(authority: string, name: string): boolean {
  if (name === "file") return authority.length === 0 || isHost(authority);
  if (name === "net.pipe") return isHost(authority);
  const bracketed = authority.startsWith("[");
  if (bracketed && authority.indexOf("]") < 0) return false;
  const colon = bracketed ? authority.indexOf(":", authority.indexOf("]") + 1) : authority.indexOf(":");
  const host = colon < 0 ? authority : authority.slice(0, colon);
  const portText = colon < 0 ? null : authority.slice(colon + 1);
  return isHost(host) && (portText === null || (port.test(portText) && Number(portText) <= 65535));
}

// A bracketed IPv6 address, a dotted IPv4 address, or letter-digit-hyphen labels whose last is not all digits.
function isHost(host: string): boolean {
  return host.startsWith("[") && host.endsWith("]")
    ? isIPv6(host.slice(1, -1))
    : ipv4.test(host) || hostName.test(host);
}

// An IPv6 address as RFC 4291 writes it: eight groups of one to four hex digits, or fewer around one "::", the last
// two of which may be a dotted IPv4 address. No zone.
function isIPv6(address: string): boolean {
  const halves = address.split("::");
  if (halves.length > 2) return false;
  let groups = 0;
  for (let h = 0; h < halves.length; h++) {
    if (halves[h].length === 0) continue;
    const parts = halves[h].split(":");
    for (let i = 0; i < parts.length; i++) {
      const last = h === halves.length - 1 && i === parts.length - 1;
      if (last && ipv4.test(parts[i])) groups += 2;
      else if (hexGroup.test(parts[i])) groups += 1;
      else return false;
    }
  }
  return halves.length === 2 ? groups <= 7 : groups === 8;
}
