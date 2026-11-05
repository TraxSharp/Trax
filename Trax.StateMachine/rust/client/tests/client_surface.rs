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

struct Table {
    languages: Vec<String>,
    types: Vec<(usize, String)>,
    members: Vec<(usize, String)>,
}

fn read_table() -> Table {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../conformance/client/PROTOCOL.md");
    let text = std::fs::read_to_string(&path).unwrap_or_else(|e| panic!("{}: {e}", path.display()));
    parse_table(&text.lines().collect::<Vec<_>>(), "Rust")
}

// The two tables, read strictly: a header row whose first cell is `Type` or `Member` and whose other cells name the
// languages, a separator, then rows of as many cells. Both tables name the same languages in the same order, and this
// test reads the column headed with its own. A language's cell is one name in backticks (`Type.member` or `Name`) or
// `—` and a reason. Anything else fails, with its line.
fn parse_table(lines: &[&str], language: &str) -> Table {
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
    let mut languages: Option<Vec<String>> = None;
    let mut i = start + 1;
    while i < end {
        let header = lines[i];
        if !header.starts_with('|') {
            i += 1;
            continue;
        }
        let mut columns = cells(header, i + 1, None);
        let first = columns.remove(0);
        let unique: BTreeSet<&String> = columns.iter().collect();
        let slot = match first.as_str() {
            "Type" => &mut types,
            "Member" => &mut members,
            _ => panic!("PROTOCOL.md:{}: a table this test does not know: {header}", i + 1),
        };
        assert!(
            !columns.is_empty() && !columns.iter().any(String::is_empty) && unique.len() == columns.len(),
            "PROTOCOL.md:{}: a table this test does not know: {header}",
            i + 1
        );
        assert!(slot.is_none(), "PROTOCOL.md:{}: a second {first} table", i + 1);
        if let Some(known) = &languages {
            assert!(
                *known == columns,
                "PROTOCOL.md:{}: the tables name different languages: {} and {}",
                i + 1,
                known.join(", "),
                columns.join(", ")
            );
        }
        let column = 1 + columns.iter().position(|c| c == language).unwrap_or_else(|| {
            panic!(
                "PROTOCOL.md:{}: the {first} table has no {language} column: {header}",
                i + 1
            )
        });
        let separator = format!("|{}", " --- |".repeat(columns.len() + 1));
        assert_eq!(
            lines.get(i + 1).copied(),
            Some(separator.as_str()),
            "PROTOCOL.md:{}: no separator row",
            i + 2
        );
        let count = columns.len() + 1;
        languages = Some(columns);
        i += 2;
        let mut names = Vec::new();
        let mut seen = BTreeSet::new();
        while i < end && lines[i].starts_with('|') {
            let cells = cells(lines[i], i + 1, Some(count));
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
        languages: languages.unwrap_or_default(),
        types: types.expect("PROTOCOL.md: no `| Type | ... |` table"),
        members: members.expect("PROTOCOL.md: no `| Member | ... |` table"),
    }
}

fn cells(line: &str, number: usize, count: Option<usize>) -> Vec<String> {
    let inner = line
        .strip_prefix("| ")
        .and_then(|l| l.strip_suffix(" |"))
        .unwrap_or_else(|| panic!("PROTOCOL.md:{number}: a row starts with `| ` and ends with ` |`"));
    let cells: Vec<String> = inner.split(" | ").map(|c| c.trim().to_string()).collect();
    if let Some(count) = count {
        assert_eq!(
            cells.len(),
            count,
            "PROTOCOL.md:{number}: a row has {count} cells, one for what it is and one per language"
        );
    }
    assert!(
        !cells[0].is_empty(),
        "PROTOCOL.md:{number}: the first cell says what the row is"
    );
    cells
}

fn name(cell: &str, number: usize) -> Option<String> {
    if let Some(reason) = cell.strip_prefix("— ") {
        let reason = reason.trim();
        assert!(!reason.is_empty(), "PROTOCOL.md:{number}: a `—` gives its reason");
        // A name behind a dash would go unchecked: it is the cell's name, not its reason.
        let only_a_name = reason
            .strip_prefix('`')
            .and_then(|r| r.strip_suffix('`'))
            .is_some_and(is_name);
        assert!(
            !only_a_name,
            "PROTOCOL.md:{number}: `{cell}` gives a name as its reason; write the name without the `—`"
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
    let table = read_table();
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
    let table = read_table();
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

// conformance/testees.json has a row per language the kits run, its `language` as the table's header names it. The
// table has a column for each of those languages and no other.
#[test]
fn the_table_has_a_column_for_each_testee_language() {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../conformance/testees.json");
    let Ok(text) = std::fs::read_to_string(&path) else {
        eprintln!("skipped: conformance/testees.json is absent, so there is no testee list to hold the columns to");
        return;
    };
    let read = trax_state_machine::Value::parse(&text).unwrap_or_else(|e| panic!("{}: {e}", path.display()));
    let list = read
        .as_array()
        .or_else(|| read.get("languages").and_then(|t| t.as_array()))
        .expect("conformance/testees.json has no `languages` list");
    let testees: BTreeSet<String> = list
        .iter()
        .map(|t| {
            t.get("language")
                .and_then(|l| l.as_str())
                .expect("conformance/testees.json has a testee without a `language` string")
                .to_string()
        })
        .collect();
    let columns: BTreeSet<String> = read_table().languages.into_iter().collect();
    assert_eq!(
        columns, testees,
        "the client surface table's languages are the testees'"
    );
}

const EXTRA: [&str; 8] = [
    "## The client surface",
    "| Type | TypeScript | C# | Rust | Swift |",
    "| --- | --- | --- | --- | --- |",
    "| The controller | `A` | `B` | `C` | `D` |",
    "",
    "| Member | TypeScript | C# | Rust | Swift |",
    "| --- | --- | --- | --- | --- |",
    "| Send | `A.send` | `B.SendAsync` | `C.send` | — not yet |",
];

#[test]
fn parse_finds_its_own_column_in_a_table_with_another_language() {
    let table = parse_table(&EXTRA, "Rust");
    assert_eq!(table.languages, ["TypeScript", "C#", "Rust", "Swift"]);
    assert_eq!(table.types, [(4, "C".to_string())]);
    assert_eq!(table.members, [(8, "C.send".to_string())]);
    assert_eq!(parse_table(&EXTRA, "TypeScript").members, [(8, "A.send".to_string())]);
}

#[test]
#[should_panic(expected = "PROTOCOL.md:2: the Type table has no Python column")]
fn parse_refuses_a_table_without_its_column() {
    parse_table(&EXTRA, "Python");
}

#[test]
#[should_panic(expected = "PROTOCOL.md:6: the tables name different languages")]
fn parse_refuses_tables_that_name_different_languages() {
    let mut lines = EXTRA;
    lines[5] = "| Member | TypeScript | C# | Rust |";
    lines[6] = "| --- | --- | --- | --- |";
    lines[7] = "| Send | `A.send` | `B.SendAsync` | `C.send` |";
    parse_table(&lines, "Rust");
}

#[test]
#[should_panic(expected = "PROTOCOL.md:4: a row has 5 cells")]
fn parse_refuses_a_row_with_a_cell_missing() {
    let mut lines = EXTRA;
    lines[3] = "| The controller | `A` | `B` | `C` |";
    parse_table(&lines, "Rust");
}
