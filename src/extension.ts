import * as vscode from 'vscode';
import * as path from 'path';
import * as os from 'os';

let isWSL: boolean
let isWindows: boolean
let buildTerminal: vscode.Terminal | undefined;

export function activate(context: vscode.ExtensionContext) {
    const buildCommand = vscode.commands.registerCommand('extension.build', async (uri: vscode.Uri) => {
        if (uri) {
            const config = vscode.workspace.getConfiguration('fastbuild');
            const useCompatibilityMode = config.get('useCompatibilityMode', false);

            let dotnetDllPath = path.join(__dirname, '..', 'publish', 'FastBuild.dll');

            // Check if the terminal exists and is still open if not, create a new one
            if (!buildTerminal || buildTerminal.exitStatus) {
                isWindows = os.platform() === 'win32';
                if (isWindows) {
                    buildTerminal = vscode.window.terminals.find(terminal => terminal.name === 'wslFB');
                    if (buildTerminal) {
                        isWSL = true;
                        isWindows = false; // linux terminal
                    }
                }
                if (!buildTerminal || buildTerminal.exitStatus) {
                    buildTerminal = vscode.window.createTerminal({
                        name: "Fast Build",
                        hideFromUser: false
                    });
                    isWSL = false;
                }
            }

            function fixWSLPath(path: string): string {
                return !isWSL ? path : path
                    .replace(/\\/g, '/')
                    .replace(/^([a-zA-Z]):/, (match, driveLetter) => `/mnt/${driveLetter.toLowerCase()}`);
            }

            buildTerminal.show(true);
            buildTerminal.sendText(isWindows ? "cls" : "clear"); // Clear previous output (use "cls" on Windows)
            buildTerminal.sendText(isWindows ? `set FASTBUILD_COMPATIBILITY_MODE=${useCompatibilityMode}` : `export FASTBUILD_COMPATIBILITY_MODE=${useCompatibilityMode}`);
            buildTerminal.sendText(`dotnet exec "${fixWSLPath(dotnetDllPath)}" "${fixWSLPath(uri.fsPath)}"`);
        } else {
            vscode.window.showErrorMessage('No URI found for clicked item.');
        }
    });

    context.subscriptions.push(buildCommand);
}

export function deactivate() {}

