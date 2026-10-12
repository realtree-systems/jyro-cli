namespace RealTree.Jyro.Cli

open Argu
open Microsoft.Extensions.Logging

/// Compile subcommand arguments.
type CompileArgs =
    | [<AltCommandLine("-s"); Mandatory>] Input_Script_File of path: string
    | [<AltCommandLine("-o")>] Output_File of path: string
    | Plugin of assemblies: string
    | [<AltCommandLine("-pd")>] Plugin_Directory of dirs: string
    | [<AltCommandLine("-pr")>] Plugin_Recursive of dirs: string
    | [<AltCommandLine("-l")>] Log_File of path: string
    | [<AltCommandLine("-v")>] Verbosity of LogLevel
    | [<AltCommandLine("-q")>] Quiet
    | Console_Logging of bool
    | Log_Format of format: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Input_Script_File _ -> "Path to the Jyro script to compile"
            | Output_File _ -> "Output path for the .jyrx file (defaults to same name with .jyrx extension)"
            | Plugin _ -> "Comma-separated paths to plugin assembly DLL files"
            | Plugin_Directory _ -> "Comma-separated directories to search for plugin DLLs (top-level only)"
            | Plugin_Recursive _ -> "Comma-separated directories to search recursively for plugin DLLs"
            | Log_File _ -> "File for logging output (defaults to console only)"
            | Verbosity _ -> "Minimum log level (Trace, Debug, Information, Warning, Error, Critical, None)"
            | Quiet -> "Disable all logging output"
            | Console_Logging _ -> "Enable console logging output (true/false). Default true."
            | Log_Format _ -> "Log output format (text or json). Defaults to text."

/// Run subcommand arguments.
type RunArgs =
    | [<AltCommandLine("-s")>] Input_Script_File of path: string
    | [<AltCommandLine("-i")>] Input_Json_File of path: string
    | [<AltCommandLine("-p")>] Parameters of value: string
    | [<AltCommandLine("-o")>] Output_Json_File of path: string
    | [<AltCommandLine("-c")>] Config of path: string
    | Stats
    | Plugin of assemblies: string
    | [<AltCommandLine("-pd")>] Plugin_Directory of dirs: string
    | [<AltCommandLine("-pr")>] Plugin_Recursive of dirs: string
    | [<AltCommandLine("-l")>] Log_File of path: string
    | [<AltCommandLine("-v")>] Verbosity of LogLevel
    | [<AltCommandLine("-q")>] Quiet
    | Console_Logging of bool
    | Log_Format of format: string
    | Stdin

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Input_Script_File _ -> "Path to the Jyro script or .jyrx binary to execute"
            | Input_Json_File _ -> "JSON file providing Input (defaults to none)"
            | Parameters _ -> "JSON object or file path providing Input._parameters"
            | Output_Json_File _ -> "Output file for script results (defaults to stdout)"
            | Config _ -> "Path to JSON configuration file (searches default locations if not specified)"
            | Stats -> "Display per-stage pipeline timing statistics"
            | Plugin _ -> "Comma-separated paths to plugin assembly DLL files"
            | Plugin_Directory _ -> "Comma-separated directories to search for plugin DLLs (top-level only)"
            | Plugin_Recursive _ -> "Comma-separated directories to search recursively for plugin DLLs"
            | Log_File _ -> "File for logging output (defaults to console only)"
            | Verbosity _ -> "Minimum log level (Trace, Debug, Information, Warning, Error, Critical, None)"
            | Quiet -> "Disable all logging output"
            | Console_Logging _ -> "Enable console logging output (true/false). Default true."
            | Log_Format _ -> "Log output format (text or json). Defaults to text."
            | Stdin -> "Read Jyro script source from stdin (cannot be used with -s)"

/// Validate subcommand arguments.
type ValidateArgs =
    | [<Mandatory>] Compiled of path: string
    | [<Mandatory>] Raw of path: string
    | [<AltCommandLine("-l")>] Log_File of path: string
    | [<AltCommandLine("-v")>] Verbosity of LogLevel
    | [<AltCommandLine("-q")>] Quiet
    | Console_Logging of bool
    | Log_Format of format: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Compiled _ -> "Path to the compiled .jyrx binary file"
            | Raw _ -> "Path to the original .jyro source file"
            | Log_File _ -> "File for logging output (defaults to console only)"
            | Verbosity _ -> "Minimum log level (Trace, Debug, Information, Warning, Error, Critical, None)"
            | Quiet -> "Disable all logging output"
            | Console_Logging _ -> "Enable console logging output (true/false). Default true."
            | Log_Format _ -> "Log output format (text or json). Defaults to text."

