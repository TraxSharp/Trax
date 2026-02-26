/**
 * Extracts TIn/TOut type arguments from a step class definition source text.
 *
 * Handles all Trax.Core step patterns:
 * - Step<TIn, TOut>, EffectStep<TIn, TOut>, IStep<TIn, TOut>
 * - Primary constructors: class Step(ILogger logger) : EffectStep<TIn, TOut>
 * - Nested generics: EffectStep<Unit, List<Manifest>>
 * - Tuples: EffectStep<(List<A>, List<B>), List<C>>
 * - Additional interfaces: class Step : Step<TIn, TOut>, IMyStep
 */

export interface StepTypes {
  tIn: string;
  tOut: string;
}

/**
 * Given the full source text of a file containing a step class,
 * extracts the TIn and TOut types from its base class/interface declaration.
 */
export function extractStepTypes(
  sourceText: string,
  stepTypeName: string
): StepTypes | null {
  const classIndex = sourceText.indexOf(`class ${stepTypeName}`);
  if (classIndex === -1) return null;

  const afterClass = sourceText.substring(classIndex);

  // Skip "class StepName" then skip past type params <T> and constructor params (...)
  // to find the ':' that starts the inheritance list.
  const skipStart = 6 + stepTypeName.length; // "class " + name
  const colonIndex = findInheritanceColon(afterClass, skipStart);
  if (colonIndex === -1) return null;

  // Get the inheritance list up to the class body '{' or end-of-statement ';'
  const afterColon = afterClass.substring(colonIndex + 1);
  const bodyStart = findBodyStart(afterColon);
  const inheritanceText =
    bodyStart !== -1 ? afterColon.substring(0, bodyStart) : afterColon;

  return findTwoArgGeneric(inheritanceText);
}

/**
 * Finds the ':' that starts the inheritance list, skipping past
 * balanced parentheses (constructor params) and angle brackets (type params).
 */
function findInheritanceColon(text: string, startIndex: number): number {
  let depth = 0;
  for (let i = startIndex; i < text.length; i++) {
    const ch = text[i];
    if (ch === "(" || ch === "<") depth++;
    else if (ch === ")" || ch === ">") depth--;
    else if (ch === ":" && depth === 0) return i;
    else if (ch === "{" && depth === 0) return -1;
  }
  return -1;
}

/**
 * Finds the start of the class body '{' or end of statement ';',
 * skipping balanced brackets in base type expressions.
 */
function findBodyStart(text: string): number {
  let depth = 0;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (ch === "(" || ch === "<") depth++;
    else if (ch === ")" || ch === ">") depth--;
    else if ((ch === "{" || ch === ";") && depth === 0) return i;
  }
  return -1;
}

/**
 * Scans text for the first generic type with exactly 2 top-level type arguments.
 * Handles nested generics (e.g., List<Manifest> as a single argument).
 */
function findTwoArgGeneric(text: string): StepTypes | null {
  let i = 0;
  while (i < text.length) {
    const angleStart = text.indexOf("<", i);
    if (angleStart === -1) return null;

    const args = extractGenericArgs(text, angleStart);
    if (args && args.length === 2) {
      return { tIn: args[0], tOut: args[1] };
    }

    // Skip past this balanced <...> group and try the next one
    i = skipBalancedAngles(text, angleStart);
  }
  return null;
}

/**
 * Extracts comma-separated generic type arguments from balanced angle brackets.
 * Handles nested generics and tuples:
 *   <Unit, List<Manifest>>           → ["Unit", "List<Manifest>"]
 *   <(List<A>, List<B>), List<C>>    → ["(List<A>, List<B>)", "List<C>"]
 */
function extractGenericArgs(
  text: string,
  startIndex: number
): string[] | null {
  if (text[startIndex] !== "<") return null;

  let angleDepth = 0;
  let parenDepth = 0;
  let current = "";
  const args: string[] = [];

  for (let i = startIndex + 1; i < text.length; i++) {
    const ch = text[i];

    if (ch === "<") {
      angleDepth++;
      current += ch;
    } else if (ch === ">") {
      if (angleDepth === 0) {
        args.push(current.trim());
        return args;
      }
      angleDepth--;
      current += ch;
    } else if (ch === "(") {
      parenDepth++;
      current += ch;
    } else if (ch === ")") {
      parenDepth--;
      current += ch;
    } else if (ch === "," && angleDepth === 0 && parenDepth === 0) {
      args.push(current.trim());
      current = "";
    } else {
      current += ch;
    }
  }

  return null; // Unbalanced
}

/**
 * Returns the index after a balanced <...> group.
 */
function skipBalancedAngles(text: string, startIndex: number): number {
  let depth = 0;
  for (let i = startIndex; i < text.length; i++) {
    if (text[i] === "<") depth++;
    else if (text[i] === ">") {
      depth--;
      if (depth === 0) return i + 1;
    }
  }
  return text.length;
}
