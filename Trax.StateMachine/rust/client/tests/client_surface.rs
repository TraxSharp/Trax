// The Rust client against the table in conformance/client/PROTOCOL.md ("The client surface"), which names each member
// of the three clients side by side. Every Rust name the table gives must be on the crates' public surface, and every
// public member of a type the table covers must be in it, so a member added here, or in another client, is a row the
// pull request has to write. The C# and TypeScript clients run the same check (ClientSurfaceTests,
// clientSurface.test.ts).

#[path = "../../tests/support/surface.rs"]
mod surface;

use std::collections::BTreeSet;
use std::path::PathBuf;

use surface::Surface;

const RUST: usize = 3;

struct Table {
    types: Vec<(usize, String)>,
    members: Vec<(usize, String)>,
}

// The two tables, read strictly: a header row as written, a separator, then rows of four cells. A language's cell is
// one name in backticks (`Type.member` or `Name`) or `—` and a reason. Anything else fails, with its line.
fn read_table(column: usize) -> Table {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../conformance/client/PROTOCOL.md");
    let text = std::fs::read_to_string(&path).unwrap_or_else(|e| panic!("{}: {e}", path.display()));
    let lines: Vec<&str> = text.lines().collect();
    let start = lines
        .iter()
        .position(|l| *l == "## The client surface")
        .expect("PROTOCOL.md has no `## The client surface` section");
    let end = lines[start + 1..]
        .iter()
        .position(|l| l.starts_with("## "))
        .map_or(lines.len(), |i| start + 1 + i);

    let mut types = None;
    let mut members = None;
    let mut i = start + 1;
    while i < end {
        let header = lines[i];
        if !header.starts_with('|') {
            i += 1;
            continue;
        }
        let slot = match header {
            "| Type | TypeScript | C# | Rust |" => &mut types,
            "| Member | TypeScript | C# | Rust |" => &mut members,
            _ => panic!("PROTOCOL.md:{}: a table this test does not know: {header}", i + 1),
        };
        assert!(slot.is_none(), "PROTOCOL.md:{}: a second {header} table", i + 1);
        assert_eq!(
            lines.get(i + 1).copied(),
            Some("| --- | --- | --- | --- |"),
            "PROTOCOL.md:{}: no separator row",
            i + 2
        );
        i += 2;
        let mut names = Vec::new();
        let mut seen = BTreeSet::new();
        while i < end && lines[i].starts_with('|') {
            let cells = cells(lines[i], i + 1);
            if let Some(name) = name(&cells[column], i + 1) {
                assert!(
                    seen.insert(name.clone()),
                    "PROTOCOL.md:{}: `{name}` is named twice in this table",
                    i + 1
                );
                names.push((i + 1, name));
            }
            i += 1;
        }
        *slot = Some(names);
    }
    Table {
        types: types.expect("PROTOCOL.md: no `| Type | TypeScript | C# | Rust |` table"),
        members: members.expect("PROTOCOL.md: no `| Member | TypeScript | C# | Rust |` table"),
    }
}

fn cells(line: &str, number: usize) -> Vec<String> {
    let inner = line
        .strip_prefix("| ")
        .and_then(|l| l.strip_suffix(" |"))
        .unwrap_or_else(|| panic!("PROTOCOL.md:{number}: a row starts with `| ` and ends with ` |`"));
    let cells: Vec<String> = inner.split(" | ").map(|c| c.trim().to_string()).collect();
    assert_eq!(cells.len(), 4, "PROTOCOL.md:{number}: a row has four cells");
    assert!(
        !cells[0].is_empty(),
        "PROTOCOL.md:{number}: the first cell says what the row is"
    );
    cells
}

fn name(cell: &str, number: usize) -> Option<String> {
    if let Some(reason) = cell.strip_prefix("— ") {
        assert!(
            !reason.trim().is_empty(),
            "PROTOCOL.md:{number}: a `—` gives its reason"
        );
        return None;
    }
    let name = cell
        .strip_prefix('`')
        .and_then(|c| c.strip_suffix('`'))
        .filter(|n| is_name(n))
        .unwrap_or_else(|| {
            panic!("PROTOCOL.md:{number}: `{cell}` is neither one name in backticks (`Type.member` or `Name`) nor `—` and a reason")
        });
    Some(name.to_string())
}

fn is_name(text: &str) -> bool {
    let parts: Vec<&str> = text.split('.').collect();
    parts.len() <= 2
        && parts.iter().all(|p| {
            let mut chars = p.chars();
            chars.next().is_some_and(|c| c.is_ascii_alphabetic() || c == '_')
                && chars.all(|c| c.is_ascii_alphanumeric() || c == '_')
        })
}

struct Crates {
    client: Surface,
    engine: Surface,
}

impl Crates {
    fn read() -> Crates {
        let dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
        Crates {
            client: Surface::read(&dir),
            engine: Surface::read(&dir.join("..")),
        }
    }

    fn exports(&self, name: &str) -> bool {
        self.client.items.contains_key(name) || self.engine.items.contains_key(name)
    }

    fn members(&self, ty: &str) -> Option<BTreeSet<String>> {
        self.client.members(ty).or_else(|| self.engine.members(ty))
    }
}

#[test]
fn every_rust_name_the_table_gives_is_public() {
    let table = read_table(RUST);
    let crates = Crates::read();
    let mut missing = Vec::new();
    for (line, name) in table.types.iter().chain(&table.members) {
        let found = match name.split_once('.') {
            None => crates.exports(name),
            Some((ty, member)) => crates.members(ty).is_some_and(|m| m.contains(member)),
        };
        if !found {
            missing.push(format!("PROTOCOL.md:{line}: `{name}`"));
        }
    }
    assert!(
        missing.is_empty(),
        "The client surface table names Rust members that trax-state-machine-client and trax-state-machine do not \
         export. Fix the row, or the crate:\n  {}",
        missing.join("\n  ")
    );
}

#[test]
fn every_public_member_of_a_covered_type_is_in_the_table() {
    let table = read_table(RUST);
    let crates = Crates::read();
    let named: BTreeSet<&str> = table.members.iter().map(|(_, n)| n.as_str()).collect();
    let mut missing = Vec::new();
    for (line, ty) in &table.types {
        let members = crates
            .members(ty)
            .unwrap_or_else(|| panic!("PROTOCOL.md:{line}: `{ty}` is not a type the Rust crates export"));
        for member in members {
            let name = format!("{ty}.{member}");
            if !named.contains(name.as_str()) {
                missing.push(name);
            }
        }
    }
    assert!(
        missing.is_empty(),
        "These public Rust members are in no row of the client surface table in conformance/client/PROTOCOL.md. Give \
         each a row with its TypeScript and C# names, or `—` and the reason a language has none:\n  {}",
        missing.join("\n  ")
    );
}