/// Test subcommand arguments.
type TestArgs =
    | [<AltCommandLine("-s"); Mandatory>] Input_Script_File of path: string
    | [<AltCommandLine("-i")>] Input_Json_File of path: string
    | [<AltCommandLine("-p")>] Parameters of value: string
    | [<AltCommandLine("-o"); Mandatory>] Output_Json_File of path: string
    | Stats
    | Plugin of assemblies: string
    | [<AltCommandLine("-pd")>] Plugin_Directory of dirs: string
    | [<AltCommandLine("-pr")>] Plugin_Recursive of dirs: string
    | [<AltCommandLine("-l")>] Log_File of path: string
    | [<AltCommandLine("-v")>] Verbosity of LogLevel
    | [<AltCommandLine("-q")>] Quiet
    | Console_Logging of bool
    | Log_Format of format: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Input_Script_File _ -> "Path to the Jyro script or .jyrx binary to execute"
            | Input_Json_File _ -> "JSON file providing Input (defaults to none)"
            | Parameters _ -> "JSON object or file path providing Input._parameters"
            | Output_Json_File _ -> "Path to the expected output JSON file to compare against"
            | Stats -> "Display per-stage pipeline timing statistics"
            | Plugin _ -> "Comma-separated paths to plugin assembly DLL files"
            | Plugin_Directory _ -> "Comma-separated directories to search for plugin DLLs (top-level only)"
            | Plugin_Recursive _ -> "Comma-separated directories to search recursively for plugin DLLs"
            | Log_File _ -> "File for logging output (defaults to console only)"
            | Verbosity _ -> "Minimum log level (Trace, Debug, Information, Warning, Error, Critical, None)"
            | Quiet -> "Disable all logging output"
            | Console_Logging _ -> "Enable console logging output (true/false). Default true."
            | Log_Format _ -> "Log output format (text or json). Defaults to text."

/// Man subcommand arguments.
type ManArgs =
    | [<MainCommand; Last>] Topic of topic: string list

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Topic _ -> "Topic to look up (case-insensitive). Omit to show full index."

/// Init-config subcommand arguments.
type InitConfigArgs =
    | [<AltCommandLine("-o")>] Output of path: string
    | Global
    | [<AltCommandLine("-f")>] Force

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Output _ -> "Explicit output path (overrides --global; defaults to ./jyro.config.json)"
            | Global -> "Write to the per-user config location (%AppData%/Jyro/jyro.config.json on Windows, ~/.config/Jyro/jyro.config.json elsewhere)"
            | Force -> "Overwrite the target file if it already exists"

/// Top-level command arguments.
[<HelpFlags("--help", "-h", "-?")>]
type JyroArgs =
    | [<CliPrefix(CliPrefix.None); AltCommandLine("c")>] Compile of ParseResults<CompileArgs>
    | [<CliPrefix(CliPrefix.None); AltCommandLine("r")>] Run of ParseResults<RunArgs>
    | [<CliPrefix(CliPrefix.None); AltCommandLine("v")>] Validate of ParseResults<ValidateArgs>
    | [<CliPrefix(CliPrefix.None); AltCommandLine("t")>] Test of ParseResults<TestArgs>
    | [<CliPrefix(CliPrefix.None)>] Man of ParseResults<ManArgs>
    | [<CliPrefix(CliPrefix.None)>] Init_Config of ParseResults<InitConfigArgs>
    | Version

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Compile _ -> "Compile a .jyro script to .jyrx precompiled binary"
            | Run _ -> "Run a .jyro script or .jyrx binary"
            | Validate _ -> "Verify a .jyrx binary is a correct compilation of a .jyro source file"
            | Test _ -> "Run a script and compare output against expected JSON"
            | Man _ -> "Browse the Jyro language manual"
            | Init_Config _ -> "Write a default jyro.config.json template"
            | Version -> "Show version information"
