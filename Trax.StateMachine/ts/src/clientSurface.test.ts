import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import { describe, expect, it } from 'vitest';

// The TypeScript client against the table in conformance/client/PROTOCOL.md ("The client surface"), which names each
// member of the three clients side by side. Every TypeScript name the table gives must be exported by one of the
// package's entry points, and every public member of a type the table covers must be in it, so a member added here,
// or in another client, is a row the pull request has to write. The C# and Rust clients run the same check
// (ClientSurfaceTests, rust/client/tests/client_surface.rs).

const here = path.dirname(fileURLToPath(import.meta.url));
const protocol = path.join(here, '..', '..', 'conformance', 'client', 'PROTOCOL.md');
const testees = path.join(here, '..', '..', 'conformance', 'testees.json');

interface Named {
  line: number;
  name: string;
}

interface Table {
  languages: string[];
  types: Named[];
  members: Named[];
}

// The two tables, read strictly: a header row whose first cell is `Type` or `Member` and whose other cells name the
// languages, a separator, then rows of as many cells. Both tables name the same languages in the same order, and this
// test reads the column headed with its own. A language's cell is one name in backticks (`Type.member` or `Name`) or
// `—` and a reason. Anything else fails, with its line.
function parseTable(lines: string[], language: string): Table {
  const start = lines.indexOf('## The client surface');
  if (start < 0) throw new Error('PROTOCOL.md has no `## The client surface` section');
  const next = lines.findIndex((l, i) => i > start && l.startsWith('## '));
  const end = next < 0 ? lines.length : next;
  const tables: Record<string, Named[] | undefined> = {};
  let languages: string[] | undefined;
  let i = start + 1;
  while (i < end) {
    const header = lines[i];
    if (!header.startsWith('|')) {
      i++;
      continue;
    }
    const [first, ...columns] = cellsOf(header, i + 1, null);
    const slot = first === 'Type' ? 'types' : first === 'Member' ? 'members' : undefined;
    if (!slot || columns.length === 0 || columns.some((c) => c === '') || new Set(columns).size !== columns.length)
      throw new Error(`PROTOCOL.md:${i + 1}: a table this test does not know: ${header}`);
    if (tables[slot]) throw new Error(`PROTOCOL.md:${i + 1}: a second ${first} table`);
    if (languages && languages.join(' | ') !== columns.join(' | '))
      throw new Error(`PROTOCOL.md:${i + 1}: the tables name different languages: ${languages.join(', ')} and ${columns.join(', ')}`);
    languages = columns;
    const column = columns.indexOf(language) + 1;
    if (column === 0) throw new Error(`PROTOCOL.md:${i + 1}: the ${first} table has no ${language} column: ${header}`);
    if (lines[i + 1] !== `| ${['---', ...columns.map(() => '---')].join(' | ')} |`)
      throw new Error(`PROTOCOL.md:${i + 2}: no separator row`);
    i += 2;
    const names: Named[] = [];
    const seen = new Set<string>();
    for (; i < end && lines[i].startsWith('|'); i++) {
      const name = nameOf(cellsOf(lines[i], i + 1, columns.length + 1)[column], i + 1);
      if (name === null) continue;
      if (seen.has(name)) throw new Error(`PROTOCOL.md:${i + 1}: \`${name}\` is named twice in this table`);
      seen.add(name);
      names.push({ line: i + 1, name });
    }
    tables[slot] = names;
  }
  if (!tables.types) throw new Error('PROTOCOL.md: no `| Type | ... |` table');
  if (!tables.members) throw new Error('PROTOCOL.md: no `| Member | ... |` table');
  return { languages: languages ?? [], types: tables.types, members: tables.members };
}

function readTable(): Table {
  return parseTable(readFileSync(protocol, 'utf8').replace(/\r\n/g, '\n').split('\n'), 'TypeScript');
}

