# Fast Build VS Code extension

This extension allows you to do partial builds of your project.

## Features

Use context menu in the explorer, in file tab or file editor to build your file or directory.

The option to execute is called `Fast Build: Build & Publish`

Note: It will open a Terminal to execute, if you want to use WSL in Windows open a terminal and rename it to `wslFB` before.

## Installation

Download the VSIX and install it in Visual Studio Code manually with option `Extensions: Install from VSIX...` from Command Palette (Ctrl+Shift+P)

## How does it work

It has 2 modes

- **CMake**: It index all the `CMakeLists.txt` files once or when one changes.  When a file is built it will check if it is on one of those and builds the necessary CMake projects. Then it will copy the final libraries required to the publish directory.

- **Csproj**: When a file is built it will check for a `.csproj` in the file or parents directory, and build that. Then it will publish the required packages to the publish directory.

Notes:
- if a new CMakeLists.txt it will reindex automatically since checking every times is costly, you have to manually delete `.fastbuild/_Tmp/CMakeCache.json` to force it.

- when build a csproj it will create `__.fastbuild.csproj`, they can be ignored or deleted after it was built. They are not deleted automatically since they can be reused on next build.

- the publish directory is in the configuration or a file inside `.fastbuild` directory.

## Configuration

In the base of your source code directory it should be a directory called `.fastbuild` with configuration file `config.json`.

``` json
{
    "requires": "x.x.x", // version of the extension that this config is targeted for
    "cmake": {
        "build": {
            "BaseDirectory": "", // all CMakeLists.txt inside of this directory or descendents will be considerer for indexing, to know where a file belongs and what are the dependencies
            "Ignore": [ ], // list of cmake to ignore, it can partially path the directory or library name
            "Command": "" // command to build one cmake project, {{CMAKE_DIRECTORY}} is the CMakeLists.txt subdirectory inside BaseDirectory, and {{PROJECT_NAME}} is the name of the library built"
        },
        "publish": {
            "Files": [ ], // list of files in .fastbuild needed to execute the command
            "Command": "" // command executed to publish, {{ROOT_DIRECTORY}} is the directory where .fastbuild is, {{RUNTIME_IDENTIFIER}} for corresponding one of the running machine, {{PROJECT_NAME}} is the name for library
        }
    },
    "csproj": {
        "check": { // optional, it executes before build to check if it is valid to build, i.e. it can checks if a full build is required
            "Files": [ ], // list of files in .fastbuild needed to execute the command
            "Command": "" // command executed to publish, {{ROOT_DIRECTORY}} is the directory where .fastbuild is, {{RUNTIME_IDENTIFIER}} for corresponding one of the running machine
        }
        "publish": {
            "Files": [ ], // list of files in .fastbuild needed to execute the command, if there is a file with .template on its name {{PACKAGE}} will be replaced with the name of the assembly to publish
            "Command": "" // command executed to publish, {{ROOT_DIRECTORY}} is the directory where .fastbuild is, {{RUNTIME_IDENTIFIER}} for corresponding one of the running machine
        }
    }
}
```
