namespace RealTree.Jyro.Cli

open Microsoft.Extensions.Logging

/// Represents the command-line options for the Jyro console application.
type JyroCommandOptions =
    { InputScriptFile: string option
      StdinSource: string option
      InputJsonFile: string option
      ParametersSource: string option
      OutputJsonFile: string option
      LogFile: string option
      LogLevel: LogLevel
      NoLogging: bool
      ConsoleLogging: bool
      LogFormat: string
      Stats: bool
      PluginAssemblies: string option
      PluginDirectories: string option
      PluginRecursiveDirectories: string option }

    static member Default =
        { InputScriptFile = None
          StdinSource = None
          InputJsonFile = None
          ParametersSource = None
          OutputJsonFile = None
          LogFile = None
          LogLevel = LogLevel.Information
          NoLogging = false
          ConsoleLogging = true
          LogFormat = "text"
          Stats = false
          PluginAssemblies = None
          PluginDirectories = None
          PluginRecursiveDirectories = None }

/// Validates the options and returns an error message if invalid.
module JyroCommandOptionsValidation =

    open System.IO

    let validate (opts: JyroCommandOptions) =
        let inputValid =
            match opts.StdinSource with
            | Some s when not (System.String.IsNullOrWhiteSpace s) -> Ok()
            | _ ->
                match opts.InputScriptFile with
                | None
                | Some "" ->
                    Error "Input script file or --stdin is required."
                | Some path when not (File.Exists path) ->
                    Error $"Input script file not found: {path}"
                | _ -> Ok()

        match inputValid with
        | Error msg -> Error msg
        | Ok() ->
            match opts.InputJsonFile with
            | Some path when path <> "" && not (File.Exists path) ->
                Error $"Input JSON file not found: {path}"
            | _ ->
                match opts.ParametersSource with
                | Some src when not (System.String.IsNullOrWhiteSpace src) && not (src.TrimStart().StartsWith("{")) && not (File.Exists src) ->
                    Error $"Parameters file not found: {src}"
                | _ -> Ok opts

/// Represents a single mismatch found during JSON comparison.
type JsonMismatch =
    { Path: string
      Expected: string
      Actual: string }
