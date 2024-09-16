import * as vscode from 'vscode';
import * as path from 'path';

let buildTerminal: vscode.Terminal | undefined;

export function activate(context: vscode.ExtensionContext) {
    const buildCommand = vscode.commands.registerCommand('extension.build', async (uri: vscode.Uri) => {
        if (uri) {
            const dotnetDllPath = path.join(__dirname, '..', 'publish', 'FastBuild.dll');

            // Check if the terminal exists and is still open if not, create a new one
            if (!buildTerminal || buildTerminal.exitStatus) {
                buildTerminal = vscode.window.createTerminal({
                    name: "Fast Build",
                    hideFromUser: false
                });
            }

            buildTerminal.show(true);
            buildTerminal.sendText("clear"); // Clear previous output (use "cls" on Windows)
            buildTerminal.sendText(`dotnet exec ${dotnetDllPath} "${uri.fsPath}"`);        
        } else {
            vscode.window.showErrorMessage('No URI found for clicked item.');
        }
    });

    context.subscriptions.push(buildCommand);
}

export function deactivate() {}