function cellsOf(line: string, number: number, count: number | null): string[] {
  if (!line.startsWith('| ') || !line.endsWith(' |'))
    throw new Error(`PROTOCOL.md:${number}: a row starts with \`| \` and ends with \` |\``);
  const cells = line
    .slice(2, -2)
    .split(' | ')
    .map((c) => c.trim());
  if (count !== null && cells.length !== count)
    throw new Error(`PROTOCOL.md:${number}: a row has ${count} cells, one for what it is and one per language`);
  if (cells[0] === '') throw new Error(`PROTOCOL.md:${number}: the first cell says what the row is`);
  return cells;
}

function nameOf(cell: string, number: number): string | null {
  const name = /^`([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)`$/;
  if (cell.startsWith('— ')) {
    const reason = cell.slice(2).trim();
    if (reason === '') throw new Error(`PROTOCOL.md:${number}: a \`—\` gives its reason`);
    // A name behind a dash would go unchecked: it is the cell's name, not its reason.
    if (name.test(reason))
      throw new Error(`PROTOCOL.md:${number}: \`${cell}\` gives a name as its reason; write the name without the \`—\``);
    return null;
  }
  const match = name.exec(cell);
  if (!match)
    throw new Error(
      `PROTOCOL.md:${number}: \`${cell}\` is neither one name in backticks (\`Type.member\` or \`Name\`) nor \`—\` and a reason`,
    );
  return match[1];
}

// What the package's entry points export, by name, read with the compiler as exports.test.ts reads them.
function packageSurface(): { exports: Map<string, ts.Symbol>; members(name: string): Set<string> | null } {
  const entryPoints = [
    path.join(here, 'index.ts'),
    path.join(here, 'client', 'index.ts'),
    path.join(here, 'react.ts'),
  ];
  const config = ts.readConfigFile(path.join(here, '..', 'tsconfig.json'), ts.sys.readFile);
  const options = ts.parseJsonConfigFileContent(config.config, ts.sys, path.join(here, '..')).options;
  const program = ts.createProgram(entryPoints, options);
  const checker = program.getTypeChecker();
  const exports = new Map<string, ts.Symbol>();
  for (const file of entryPoints) {
    const source = program.getSourceFile(file);
    const entry = source && checker.getSymbolAtLocation(source);
    if (!entry) throw new Error(`${file} is not a module`);
    for (const symbol of checker.getExportsOfModule(entry)) {
      const resolved = symbol.flags & ts.SymbolFlags.Alias ? checker.getAliasedSymbol(symbol) : symbol;
      exports.set(symbol.getName(), resolved);
    }
  }

  const isPublic = (member: ts.Symbol): boolean =>
    !member.getName().startsWith('#') &&
    (member.declarations ?? []).every(
      (d) => (ts.getCombinedModifierFlags(d) & (ts.ModifierFlags.Private | ts.ModifierFlags.Protected)) === 0,
    );

  // A class's public instance and static members, or an interface's or object type's properties, inherited ones
  // included. Null for an export that is not a type with members.
  const members = (name: string): Set<string> | null => {
    const symbol = exports.get(name);
    if (!symbol || !(symbol.flags & (ts.SymbolFlags.Class | ts.SymbolFlags.Interface | ts.SymbolFlags.TypeAlias)))
      return null;
    const result = new Set<string>();
    for (const member of checker.getPropertiesOfType(checker.getDeclaredTypeOfSymbol(symbol)))
      if (isPublic(member)) result.add(member.getName());
    if (symbol.flags & ts.SymbolFlags.Class)
      for (const member of checker.getPropertiesOfType(checker.getTypeOfSymbol(symbol)))
        if (member.getName() !== 'prototype' && isPublic(member)) result.add(member.getName());
    return result;
  };
  return { exports, members };
}

