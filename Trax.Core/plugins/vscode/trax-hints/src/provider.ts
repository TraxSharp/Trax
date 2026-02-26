import * as vscode from "vscode";
import { extractStepTypes, StepTypes } from "./typeParser.js";

interface ChainMatch {
  methodName: string;
  stepTypeName: string;
  hintPosition: vscode.Position;
  typeNamePosition: vscode.Position;
}

/**
 * Provides inlay hints for Trax.Core workflow chains, showing TIn → TOut
 * for each .Chain<StepType>(), .ShortCircuit<StepType>(), and .IChain<StepType>() call.
 */
export class ChainHintsProvider implements vscode.InlayHintsProvider {
  /** Cache: step type name → successfully resolved types */
  private typeCache = new Map<string, StepTypes>();

  /** Step types that permanently failed resolution (not a step class) */
  private unresolvable = new Set<string>();

  private _onDidChangeInlayHints = new vscode.EventEmitter<void>();
  readonly onDidChangeInlayHints = this._onDidChangeInlayHints.event;

  private static readonly CHAIN_PATTERN =
    /\.(Chain|ShortCircuit|IChain)<(\w+)>\(\)/g;

  constructor() {}

  async provideInlayHints(
    document: vscode.TextDocument,
    range: vscode.Range,
    token: vscode.CancellationToken
  ): Promise<vscode.InlayHint[]> {
    const text = document.getText();
    ChainHintsProvider.CHAIN_PATTERN.lastIndex = 0;

    // Collect all chain calls in the visible range
    const matches: ChainMatch[] = [];
    let match: RegExpExecArray | null;
    while (
      (match = ChainHintsProvider.CHAIN_PATTERN.exec(text)) !== null
    ) {
      if (token.isCancellationRequested) return [];

      const matchStart = document.positionAt(match.index);
      const matchEnd = document.positionAt(match.index + match[0].length);
      if (matchEnd.isBefore(range.start) || matchStart.isAfter(range.end)) {
        continue;
      }

      const methodName = match[1];
      const stepTypeName = match[2];
      const typeNameOffset = match.index + 1 + methodName.length + 1;

      matches.push({
        methodName,
        stepTypeName,
        hintPosition: matchEnd,
        typeNamePosition: document.positionAt(typeNameOffset),
      });
    }

    if (matches.length === 0) return [];

    // Resolve any uncached types (awaiting the definition provider directly)
    const toResolve = matches.filter(
      (m) =>
        !this.typeCache.has(m.stepTypeName) &&
        !this.unresolvable.has(m.stepTypeName)
    );

    if (toResolve.length > 0) {
      // Deduplicate by step type name
      const unique = new Map<string, ChainMatch>();
      for (const m of toResolve) {
        if (!unique.has(m.stepTypeName)) unique.set(m.stepTypeName, m);
      }

      // Resolve all in parallel
      await Promise.all(
        [...unique.values()].map((m) =>
          this.resolveAndCache(document.uri, m.stepTypeName, m.typeNamePosition)
        )
      );
    }

    // Build hints from cache using InlayHintLabelPart[] so that
    // VSCode truncates individual parts rather than the whole string,
    // and each part gets a tooltip showing its full untruncated type.
    const hints: vscode.InlayHint[] = [];
    for (const m of matches) {
      const types = this.typeCache.get(m.stepTypeName);
      if (types) {
        const fullLabel = `${types.tIn} → ${types.tOut}`;

        const tInPart = new vscode.InlayHintLabelPart(types.tIn);
        tInPart.tooltip = new vscode.MarkdownString(`**TIn:** \`${types.tIn}\``);

        const arrowPart = new vscode.InlayHintLabelPart(" → ");

        const tOutPart = new vscode.InlayHintLabelPart(types.tOut);
        tOutPart.tooltip = new vscode.MarkdownString(`**TOut:** \`${types.tOut}\``);

        const hint = new vscode.InlayHint(
          m.hintPosition,
          [tInPart, arrowPart, tOutPart],
          vscode.InlayHintKind.Type
        );
        hint.paddingLeft = true;
        hint.tooltip = new vscode.MarkdownString(
          `**${m.stepTypeName}**\n\n\`${fullLabel}\``
        );
        hints.push(hint);
      }
    }

    // If some types couldn't be resolved (language server not ready),
    // schedule a retry so hints appear once the server catches up
    const anyUnresolved = matches.some(
      (m) =>
        !this.typeCache.has(m.stepTypeName) &&
        !this.unresolvable.has(m.stepTypeName)
    );
    if (anyUnresolved) {
      this.scheduleRefresh();
    }

    return hints;
  }

  clearCache(): void {
    this.typeCache.clear();
    this.unresolvable.clear();
    this._onDidChangeInlayHints.fire();
  }

  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  private scheduleRefresh(): void {
    if (this.refreshTimer) return;
    this.refreshTimer = setTimeout(() => {
      this.refreshTimer = undefined;
      this._onDidChangeInlayHints.fire();
    }, 5000);
  }

  private async resolveAndCache(
    documentUri: vscode.Uri,
    stepTypeName: string,
    position: vscode.Position
  ): Promise<void> {
    try {
      const definitions =
        await vscode.commands.executeCommand<
          (vscode.Location | vscode.LocationLink)[]
        >("vscode.executeDefinitionProvider", documentUri, position);

      if (!definitions || definitions.length === 0) {
        return; // Language server not ready yet — will retry
      }

      const def = definitions[0];
      const defUri = "targetUri" in def ? def.targetUri : def.uri;

      const defDoc = await vscode.workspace.openTextDocument(defUri);
      const defText = defDoc.getText();
      const result = extractStepTypes(defText, stepTypeName);

      if (result) {
        this.typeCache.set(stepTypeName, result);
      } else {
        this.unresolvable.add(stepTypeName);
      }
    } catch {
      // Don't mark as unresolvable — could be a transient error
    }
  }
}
