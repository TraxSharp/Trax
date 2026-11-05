// How deep the arrays and objects of a value may nest, the outermost counting as one: the Rust engine's limit, which
// every engine holds an IR, a reduction's context and a plan's fields input and context to, so they refuse the same
// values.
export const MAX_NESTING = 512;

// Whether `value` nests deeper than MAX_NESTING. Never recurses, so a value built in memory cannot exhaust the stack.
export const nestsTooDeep = (value: unknown): boolean => {
  const pending: [unknown, number][] = [[value, 0]];
  for (let next = pending.pop(); next !== undefined; next = pending.pop()) {
    const [member, depth] = next;
    if (member === null || typeof member !== 'object') continue;
    if (depth >= MAX_NESTING) return true;
    for (const inner of Array.isArray(member) ? member : Object.values(member)) pending.push([inner, depth + 1]);
  }
  return false;
};
