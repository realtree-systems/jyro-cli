# Jyro CLI

Jyro CLI is the command-line interface for the Jyro scripting language. It is used to compile, execute, validate, and test Jyro scripts from the terminal, and to read the Jyro reference manual. Jyro is an imperative data manipulation language designed for secure, sandboxed processing of JSON-like data structures.

The tool is distributed for Windows, Linux, and macOS.

## Installation

The tool is self-contained: no .NET runtime needs to be installed. Every release publishes the packages below, and each `latest` link always serves the newest release.

| Platform | Package | Download |
|----------|---------|----------|
| Windows x64 | MSI installer | [jyro-win-x64.msi](https://github.com/realtree-systems/jyro-cli/releases/latest/download/jyro-win-x64.msi) |
| Windows x64 | Zip archive | [jyro-win-x64.zip](https://github.com/realtree-systems/jyro-cli/releases/latest/download/jyro-win-x64.zip) |
| Linux x64 | Debian package | [jyro-linux-x64.deb](https://github.com/realtree-systems/jyro-cli/releases/latest/download/jyro-linux-x64.deb) |
| Linux arm64 | Debian package | [jyro-linux-arm64.deb](https://github.com/realtree-systems/jyro-cli/releases/latest/download/jyro-linux-arm64.deb) |
| Linux x64 / arm64 | Tar archive | [jyro-linux-x64.tar.gz](https://github.com/realtree-systems/jyro-cli/releases/latest/download/jyro-linux-x64.tar.gz), [jyro-linux-arm64.tar.gz](https://github.com/realtree-systems/jyro-cli/releases/latest/download/jyro-linux-arm64.tar.gz) |
| macOS Apple Silicon / Intel | Homebrew, or tar archive | `brew install realtree-systems/tap/jyro`, or [jyro-macos-arm64.tar.gz](https://github.com/realtree-systems/jyro-cli/releases/latest/download/jyro-macos-arm64.tar.gz), [jyro-macos-x64.tar.gz](https://github.com/realtree-systems/jyro-cli/releases/latest/download/jyro-macos-x64.tar.gz) |

A `SHA256SUMS.txt` listing the checksum of every package accompanies each release.

- The **MSI installer** places `Jyro.exe` in `Program Files\RealTree\Jyro`, adds that directory to the system `PATH`, and associates `.jyro` and `.jyrx` files with the tool so that opening one runs it.
- The **Debian package** installs the tool under `/usr/lib/jyro` and provides the command `jyro` in `/usr/bin`. Install it with `sudo apt install ./jyro-linux-x64.deb`.
- **Homebrew** installs the command `jyro`.
- A **tar or zip archive** holds one executable, named `Jyro.exe` on Windows and `Jyro` elsewhere. Place it on the `PATH`; on Linux and macOS, rename it to `jyro` if a lowercase command is wanted, and mark it executable:

```bash
chmod +x jyro
```

Installation can be verified by running:

```
jyro --version
```

This prints the installed version, for example `jyro 1.0.0+3bab186867fac825a64c905a782504367cd4cff8`. The text after `+` is the commit the release was built from.

## Overview

Jyro CLI provides six subcommands:

| Command       | Alias | Purpose                                                       |
|---------------|-------|---------------------------------------------------------------|
| `compile`     | `c`   | Compile a `.jyro` source file to a `.jyrx` precompiled binary |
| `run`         | `r`   | Execute a `.jyro` script or `.jyrx` binary                    |
| `validate`    | `v`   | Verify that a `.jyrx` binary matches a given `.jyro` source   |
| `test`        | `t`   | Execute a script and compare its output to expected JSON      |
| `man`         |       | Browse the Jyro reference manual in the terminal              |
| `init-config` |       | Write a default `jyro.config.json` template                   |

Each subcommand accepts its own set of options, listed by `jyro <subcommand> --help`. The short alias may be used interchangeably with the full command name (for example, `jyro c` is equivalent to `jyro compile`). `jyro --help` lists the subcommands and `jyro --version` prints the version.

## Script File Formats

Two script file formats are supported:

- **`.jyro`** -- Plain-text UTF-8 source files containing Jyro script code.
- **`.jyrx`** -- Precompiled binary files produced by the `compile` command. A `.jyrx` file embeds a SHA-256 hash of the original source, which is used by the `validate` command for integrity verification.

All data input and output files are standard JSON.

## Input and Output

Jyro scripts read host data through the read-only identifier `Input` and write their results to the `Output` object. `Output` starts as an empty object at the beginning of execution and is returned to the host after the script completes. Scripts may add properties to `Output`, or reassign it to any value (e.g. `Output = [1, 2, 3]`) to return a non-object shape.

The CLI populates `Input` from the `-i` and `-p` switches as follows:

| `-i` provided | `-p` provided | Resulting `Input` |
|---------------|---------------|-------------------|
| no            | no            | `null` |
| yes           | no            | The contents of the `-i` file, exactly as parsed (object, array, scalar, etc.) |
| no            | yes           | An object with a single `_parameters` property |
| yes           | yes           | The contents of the `-i` file with `_parameters` attached as a sibling property (the input file must contain a JSON object) |

Example script using all three identifiers:

```
# Read top-level fields from the input file
var items = Input.items

# Read a value from --parameters
var threshold = Input._parameters.threshold

# Populate the output (any shape - independent of Input)
Output.matched = WhereByField(items, "score", ">", threshold)
Output.matchCount = Length(Output.matched)
```

If `-i` and `-p` are both supplied but the input file does not contain a JSON object, the CLI exits with an error before invoking the script, since there is nowhere to attach `_parameters`.

---

## `compile`

```
jyro compile -s <script.jyro> [-o <output.jyrx>] [plugin options] [logging options]
```

The `compile` command is used to transform a `.jyro` source file into a `.jyrx` precompiled binary. Precompilation allows the parsing, validation, and linking stages of the pipeline to be performed ahead of time so that subsequent execution is faster.

The standard library and the `WriteLog` host function are always available during compilation. If a script references functions provided by external plugin assemblies, those plugins must be supplied at compile time using the plugin options described below (see [Plugin Loading](#plugin-loading) for details).

### Options

| Option                 | Alias | Required | Description                                                                  |
|------------------------|-------|----------|------------------------------------------------------------------------------|
| `--input-script-file`  | `-s`  | Yes      | Path to the `.jyro` source file to be compiled.                              |
| `--output-file`        | `-o`  | No       | Path for the output `.jyrx` file. Defaults to the input filename with a `.jyrx` extension. |
| `--plugin`             |       | No       | Comma-separated paths to individual plugin assembly DLL files.               |
| `--plugin-directory`   | `-pd` | No       | Comma-separated directories to search for plugin DLLs (top-level only).      |
| `--plugin-recursive`   | `-pr` | No       | Comma-separated directories to search recursively for plugin DLLs.           |
| `--log-file`           | `-l`  | No       | Path to a file for log output. Logging is written to the console by default. |
| `--verbosity`          | `-v`  | No       | Minimum log level. Accepted values: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`. Defaults to `Information`. |
| `--quiet`              | `-q`  | No       | Disables all logging output.                                                 |
| `--console-logging`    |       | No       | Enables or disables console logging to stderr (`true` or `false`). Defaults to `false`. |
| `--log-format`         |       | No       | Log output format (`text` or `json`). Defaults to `text`.                    |

### Usage

A script is compiled as follows:

```bash
jyro compile -s transform.jyro
```

This will produce `transform.jyrx` in the same directory. An explicit output path may be specified:

```bash
jyro compile -s transform.jyro -o build/transform.jyrx
```

If the script uses functions from an external plugin, the plugin must be provided:

```bash
jyro compile -s transform.jyro --plugin MyPlugin.dll
jyro compile -s transform.jyro -pr C:\Plugins
```

On success, a confirmation message is printed indicating the input file, output file, and the size of the compiled binary in bytes:

```
Compiled transform.jyro -> build/transform.jyrx (1284 bytes)
```

If compilation fails, one or more diagnostic messages are printed to stderr. Each diagnostic includes an error code, source location (line and column), and a description of the problem. The codes are documented in the manual under `jyro man error-codes`.

```
[JM3100] Ln 5, Col 10: Undeclared variable 'x'
```

### Exit Codes

- `0` -- Compilation succeeded.
- `1` -- The input file was not found, a plugin could not be loaded, or one or more compilation errors were encountered.

---

## `run`

```
jyro run -s <script.jyro|jyrx> [-i <data.json>] [-p <params>] [-o <output.json>] [options]
jyro run --stdin [-i <data.json>] [-p <params>] [-o <output.json>] [options]
```

The `run` command executes a Jyro script or precompiled binary. The tool automatically detects the file type based on its extension: `.jyrx` files are loaded as precompiled binaries, and all other files are treated as source code. Script source can also be piped via stdin using the `--stdin` flag.

### Options

| Option                 | Alias | Required | Description                                                                                  |
|------------------------|-------|----------|----------------------------------------------------------------------------------------------|
| `--input-script-file`  | `-s`  | *        | Path to the `.jyro` source file or `.jyrx` binary to execute.                                |
| `--stdin`              |       | *        | Read Jyro script source from stdin. Cannot be used with `-s`. Only supports source scripts, not `.jyrx` binaries. |
| `--input-json-file`    | `-i`  | No       | Path to a JSON file whose contents are passed to the script as `Input`.                      |
| `--parameters`         | `-p`  | No       | JSON object or file path attached to `Input._parameters`. Inline JSON must be wrapped in `{}`. Values not starting with `{` are treated as file paths. See [Input and Output](#input-and-output) for the merge semantics when both `-i` and `-p` are supplied. |
| `--output-json-file`   | `-o`  | No       | Path to write the JSON output. If omitted, output is written to stdout.                      |
| `--log-file`           | `-l`  | No       | Path to a file for log output. Logging is written to the console by default.                 |
| `--verbosity`          | `-v`  | No       | Minimum log level. Accepted values: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`. Defaults to `Information`. |
| `--quiet`              | `-q`  | No       | Disables all logging output.                                                                 |
| `--console-logging`    |       | No       | Enables or disables console logging to stderr (`true` or `false`). Defaults to `false`.                |
| `--config`             | `-c`  | No       | Path to a JSON configuration file. If not specified, default locations are searched (see [Configuration](#configuration)). |
| `--stats`              |       | No       | Displays per-stage pipeline timing statistics on stderr after execution (see [Pipeline Statistics](#pipeline-statistics)). |
| `--log-format`         |       | No       | Log output format (`text` or `json`). Defaults to `text`.                                    |
| `--plugin`             |       | No       | Comma-separated paths to individual plugin assembly DLL files.                               |
| `--plugin-directory`   | `-pd` | No       | Comma-separated directories to search for plugin DLLs (top-level only).                      |
| `--plugin-recursive`   | `-pr` | No       | Comma-separated directories to search recursively for plugin DLLs.                           |

\* Either `--input-script-file` (`-s`) or `--stdin` is required. They are mutually exclusive.

### Usage

The simplest invocation executes a script with no input data and prints the result to stdout:

```bash
jyro run -s transform.jyro
```

Input data can be supplied from a JSON file. The contents of this file become the script's `Input`:

```bash
jyro run -s transform.jyro -i input.json
```

Parameters can be provided as inline JSON or from a file. They are attached to the script's `Input._parameters`:

```bash
jyro run -s transform.jyro -i input.json -p '{"threshold":10}'
jyro run -s transform.jyro -i input.json -p params.json
```

To write the output to a file rather than stdout:

```bash
jyro run -s transform.jyro -i input.json -o result.json
```

A precompiled binary can be executed in exactly the same way:

```bash
jyro run -s transform.jyrx -i input.json -o result.json
```

#### Reading from stdin

Instead of passing a file path, script source can be piped directly using `--stdin`. This is useful when the script is generated dynamically or held in a shell variable:

```bash
echo 'Output.x = 1' | jyro run --stdin
```

```powershell
$script = Get-Content transform.jyro -Raw
$script | jyro run --stdin -i input.json -o result.json
```

Stdin mode only supports `.jyro` source scripts. Precompiled `.jyrx` binaries must be provided via `-s`.

Logging is off on the console by default. It can be written to a file, shown on stderr, or suppressed entirely:

```bash
jyro run -s transform.jyro -v Debug -l run.log
jyro run -s transform.jyro --console-logging true
jyro run -s transform.jyro -q
```

If the script uses functions from external plugins, the plugin assemblies or directories must be provided:

```bash
jyro run -s transform.jyro --plugin MyPlugin.dll
jyro run -s transform.jyro -pr C:\Plugins,D:\MorePlugins
```

All output JSON is pretty-printed with two-space indentation.

### Exit Codes

- `0` -- Execution succeeded and output was produced.
- `1` -- A required option was missing, a file was not found, or compilation or execution failed.

When execution fails part-way, the diagnostics are printed to stderr and the value `Output` held at that point is still written, so the partial result can be inspected; the exit code is `1` regardless.

---

## `validate`

```
jyro validate --compiled <file.jyrx> --raw <file.jyro> [logging options]
```

The `validate` command is used to verify the integrity of a `.jyrx` precompiled binary against its original `.jyro` source file. During compilation, a SHA-256 hash of the source code is embedded in the `.jyrx` binary header. The `validate` command recomputes this hash from the supplied source file and compares it to the stored value.

This is useful in deployment pipelines and review workflows where it must be confirmed that a binary was produced from a specific, unmodified source file.

### Options

| Option              | Alias | Required | Description                                  |
|---------------------|-------|----------|----------------------------------------------|
| `--compiled`        |       | Yes      | Path to the `.jyrx` precompiled binary file. |
| `--raw`             |       | Yes      | Path to the original `.jyro` source file.    |
| `--log-file`        | `-l`  | No       | Path to a file for log output. Logging is written to the console by default. |
| `--verbosity`       | `-v`  | No       | Minimum log level. Accepted values: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`. Defaults to `Information`. |
| `--quiet`           | `-q`  | No       | Disables all logging output.                 |
| `--console-logging` |       | No       | Enables or disables console logging to stderr (`true` or `false`). Defaults to `false`. |
| `--log-format`      |       | No       | Log output format (`text` or `json`). Defaults to `text`. |

### Usage

```bash
jyro validate --compiled transform.jyrx --raw transform.jyro
```

If the hashes match, the following is printed in green:

```
[PASS]: transform.jyrx is a valid representation of transform.jyro
```

If the hashes do not match, a failure message is printed in red along with both hash values for diagnostic purposes:

```
[FAIL]: transform.jyrx does NOT match transform.jyro
  Source hash:   A1B2C3...
  Compiled hash: D4E5F6...
```

### Exit Codes

- `0` -- The binary is a valid compilation of the source file.
- `1` -- A file was not found, the binary header could not be read, or the hashes did not match.

---

## `test`

```
jyro test -s <script.jyro|jyrx> [-i <data.json>] [-p <params>] -o <expected.json> [--stats] [plugin options] [logging options]
```

The `test` command executes a Jyro script and compares its output against an expected JSON file. It is intended for use in automated testing and continuous integration pipelines.

### Options

| Option                 | Alias | Required | Description                                                                                  |
|------------------------|-------|----------|----------------------------------------------------------------------------------------------|
| `--input-script-file`  | `-s`  | Yes      | Path to the `.jyro` source file or `.jyrx` binary to execute.                                |
| `--input-json-file`    | `-i`  | No       | Path to a JSON file whose contents are passed to the script as `Input`.                      |
| `--parameters`         | `-p`  | No       | JSON object or file path attached to `Input._parameters`. Inline JSON must be wrapped in `{}`. Values not starting with `{` are treated as file paths. See [Input and Output](#input-and-output) for the merge semantics when both `-i` and `-p` are supplied. |
| `--output-json-file`   | `-o`  | Yes      | Path to the expected output JSON file to compare against.                                    |
| `--stats`              |       | No       | Displays per-stage pipeline timing statistics on stderr.                                     |
| `--plugin`             |       | No       | Comma-separated paths to individual plugin assembly DLL files.                               |
| `--plugin-directory`   | `-pd` | No       | Comma-separated directories to search for plugin DLLs (top-level only).                      |
| `--plugin-recursive`   | `-pr` | No       | Comma-separated directories to search recursively for plugin DLLs.                           |
| `--log-file`           | `-l`  | No       | Path to a file for log output. Logging is written to the console by default.                 |
| `--verbosity`          | `-v`  | No       | Minimum log level. Accepted values: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`. Defaults to `Information`. |
| `--quiet`              | `-q`  | No       | Disables all logging output.                                                                 |
| `--console-logging`    |       | No       | Enables or disables console logging to stderr (`true` or `false`). Defaults to `false`.                |
| `--log-format`         |       | No       | Log output format (`text` or `json`). Defaults to `text`.                                    |

### Usage

```bash
jyro test -s transform.jyro -i input.json -o expected-output.json
```

If the actual output matches the expected output, a pass message is printed in green:

```
[PASS]: transform.jyro correctly produced output identical to expected-output.json
```

If differences are found, each mismatch is listed with its JSON path, expected value, and actual value:

```
[FAIL]: transform.jyro produced output that differs from expected-output.json
  3 difference(s) found:
    $.name: expected Alice, got Bob
    $.scores[2]: expected 95, got 87
    $.metadata.version: expected 2.0, got (missing)
```

### Comparison Semantics

The comparison is performed semantically rather than as a textual diff. Object property order is not significant; a property present on only one side is reported as `(missing)` on the other. Numeric values are compared with a relative tolerance of 1e-10, so values such as `0.1 + 0.2` and `0.3` are treated as equal. Integer and floating-point representations of the same value (e.g., `42` and `42.0`) are also treated as equal.

Array elements are compared positionally. If the arrays differ in length, a mismatch on the `.length` path is reported in addition to any element-level differences.

### Exit Codes

- `0` -- The script executed successfully and the output matched the expected file.
- `1` -- A file was not found, execution failed, or the output differed from the expected file.

### Use in CI

Because the `test` command returns a non-zero exit code on failure, it integrates naturally with CI systems. A typical test script might look like:

```bash
jyro test -s tests/transform.jyro -i tests/input.json -o tests/expected.json
jyro test -s tests/aggregate.jyro -i tests/data.json -o tests/aggregate-expected.json
```

Each line will halt a CI pipeline (under `set -e` or equivalent) if the test fails.

---

## `man`

```
jyro man [<topic> ...]
```

The `man` command shows the Jyro reference manual, which is embedded in the tool: the language, the standard library, the host API, and this command-line tool. Without a topic it shows the table of contents; with one it opens the matching page, subsection, or section. A topic of several words may be given as separate arguments or quoted, and the lookup ignores case.

```bash
jyro man
jyro man foreach
jyro man types and values
jyro man standard-library
```

Where several pages share a title - most sections have an *Overview* - the command lists them, each with a path that names it alone. A path is the page's title preceded by enough of its section and subsection to make it unique, and the manual's website addresses are accepted as well:

```bash
jyro man string/overview
jyro man /docs/standard-library/string/overview
```

When standard output is a terminal, the manual opens in a browser inside the terminal: arrow keys scroll, Tab moves between links, Enter follows one, Backspace goes back, and `q` quits. When standard output is redirected, the page is written as plain text, so pages can be piped or saved. The full description of the command is in the manual itself, under `jyro man command-line/man`.

### Exit Codes

- `0` -- The page was shown.
- `1` -- No page matched, or several did.

---

## `init-config`

```
jyro init-config [-o <path>] [--global] [-f]
```

The `init-config` command writes a `jyro.config.json` template holding every setting the `run` command reads, at its default value, so a configuration file can be made by editing it rather than written from memory. See [Configuration](#configuration) for what the settings mean and where the `run` command looks for the file.

### Options

| Option     | Alias | Description                                                                                   |
|------------|-------|-----------------------------------------------------------------------------------------------|
| `--output` | `-o`  | Path to write. Defaults to `./jyro.config.json`. Overrides `--global`.                         |
| `--global` |       | Write to the per-user location: `%APPDATA%\Jyro\jyro.config.json` on Windows, `~/Library/Application Support/Jyro/jyro.config.json` on macOS, `~/.config/Jyro/jyro.config.json` on Linux. |
| `--force`  | `-f`  | Overwrite the file if it already exists. Without this, an existing file is left alone and the command exits with `1`. |

### Usage

```bash
jyro init-config
jyro init-config --global
jyro init-config -o config/jyro.config.json -f
```

---

## Configuration

The `run` command supports a layered configuration system. Options may be specified through three sources, listed here from lowest to highest priority:

1. **Configuration file** -- A JSON file named `jyro.config.json`.
2. **Environment variables** -- Prefixed with `JYRO_`.
3. **Command-line arguments** -- Passed directly on the command line.

When the same option is set in multiple sources, the highest-priority source wins. For example, a `--verbosity` flag on the command line will override a `logLevel` value in the configuration file.

The other subcommands read neither the configuration file nor the environment variables.

### Configuration File

If no `--config` path is given explicitly, the following locations are searched in order:

1. The current working directory: `./jyro.config.json`
2. The application data directory: see platform-specific paths below.
3. The user home directory: `~/jyro.config.json`

The first file found is used. If no file is found in any location, default values are applied.

A configuration file has the following structure, which is what `jyro init-config` writes:

```json
{
  "inputScriptFile": "",
  "inputJsonFile": "",
  "parametersSource": "",
  "outputJsonFile": "",
  "logFile": "",
  "logLevel": "Information",
  "noLogging": false,
  "consoleLogging": false,
  "logFormat": "text",
  "stats": false,
  "pluginAssemblies": "",
  "pluginDirectories": "",
  "pluginRecursiveDirectories": ""
}
```

All fields are optional, and key names are matched without regard to case. Only the fields that differ from the defaults need to be included; an empty string is the same as leaving a field out.

#### Platform-Specific Application Data Paths

The application data directory varies by platform:

| Platform | Path                                              |
|----------|---------------------------------------------------|
| Windows  | `%APPDATA%\Jyro\jyro.config.json`                |
| Linux    | `$XDG_CONFIG_HOME/Jyro/jyro.config.json` (typically `~/.config/Jyro/jyro.config.json`) |
| macOS    | `~/Library/Application Support/Jyro/jyro.config.json` |

These paths are resolved through the .NET `Environment.SpecialFolder.ApplicationData` API, which maps to the appropriate location on each operating system. `jyro init-config --global` writes to this location.

### Environment Variables

Environment variables are prefixed with `JYRO_` and use the same key names as the configuration file. For example:

```bash
export JYRO_InputScriptFile=transform.jyro
export JYRO_InputJsonFile=data.json
export JYRO_ParametersSource=params.json
export JYRO_LogLevel=Debug
export JYRO_LogFormat=json
export JYRO_PluginRecursiveDirectories=/opt/jyro-plugins
```

On Windows, the equivalent is:

```cmd
set JYRO_InputScriptFile=transform.jyro
set JYRO_InputJsonFile=data.json
set JYRO_ParametersSource=params.json
set JYRO_LogLevel=Debug
set JYRO_LogFormat=json
set JYRO_PluginRecursiveDirectories=C:\Plugins
```

---

## Logging

The `compile`, `run`, `validate`, and `test` commands write structured log messages, each with a timestamp, level, and category. The console and the log file use slightly different layouts; the file's is shown here and the console's under [Log formats](#log-formats).

```
2026-02-14 10:30:00 [Information] Jyro.Cli.Compile: Compiling transform.jyro
```

### Options

| Option              | Alias | Default         | Effect                                                                 |
|---------------------|-------|-----------------|------------------------------------------------------------------------|
| `--verbosity`       | `-v`  | `Information`   | Sets the minimum log level. Messages below this level are discarded.   |
| `--quiet`           | `-q`  | `false`         | Disables all logging output (console and file). Overrides other options.|
| `--console-logging` |       | `false`         | Enables or disables the console log provider, which writes to stderr.  |
| `--log-file`        | `-l`  |                 | Appends log output to the specified file. The directory is created automatically if it does not exist. |
| `--log-format`      |       | `text`          | Selects the log output format: `text` for human-readable lines, `json` for structured JSON. |

The accepted `--verbosity` values, from most to least verbose, are: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`.

### Logging behavior by subcommand

| Subcommand  | Script `WriteLog()` supported | Config file / env vars |
|-------------|-------------------------------|------------------------|
| `compile`   | No (scripts are not executed) | No                     |
| `run`       | Yes                           | Yes                    |
| `validate`  | No (scripts are not executed) | No                     |
| `test`      | Yes                           | No                     |

Console logging is off by default, so stdout carries only a command's own output: the JSON result of `run`, the PASS/FAIL results of `test`. When it is turned on with `--console-logging true`, every subcommand writes its log to stderr, so it can be redirected independently:

```bash
jyro run -s transform.jyro --console-logging true 2>run.log | jq .
```

A script's `WriteLog()` messages go through the same logging, so they appear only when console logging or a log file is enabled.

The `run` command's logging options can also be set via configuration file and environment variables (see [Configuration](#configuration)). The other subcommands accept logging options from the command line only.

### Output routing summary

The following table shows where log output ends up based on the options provided.

| `--quiet` | `--console-logging` | `--log-file` | Console output | File output |
|-----------|---------------------|--------------|----------------|-------------|
|           |                     |              | No             | No          |
|           |                     | `log.txt`    | No             | Yes         |
|           | `true`              |              | Yes (stderr)   | No          |
|           | `true`              | `log.txt`    | Yes (stderr)   | Yes         |
| `-q`      |                     |              | No             | No          |
| `-q`      | `true`              | `log.txt`    | No             | No          |

When `--quiet` is set, all logging is suppressed regardless of other options. Without `--console-logging true`, only file logging is active (if a log file is specified).

### Log formats

The default `text` format produces one human-readable line per message. On the console the line is laid out by the .NET console logger, with the level abbreviated:

```
2026-02-14 10:30:00 info: Jyro.Cli.Compile[0] Compiling transform.jyro
```

In a log file the level is spelled out:

```
2026-02-14 10:30:00 [Information] Jyro.Cli.Compile: Compiling transform.jyro
```

The `json` format produces one JSON object per line ([JSON Lines](https://jsonlines.org/) format), suitable for machine parsing and ingestion by Jyro scripts or CI/CD tooling. A log file holds the timestamp, level, category, message, and exception, if any:

```json
{"Timestamp":"2026-02-14T10:30:00.123+00:00","LogLevel":"Information","Category":"Jyro.Cli.Compile","Message":"Compiling transform.jyro","Exception":null}
```

The console's JSON objects carry the same fields, plus an `EventId` and a `State` object holding the message's named placeholders.

### Logging from Scripts

The `run` and `test` commands register a `WriteLog` host function that lets Jyro scripts write to the logging pipeline. It accepts a level string and a message:

```
WriteLog("info", "Processing started")
WriteLog("debug", "Record count: 42")
WriteLog("error", "Unexpected null in field 'name'")
```

The accepted level values are: `trace`, `debug`, `info` (or `information`), `warn` (or `warning`), `error`, and `critical`. An invalid level ends the run with a `JM5006` host function error naming the value given. Messages from scripts are prefixed with `[Script]` in the log output to distinguish them from runtime messages.

Neither `compile` nor `validate` executes a script, so neither produces `WriteLog` output. `WriteLog` calls are still resolved and type-checked during compilation.

---

## Pipeline Statistics

When the `--stats` flag is passed to the `run` or `test` commands, per-stage timing information is printed to stderr after execution completes. This is useful for identifying performance bottlenecks.

For a `.jyro` source file, the full pipeline is displayed:

```
Pipeline Statistics:
  ----------------------------
  Parse:           1.23 ms
  Validate:        0.45 ms
  Link:            0.12 ms
  Compile:         0.89 ms
  Execute:         2.34 ms
  ----------------------------
  Total:           5.03 ms
```

For a `.jyrx` precompiled binary, the Parse stage is replaced by Deserialize. The binary is still validated and linked against the functions the running host provides:

```
Pipeline Statistics:
  ----------------------------
  Deserialize:     0.31 ms
  Validate:        0.40 ms
  Link:            0.15 ms
  Compile:         0.67 ms
  Execute:         2.18 ms
  ----------------------------
  Total:           3.71 ms
```

Statistics are written to stderr so that they do not interfere with JSON output on stdout. This means they can be viewed even when output is piped to another command or redirected to a file.

---

## Plugin Loading

Jyro CLI supports loading custom functions from external .NET assemblies at both compile time and run time. This allows third-party or in-house libraries to extend the Jyro language with additional functions beyond the built-in standard library.

All function references in a Jyro script must be resolvable at compile time. If a script calls a function that is not part of the standard library, the plugin assembly containing that function must be provided via the plugin options. This applies to the `compile`, `run`, and `test` commands alike.

### Plugin Options

Three options are available on the `compile`, `run`, and `test` commands. All accept comma-separated values for specifying multiple paths.

| Option               | Alias | Description                                                    |
|----------------------|-------|----------------------------------------------------------------|
| `--plugin`           |       | Paths to individual plugin assembly DLL files.                 |
| `--plugin-directory`  | `-pd` | Directories to search for `*.dll` plugin files (top-level only). |
| `--plugin-recursive` | `-pr` | Directories to search recursively for `*.dll` plugin files.    |

### Examples

Load a single plugin assembly:

```bash
jyro compile -s script.jyro --plugin MyPlugin.dll
jyro run -s script.jyro --plugin MyPlugin.dll
```

Load multiple plugin assemblies:

```bash
jyro run -s script.jyro --plugin Plugin1.dll,Plugin2.dll
```

Search a directory for plugins:

```bash
jyro compile -s script.jyro -pd C:\Plugins
```

Recursively search multiple directories:

```bash
jyro run -s script.jyro -pr C:\Plugins,D:\MorePlugins
```

Options can be combined freely:

```bash
jyro run -s script.jyro --plugin SpecialPlugin.dll -pr C:\Plugins
```

### Configuration File

Plugin paths can also be specified in `jyro.config.json`:

```json
{
  "pluginAssemblies": "Plugin1.dll,Plugin2.dll",
  "pluginDirectories": "C:\\Plugins",
  "pluginRecursiveDirectories": "C:\\Plugins\\Extensions"
}
```

Command-line arguments override configuration file values, following the standard [configuration precedence](#configuration).

### Creating Plugins

To create a plugin, build a .NET class library that references the [`RealTree.Jyro`](https://www.nuget.org/packages/RealTree.Jyro) NuGet package and implement one or more functions by inheriting from `JyroFunctionBase`. A function declares its name, its signature, its effect, and what it does:

```csharp
using System.Collections.Generic;
using RealTree.Jyro;

public class MyCustomFunction : JyroFunctionBase
{
    public MyCustomFunction()
        : base("MyFunction",
               JyroFunctionSignature.Create(ParameterType.String,
                   Parameter.Required("value", ParameterType.String)))
    {
    }

    // Pure: the result depends on the arguments alone. NonDeterministic,
    // Blocking and Mutating declare stronger effects.
    public override FunctionEffect Effect => FunctionEffect.Pure;

    public override JyroValue Execute(
        IReadOnlyList<JyroValue> arguments,
        JyroExecutionContext context)
    {
        var input = arguments.GetString(0);
        return new JyroString($"Processed: {input}");
    }
}
```

The argument readers (`GetString`, `GetNumber`, `GetInt32`, `GetArray`, `GetObject`, and so on) are extension methods on the argument list; each checks the argument's type and raises the matching diagnostic when it is wrong. A function reports a failure of its own by throwing `JyroError.Host("ReasonName", values...)`. The host API section of the manual (`jyro man host-functions`) describes the full contract.

Compile the class library to a DLL and provide it to Jyro CLI using the plugin options.

**Requirements:**

- Plugin functions must have public parameterless constructors.
- Plugin assemblies must reference the `RealTree.Jyro` package and target .NET 8.0 or later.
- All function types must implement `IJyroFunction` (typically by inheriting from `JyroFunctionBase`).
- A function's name must not be that of a standard library function; the compilation reports the collision.

### Discovery Behavior

When loading from a directory (`-pd` or `-pr`), the CLI scans all `*.dll` files and discovers any concrete, non-abstract classes that implement `IJyroFunction`. The following are silently skipped:

- Non-.NET assemblies (native DLLs).
- Assemblies that cannot be loaded due to missing dependencies.
- Types that cannot be instantiated (for example, those without a parameterless constructor).

When loading an individual assembly (`--plugin`), a missing file or a type that cannot be instantiated is reported immediately as a failure.

---

## Exit Codes

All subcommands follow a consistent convention:

| Code | Meaning                                                                 |
|------|-------------------------------------------------------------------------|
| `0`  | The operation completed successfully.                                   |
| `1`  | An error occurred: the command line was invalid, a file was not found, compilation or execution failed, or a comparison did not match. |

Error messages are printed in red to stderr. Diagnostic messages from the compiler include an error code, the source location (line and column), and a human-readable description.

---

## Platform Notes

### Windows

The executable is named `Jyro.exe`; file names are not case-sensitive, so `jyro` runs it. The MSI installer adds it to the system `PATH` and associates `.jyro` and `.jyrx` files with it. Path separators may use either forward slashes or backslashes. The application data directory for configuration files is `%APPDATA%\Jyro\`.

### Linux

The Debian package provides the command `jyro`. The executable in the tar archive is named `Jyro` and must be marked as executable with `chmod +x Jyro` after download; file names are case-sensitive, so rename it to `jyro` for a lowercase command. The application data directory is typically `~/.config/Jyro/`, governed by `$XDG_CONFIG_HOME`.

### macOS

Homebrew provides the command `jyro`. The executable in the tar archive is named `Jyro` and must be marked as executable with `chmod +x Jyro` after download. On recent versions of macOS, Gatekeeper may block a binary downloaded from the internet with a browser. If this occurs, the quarantine attribute can be removed:

```bash
xattr -d com.apple.quarantine Jyro
```

Homebrew installs are not affected. The application data directory is `~/Library/Application Support/Jyro/`.

---

## Building from Source

Building requires the .NET 10 SDK and two sibling repositories, cloned beside this one so that the paths `../jyro-lang` and `../jyro-doc` resolve:

- [`jyro-lang`](https://github.com/realtree-systems/jyro-lang), the language, which the CLI references as projects.
- [`jyro-doc`](https://github.com/realtree-systems/jyro-doc), the reference manual, whose `manual/` directory is embedded as the pages of `jyro man`. Without it the build warns and the tool has no manual pages.

```bash
dotnet build
dotnet test
```

Every merge to `main` runs the release workflow in `.github/workflows/release.yml`, which builds and tests the tool, packages it for every platform, publishes a GitHub release, and updates the Homebrew tap.

---

## License

Jyro CLI is released under the MIT License. See [LICENSE](LICENSE) for details.