describe('the client surface table', () => {
  const table = readTable();
  let read: ReturnType<typeof packageSurface> | undefined;
  const surface = () => (read ??= packageSurface());

  it('names only what the package exports', () => {
    const missing = [...table.types, ...table.members]
      .filter(({ name }) => {
        const dot = name.indexOf('.');
        if (dot < 0) return !surface().exports.has(name);
        return !surface().members(name.slice(0, dot))?.has(name.slice(dot + 1));
      })
      .map(({ line, name }) => `PROTOCOL.md:${line}: \`${name}\``);
    expect(
      missing,
      'The client surface table names TypeScript members that @traxsharp/state-machine does not export. Fix the row, or the package.',
    ).toEqual([]);
  }, 30_000);

  it('names every public member of each type it covers', () => {
    const named = new Set(table.members.map((m) => m.name));
    const missing: string[] = [];
    for (const { line, name } of table.types) {
      const members = surface().members(name);
      if (!members) throw new Error(`PROTOCOL.md:${line}: \`${name}\` is not a type the package exports`);
      for (const member of [...members].sort()) if (!named.has(`${name}.${member}`)) missing.push(`${name}.${member}`);
    }
    expect(
      missing,
      'These public TypeScript members are in no row of the client surface table in conformance/client/PROTOCOL.md. ' +
        'Give each a row with its C# and Rust names, or `—` and the reason a language has none.',
    ).toEqual([]);
  }, 30_000);
});

// conformance/testees.json has a row per language the kits run, its `language` as the table's header names it. The
// table has a column for each of those languages and no other.
function testeeLanguages(): string[] {
  const read: unknown = JSON.parse(readFileSync(testees, 'utf8'));
  const list = Array.isArray(read) ? read : (read as { languages?: unknown }).languages;
  if (!Array.isArray(list)) throw new Error('conformance/testees.json has no `languages` list');
  const languages = list.map((t: { language?: unknown }) => t.language);
  if (languages.some((l) => typeof l !== 'string'))
    throw new Error('conformance/testees.json has a testee without a `language` string');
  return [...new Set(languages as string[])].sort();
}

describe('the client surface table, read', () => {
  const extra = [
    '## The client surface',
    '| Type | TypeScript | C# | Rust | Swift |',
    '| --- | --- | --- | --- | --- |',
    '| The controller | `A` | `B` | `C` | `D` |',
    '',
    '| Member | TypeScript | C# | Rust | Swift |',
    '| --- | --- | --- | --- | --- |',
    '| Send | `A.send` | `B.SendAsync` | `C.send` | — not yet |',
  ];

  it('finds its own column in a table with another language', () => {
    const table = parseTable(extra, 'TypeScript');
    expect(table.languages).toEqual(['TypeScript', 'C#', 'Rust', 'Swift']);
    expect(table.types).toEqual([{ line: 4, name: 'A' }]);
    expect(table.members).toEqual([{ line: 8, name: 'A.send' }]);
    expect(parseTable(extra, 'Rust').members).toEqual([{ line: 8, name: 'C.send' }]);
  });

  it('refuses a table without its column', () => {
    expect(() => parseTable(extra, 'Python')).toThrow('PROTOCOL.md:2: the Type table has no Python column');
  });

  it('refuses tables that name different languages', () => {
    const lines = [...extra];
    lines[5] = '| Member | TypeScript | C# | Rust |';
    lines[6] = '| --- | --- | --- | --- |';
    lines[7] = '| Send | `A.send` | `B.SendAsync` | `C.send` |';
    expect(() => parseTable(lines, 'TypeScript')).toThrow('PROTOCOL.md:6: the tables name different languages');
  });

  it('refuses a row with a cell missing', () => {
    const lines = [...extra];
    lines[3] = '| The controller | `A` | `B` | `C` |';
    expect(() => parseTable(lines, 'TypeScript')).toThrow('PROTOCOL.md:4: a row has 5 cells');
  });
});

describe('the client surface table, against the testees', () => {
  it('has a column for each language a testee is in, and no other', (context) => {
    if (!existsSync(testees)) {
      context.skip('conformance/testees.json is absent, so there is no testee list to hold the columns to');
      return;
    }
    expect([...readTable().languages].sort()).toEqual(testeeLanguages());
  });
});
