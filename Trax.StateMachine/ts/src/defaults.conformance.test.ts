import fs from 'node:fs';
import path from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { conformanceFile } from './fixtures';
import { EXPANSION_LIMIT, MAX_NESTING } from './plan';
import { MAX_STORED_DEPTH } from './storedText';
import { DEFAULT_RESUBSCRIBE_DELAYS, REFUSED_CLOSES, jittered } from './client/machineServer';
import { DEFAULT_MAX_RESPONSE_SIZE, DEFAULT_REQUEST_TIMEOUT, LONGEST_TIMER } from './client/transport';
import { DEFAULT_RETRY_DELAYS } from './server/serverController';

// conformance/client/defaults.json and conformance/limits.json hold every default and limit once; this holds the
// package's own constants, and the operations client trax client writes, to them. The C# and Rust clients hold theirs
// to the same files.
const read = (...parts: string[]) => JSON.parse(fs.readFileSync(conformanceFile(...parts), 'utf8'));
const defaults = read('client', 'defaults.json');
const limits = read('limits.json');

afterEach(() => vi.restoreAllMocks());

describe('the client defaults', () => {
  it('are the conformance kit\'s', () => {
    expect(DEFAULT_REQUEST_TIMEOUT).toBe(defaults.requestTimeoutMs);
    expect(DEFAULT_RETRY_DELAYS).toEqual(defaults.retryDelaysMs);
    expect(DEFAULT_RESUBSCRIBE_DELAYS).toEqual(defaults.resubscribeDelaysMs);
    expect(DEFAULT_MAX_RESPONSE_SIZE).toBe(defaults.maxResponseBytes);
    // The TypeScript client owns neither its socket's messages nor its timing.
    expect(defaults.maxSocketMessageBytes.ts).toBeNull();
    expect(defaults.socket.ts).toBeNull();
  });

  it('jitter a resubscribe wait between the kit\'s bounds, a draw of 0 waiting the whole step', () => {
    vi.spyOn(Math, 'random').mockReturnValue(0);
    expect(jittered(1000)).toBe(1000 * defaults.resubscribeJitter.mostFraction);
    vi.spyOn(Math, 'random').mockReturnValue(1);
    expect(jittered(1000)).toBe(1000 * defaults.resubscribeJitter.leastFraction);
  });

  it('turn a caller away on the refused close codes only', () => {
    expect(Object.fromEntries(REFUSED_CLOSES)).toEqual(defaults.closeCodes.refused);
    for (const code of defaults.closeCodes.transient) expect(REFUSED_CLOSES.has(code)).toBe(false);
  });

  // The operations client is generated text (Trax.Cli's ClientGenerator), committed for the operations kit, and its
  // own tests check the committed file is what the CLI writes.
  it('are the generated operations client\'s', () => {
    const client = fs.readFileSync(
      path.join(conformanceFile(), '..', 'ts', 'tools', 'conformance', 'operations', 'client', 'client.g.ts'),
      'utf8',
    );
    const constant = (name: string): number => {
      const match = new RegExp(`const ${name} = ([\\d_ *]+);`).exec(client);
      if (match === null) throw new Error(`client.g.ts declares no ${name}.`);
      return match[1].split('*').reduce((product, factor) => product * Number(factor.trim().replaceAll('_', '')), 1);
    };
    expect(constant('defaultMaxResponseBytes')).toBe(defaults.maxResponseBytes);
    expect(constant('defaultRequestTimeout')).toBe(defaults.requestTimeoutMs);
    const retried = /shouldRetry: \(error\) => isCloseEvent\(error\)((?: && error\.code !== \d+)+),/.exec(client);
    expect(retried, 'client.g.ts names the closes it does not retry').not.toBeNull();
    const notRetried = [...retried![1].matchAll(/\d+/g)].map((m) => m[0]);
    expect(notRetried.sort()).toEqual(Object.keys(defaults.closeCodes.refused).sort());
  });
});

describe('the shared limits', () => {
  it('are the conformance kit\'s', () => {
    expect(MAX_NESTING).toBe(limits.maxValueDepth);
    expect(MAX_STORED_DEPTH).toBe(limits.maxStoredDepth);
    expect(EXPANSION_LIMIT).toBe(limits.maxPlanExpansions);
    expect(LONGEST_TIMER).toBe(limits.maxTimerMs);
  });
});

// PROTOCOL.md's "Defaults" table is what a reader goes by, so it states the same numbers as the file.
describe('the Defaults table of conformance/client/PROTOCOL.md', () => {
  const protocol = fs.readFileSync(conformanceFile('client', 'PROTOCOL.md'), 'utf8');
  const section = protocol.slice(protocol.indexOf('\n## Defaults\n'), protocol.indexOf('\n## ', protocol.indexOf('\n## Defaults\n') + 1));
  const row = (option: string): string => {
    const match = new RegExp(`^\\| ${option} \\| ([^|]+) \\|`, 'm').exec(section);
    if (match === null) throw new Error(`The Defaults table has no ${option} row.`);
    return match[1].trim();
  };
  // "30 s", "250, 500 ms" or "16 MiB", in milliseconds or bytes.
  const amounts = (cell: string): number[] => {
    const match = /^([\d, ]+) (ms|s|MiB)$/.exec(cell);
    if (match === null) throw new Error(`"${cell}" is not a list of numbers with a unit.`);
    const unit = { ms: 1, s: 1000, MiB: 1024 * 1024 }[match[2]]!;
    return match[1].split(',').map((n) => Number(n.trim()) * unit);
  };

  it('states the file\'s values', () => {
    expect(section).not.toBe('');
    expect(amounts(row('Request timeout'))).toEqual([defaults.requestTimeoutMs]);
    expect(amounts(row('Retry delays'))).toEqual(defaults.retryDelaysMs);
    expect(amounts(row('Resubscribe delays'))).toEqual(defaults.resubscribeDelaysMs);
    expect(amounts(row('Response size'))).toEqual([defaults.maxResponseBytes]);
  });

  it('states the file\'s jitter bounds', () => {
    const match = /drawn uniformly between (\d+)% and (\d+)% of its step/.exec(section);
    expect(match, 'the section says how a resubscribe wait is drawn').not.toBeNull();
    expect([Number(match![1]) / 100, Number(match![2]) / 100]).toEqual([
      defaults.resubscribeJitter.leastFraction,
      defaults.resubscribeJitter.mostFraction,
    ]);
  });
});
