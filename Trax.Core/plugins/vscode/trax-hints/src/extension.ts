import * as vscode from "vscode";
import { ChainHintsProvider } from "./provider.js";

export function activate(context: vscode.ExtensionContext) {
  const provider = new ChainHintsProvider();

  const registration = vscode.languages.registerInlayHintsProvider(
    { language: "csharp" },
    provider
  );

  // Clear cached types when any C# file changes on disk
  const watcher = vscode.workspace.createFileSystemWatcher("**/*.cs");
  watcher.onDidChange(() => provider.clearCache());
  watcher.onDidCreate(() => provider.clearCache());
  watcher.onDidDelete(() => provider.clearCache());

  context.subscriptions.push(registration, watcher);
}

export function deactivate() {}
