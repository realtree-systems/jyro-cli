module RealTree.Jyro.Cli.Configuration

open System
open System.IO
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Console

let private defaultConfigFileName = "jyro.config.json"

/// Searches for a configuration file in default locations.
let findConfigurationFile () =
    // 1. Current directory
    let currentDirConfig =
        Path.Combine(Directory.GetCurrentDirectory(), defaultConfigFileName)

    if File.Exists(currentDirConfig) then
        Some currentDirConfig
    else
        // 2. AppData/Roaming/Jyro
        let appDataPath =
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)

        let appDataConfig = Path.Combine(appDataPath, "Jyro", defaultConfigFileName)

        if File.Exists(appDataConfig) then
            Some appDataConfig
        else
            // 3. User home directory
            let homePath =
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)

            let homeConfig = Path.Combine(homePath, defaultConfigFileName)

            if File.Exists(homeConfig) then
                Some homeConfig
            else
                None

/// Builds an IConfiguration with proper precedence: Command-line > Environment > Config file > Defaults.
let buildConfiguration (configPath: string option) (cliOverrides: Map<string, string>) =
    let builder = ConfigurationBuilder()
    builder.SetBasePath(Directory.GetCurrentDirectory()) |> ignore

    // 1. JSON config file (lowest priority)
    let effectivePath =
        match configPath with
        | Some p when not (String.IsNullOrWhiteSpace p) -> Some p
        | _ -> findConfigurationFile ()

    match effectivePath with
    | Some path ->
        builder.AddJsonFile(path, optional = false, reloadOnChange = false)
        |> ignore
    | None -> ()

    // 2. Environment variables (override config file)
    builder.AddEnvironmentVariables(prefix = "JYRO_") |> ignore

    // 3. CLI args (highest priority)
    if not cliOverrides.IsEmpty then
        builder.AddInMemoryCollection(cliOverrides |> Map.toSeq |> dict)
        |> ignore

    builder.Build() :> IConfiguration

/// Manually binds configuration values to JyroCommandOptions without reflection.
let bindOptions (config: IConfiguration) =
    let mutable opts = JyroCommandOptions.Default

    match config["InputScriptFile"] with
    | null -> ()
    | s -> opts <- { opts with InputScriptFile = Some s }

    match config["InputJsonFile"] with
    | null -> ()
    | s -> opts <- { opts with InputJsonFile = Some s }

    match config["ParametersSource"] with
    | null -> ()
    | s -> opts <- { opts with ParametersSource = Some s }

    match config["OutputJsonFile"] with
    | null -> ()
    | s -> opts <- { opts with OutputJsonFile = Some s }

    match config["LogFile"] with
    | null -> ()
    | s -> opts <- { opts with LogFile = Some s }

    match config["LogLevel"] with
    | null -> ()
    | s ->
        match Enum.TryParse<LogLevel>(s, true) with
        | true, level -> opts <- { opts with LogLevel = level }
        | _ -> ()

    match config["NoLogging"] with
    | null -> ()
    | s ->
        match Boolean.TryParse(s) with
        | true, v -> opts <- { opts with NoLogging = v }
        | _ -> ()

    match config["ConsoleLogging"] with
    | null -> ()
    | s ->
        match Boolean.TryParse(s) with
        | true, v -> opts <- { opts with ConsoleLogging = v }
        | _ -> ()

    match config["LogFormat"] with
    | null -> ()
    | s -> opts <- { opts with LogFormat = s }

    match config["Stats"] with
    | null -> ()
    | s ->
        match Boolean.TryParse(s) with
        | true, v -> opts <- { opts with Stats = v }
        | _ -> ()

    match config["PluginAssemblies"] with
    | null -> ()
    | s -> opts <- { opts with PluginAssemblies = Some s }

    match config["PluginDirectories"] with
    | null -> ()
    | s -> opts <- { opts with PluginDirectories = Some s }

    match config["PluginRecursiveDirectories"] with
    | null -> ()
    | s ->
        opts <-
            { opts with
                PluginRecursiveDirectories = Some s }

    opts

/// Creates a standalone ILoggerFactory configured with the given logging options.
let createLoggerFactory
    (logFile: string option)
    (verbosity: LogLevel)
    (quiet: bool)
    (consoleLogging: bool)
    (logFormat: string)
    (useStdErr: bool)
    =
    let useJson =
        String.Equals(logFormat, "json", StringComparison.OrdinalIgnoreCase)

    LoggerFactory.Create(fun builder ->
        if quiet || verbosity = LogLevel.None then
            builder.SetMinimumLevel(LogLevel.None) |> ignore
        else
            builder.SetMinimumLevel(verbosity) |> ignore

            if consoleLogging then
                if useJson then
                    builder.AddJsonConsole(fun o ->
                        o.IncludeScopes <- false
                        o.TimestampFormat <- "yyyy-MM-ddTHH:mm:ss.fffzzz")
                    |> ignore
                else
                    builder.AddSimpleConsole(fun o ->
                        o.IncludeScopes <- false
                        o.SingleLine <- true
                        o.TimestampFormat <- "yyyy-MM-dd HH:mm:ss ")
                    |> ignore

                if useStdErr then
                    builder.Services.Configure<ConsoleLoggerOptions>(fun (o: ConsoleLoggerOptions) ->
                        o.LogToStandardErrorThreshold <- LogLevel.Trace)
                    |> ignore

            match logFile with
            | Some path when not (String.IsNullOrWhiteSpace path) ->
                builder.AddProvider(new SimpleFileLoggerProvider(path, useJson))
                |> ignore
            | _ -> ())
