module RealTree.Jyro.Cli.Commands

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Argu
open RealTree.Jyro
open Microsoft.Extensions.Logging

/// Creates a logger factory from compile subcommand args.
let private createLoggerFromCompileArgs (args: ParseResults<CompileArgs>) =
    let logFile = args.TryGetResult CompileArgs.Log_File
    let verbosity = args.TryGetResult CompileArgs.Verbosity |> Option.defaultValue LogLevel.Information
    let quiet = args.Contains CompileArgs.Quiet
    let consoleLogging = args.TryGetResult CompileArgs.Console_Logging |> Option.defaultValue false
    let logFormat = args.TryGetResult CompileArgs.Log_Format |> Option.defaultValue "text"
    Configuration.createLoggerFactory logFile verbosity quiet consoleLogging logFormat

let private createLoggerFromValidateArgs (args: ParseResults<ValidateArgs>) =
    let logFile = args.TryGetResult ValidateArgs.Log_File
    let verbosity = args.TryGetResult ValidateArgs.Verbosity |> Option.defaultValue LogLevel.Information
    let quiet = args.Contains ValidateArgs.Quiet
    let consoleLogging = args.TryGetResult ValidateArgs.Console_Logging |> Option.defaultValue false
    let logFormat = args.TryGetResult ValidateArgs.Log_Format |> Option.defaultValue "text"
    Configuration.createLoggerFactory logFile verbosity quiet consoleLogging logFormat

let private createLoggerFromTestArgs (args: ParseResults<TestArgs>) =
    let logFile = args.TryGetResult TestArgs.Log_File
    let verbosity = args.TryGetResult TestArgs.Verbosity |> Option.defaultValue LogLevel.Information
    let quiet = args.Contains TestArgs.Quiet
    let consoleLogging = args.TryGetResult TestArgs.Console_Logging |> Option.defaultValue false
    let logFormat = args.TryGetResult TestArgs.Log_Format |> Option.defaultValue "text"
    Configuration.createLoggerFactory logFile verbosity quiet consoleLogging logFormat

/// Builds the default jyro.config.json template as a JSON string.
let private buildDefaultConfigJson () =
    let defaults = JyroCommandOptions.Default
    let strOpt (v: string option) = v |> Option.defaultValue ""

    use stream = new MemoryStream()
    let writerOptions = JsonWriterOptions(Indented = true)
    use writer = new Utf8JsonWriter(stream, writerOptions)

    writer.WriteStartObject()
    writer.WriteString("inputScriptFile", strOpt defaults.InputScriptFile)
    writer.WriteString("inputJsonFile", strOpt defaults.InputJsonFile)
    writer.WriteString("parametersSource", strOpt defaults.ParametersSource)
    writer.WriteString("outputJsonFile", strOpt defaults.OutputJsonFile)
    writer.WriteString("logFile", strOpt defaults.LogFile)
    writer.WriteString("logLevel", defaults.LogLevel.ToString())
    writer.WriteBoolean("noLogging", defaults.NoLogging)
    writer.WriteBoolean("consoleLogging", defaults.ConsoleLogging)
    writer.WriteString("logFormat", defaults.LogFormat)
    writer.WriteBoolean("stats", defaults.Stats)
    writer.WriteString("pluginAssemblies", strOpt defaults.PluginAssemblies)
    writer.WriteString("pluginDirectories", strOpt defaults.PluginDirectories)
    writer.WriteString("pluginRecursiveDirectories", strOpt defaults.PluginRecursiveDirectories)
    writer.WriteEndObject()
    writer.Flush()

    Encoding.UTF8.GetString(stream.ToArray())

/// Resolves the target path for init-config.
let private resolveInitConfigPath (args: ParseResults<InitConfigArgs>) =
    match args.TryGetResult InitConfigArgs.Output with
    | Some path -> path
    | None ->
        if args.Contains InitConfigArgs.Global then
            let appData =
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)

            Path.Combine(appData, "Jyro", "jyro.config.json")
        else
            Path.Combine(Directory.GetCurrentDirectory(), "jyro.config.json")

/// Handles the init-config command.
let runInitConfig (args: ParseResults<InitConfigArgs>) =
    task {
        let targetPath = resolveInitConfigPath args
        let force = args.Contains InitConfigArgs.Force

        if File.Exists(targetPath) && not force then
            ConsoleOutput.writeError
                $"Error: {targetPath} already exists. Use --force to overwrite."

            return 1
        else
            let parentDir = Path.GetDirectoryName(targetPath)

            if not (String.IsNullOrEmpty parentDir) && not (Directory.Exists parentDir) then
                Directory.CreateDirectory(parentDir) |> ignore

            let json = buildDefaultConfigJson ()
            do! File.WriteAllTextAsync(targetPath, json)
            Console.WriteLine($"Wrote default configuration to {targetPath}")
            return 0
    }

/// Handles the compile command.
let runCompile (args: ParseResults<CompileArgs>) =
    task {
        use loggerFactory = createLoggerFromCompileArgs args
        let logger = loggerFactory.CreateLogger("Jyro.Cli.Compile")

        let inputFile = args.GetResult CompileArgs.Input_Script_File
        let outputFile = args.TryGetResult CompileArgs.Output_File

        if not (File.Exists inputFile) then
            ConsoleOutput.writeError $"Error: Input script file not found: {inputFile}"
            return 1
        else
            logger.LogInformation("Compiling {InputFile}", inputFile)

            let! source = File.ReadAllTextAsync(inputFile)

            // WriteLog is registered here even though compilation never invokes
            // it: the linker resolves call sites by name, so without it every
            // script that logs fails to compile with UndefinedFunction.
            let compiler =
                JyroCompiler()
                    .WithSource(source)
                    .WithStdlib()
                    .AddFunction(new LogFunction(logger))

            // Load plugin functions
            let mutable pluginError = false

            try
                let plugins = args.TryGetResult CompileArgs.Plugin
                let pluginDirs = args.TryGetResult CompileArgs.Plugin_Directory
                let pluginRecursive = args.TryGetResult CompileArgs.Plugin_Recursive

                let pluginFunctions =
                    PluginLoader.loadAll plugins pluginDirs pluginRecursive

                for fn in pluginFunctions do
                    compiler.AddFunction(fn) |> ignore

                if pluginFunctions.Length > 0 then
                    logger.LogInformation("Loaded {Count} plugin function(s)", pluginFunctions.Length)
            with ex ->
                ConsoleOutput.writeError $"Error loading plugins: {ex.Message}"
                pluginError <- true

            if pluginError then
                return 1
            else

            let result = compiler.CompileToArtifact()

            match result.TryGetValue() with
            | true, bytes ->
                let outputPath =
                    outputFile |> Option.defaultValue (Path.ChangeExtension(inputFile, ".jyrx"))

                do! File.WriteAllBytesAsync(outputPath, bytes)
                Console.WriteLine($"Compiled {inputFile} -> {outputPath} ({bytes.Length} bytes)")

                logger.LogInformation(
                    "Compilation complete. Output: {OutputFile} ({Size} bytes)",
                    outputPath,
                    bytes.Length
                )

                return 0
            | _ ->
                ConsoleOutput.printDiagnostics result.Messages
                return 1
    }

/// Handles the run command.
let runRun (args: ParseResults<RunArgs>) =
    let hasStdin = args.Contains RunArgs.Stdin
    let hasInputFile = args.TryGetResult RunArgs.Input_Script_File |> Option.isSome

    if hasStdin && hasInputFile then
        ConsoleOutput.writeError "Error: --stdin and --input-script-file (-s) are mutually exclusive."
        Threading.Tasks.Task.FromResult(1)
    else

    // Build configuration with proper precedence
    let configPath = args.TryGetResult RunArgs.Config

    let cliOverrides =
        [ args.TryGetResult RunArgs.Input_Script_File
          |> Option.map (fun v -> "InputScriptFile", v)
          args.TryGetResult RunArgs.Input_Json_File
          |> Option.map (fun v -> "InputJsonFile", v)
          args.TryGetResult RunArgs.Output_Json_File
          |> Option.map (fun v -> "OutputJsonFile", v)
          args.TryGetResult RunArgs.Log_File
          |> Option.map (fun v -> "LogFile", v)
          args.TryGetResult RunArgs.Verbosity
          |> Option.map (fun v -> "LogLevel", v.ToString())
          (if args.Contains RunArgs.Quiet then
               Some("NoLogging", "true")
           else
               None)
          args.TryGetResult RunArgs.Console_Logging
          |> Option.map (fun v -> "ConsoleLogging", v.ToString())
          (if args.Contains RunArgs.Stats then
               Some("Stats", "true")
           else
               None)
          args.TryGetResult RunArgs.Parameters
          |> Option.map (fun v -> "ParametersSource", v)
          args.TryGetResult RunArgs.Plugin
          |> Option.map (fun v -> "PluginAssemblies", v)
          args.TryGetResult RunArgs.Plugin_Directory
          |> Option.map (fun v -> "PluginDirectories", v)
          args.TryGetResult RunArgs.Plugin_Recursive
          |> Option.map (fun v -> "PluginRecursiveDirectories", v)
          args.TryGetResult RunArgs.Log_Format
          |> Option.map (fun v -> "LogFormat", v) ]
        |> List.choose id
        |> Map.ofList

    let config = Configuration.buildConfiguration configPath cliOverrides
    let baseOpts = Configuration.bindOptions config

    if not hasStdin && (baseOpts.InputScriptFile.IsNone || baseOpts.InputScriptFile.Value = "") then
        ConsoleOutput.writeError "Error: --input-script-file (-s) or --stdin is required."
        Threading.Tasks.Task.FromResult(1)
    else

    task {
        let! opts =
            task {
                if hasStdin then
                    let! content = Console.In.ReadToEndAsync()
                    return { baseOpts with StdinSource = Some content }
                else
                    return baseOpts
            }

        // Create logger factory from bound options
        use loggerFactory =
            Configuration.createLoggerFactory
                opts.LogFile
                opts.LogLevel
                opts.NoLogging
                opts.ConsoleLogging
                opts.LogFormat

        let logger = loggerFactory.CreateLogger("Jyro.Cli.Run")
        return! ScriptExecutor.executeAsync opts logger
    }

/// Handles the validate command.
let runValidate (args: ParseResults<ValidateArgs>) =
    task {
        use loggerFactory = createLoggerFromValidateArgs args
        let logger = loggerFactory.CreateLogger("Jyro.Cli.Validate")

        let compiledPath = args.GetResult ValidateArgs.Compiled
        let rawPath = args.GetResult ValidateArgs.Raw

        if not (File.Exists compiledPath) then
            ConsoleOutput.writeError $"Error: Compiled file not found: {compiledPath}"
            return 1
        elif not (File.Exists rawPath) then
            ConsoleOutput.writeError $"Error: Source file not found: {rawPath}"
            return 1
        else
            logger.LogInformation("Validating {CompiledFile} against {SourceFile}", compiledPath, rawPath)

            // Read .jyrx header to get the stored source hash
            let! jyrxBytes = File.ReadAllBytesAsync(compiledPath)
            let headerResult = JyroArtifactInfo.Read jyrxBytes

            match headerResult.TryGetValue() with
            | false, _ ->
                ConsoleOutput.printDiagnostics headerResult.Messages
                return 1
            | true, header ->
                // Compute SHA256 of the raw source file
                let! source = File.ReadAllTextAsync(rawPath)
                let sourceHash = SHA256.HashData(Encoding.UTF8.GetBytes(source: string))

                // Compare hashes
                let storedHash: byte array = header.SourceHash.ToArray()

                if sourceHash.AsSpan().SequenceEqual(storedHash.AsSpan()) then
                    Console.ForegroundColor <- ConsoleColor.Green
                    Console.WriteLine($"[PASS]: {compiledPath} is a valid representation of {rawPath}")
                    Console.ResetColor()
                    logger.LogInformation("Validation passed")
                    return 0
                else
                    Console.ForegroundColor <- ConsoleColor.Red
                    Console.WriteLine($"[FAIL]: {compiledPath} does NOT match {rawPath}")
                    Console.WriteLine($"  Source hash:   {Convert.ToHexString(sourceHash)}")
                    Console.WriteLine($"  Compiled hash: {Convert.ToHexString(storedHash)}")
                    Console.ResetColor()

                    logger.LogWarning(
                        "Validation failed. Source hash: {SourceHash}, Compiled hash: {CompiledHash}",
                        Convert.ToHexString(sourceHash),
                        Convert.ToHexString(storedHash)
                    )

                    return 1
    }

/// Handles the test command.
let runTest (args: ParseResults<TestArgs>) =
    task {
        use loggerFactory = createLoggerFromTestArgs args
        let logger = loggerFactory.CreateLogger("Jyro.Cli.Test")

        let inputFile = args.GetResult TestArgs.Input_Script_File
        let inputJsonFile = args.TryGetResult TestArgs.Input_Json_File
        let parametersSource = args.TryGetResult TestArgs.Parameters
        let expectedFile = args.GetResult TestArgs.Output_Json_File
        let showStats = args.Contains TestArgs.Stats
        let plugins = args.TryGetResult TestArgs.Plugin
        let pluginDirs = args.TryGetResult TestArgs.Plugin_Directory
        let pluginRecursive = args.TryGetResult TestArgs.Plugin_Recursive

        if not (File.Exists inputFile) then
            ConsoleOutput.writeError $"Error: Input script file not found: {inputFile}"
            return 1
        elif not (File.Exists expectedFile) then
            ConsoleOutput.writeError $"Error: Expected output file not found: {expectedFile}"
            return 1
        else
            logger.LogInformation("Testing {InputFile} against {ExpectedFile}", inputFile, expectedFile)

            // Load Input via the shared loader (mirrors the run command's behavior).
            let! inputResult = ScriptExecutor.loadInputAsync inputJsonFile parametersSource

            match inputResult with
            | Error msg ->
                ConsoleOutput.writeError $"Error: {msg}"
                return 1
            | Ok input ->

                // Load plugin functions
                let functions = ResizeArray<IJyroFunction>([ new LogFunction(logger) :> IJyroFunction ])
                let mutable pluginError = false

                try
                    let pluginFunctions =
                        PluginLoader.loadAll plugins pluginDirs pluginRecursive

                    functions.AddRange(pluginFunctions)

                    if pluginFunctions.Length > 0 then
                        logger.LogInformation("Loaded {Count} plugin function(s)", pluginFunctions.Length)
                with ex ->
                    ConsoleOutput.writeError $"Error loading plugins: {ex.Message}"
                    pluginError <- true

                if pluginError then
                    return 1
                else

                let stats = if showStats then Some(JyroPipelineStats()) else None

                let! script = ScriptExecutor.readScriptAsync inputFile
                let result = ScriptExecutor.runPipeline script functions input stats

                let! exitCode =
                    task {
                        if not result.IsSuccess then
                            Console.ForegroundColor <- ConsoleColor.Red
                            Console.WriteLine($"[FAIL]: Execution of {inputFile} failed:")
                            Console.ResetColor()
                            ConsoleOutput.printDiagnostics result.Messages
                            return 1
                        else
                            match result.TryGetValue() with
                            | false, _ ->
                                ConsoleOutput.writeError "[FAIL]: Execution returned null result."
                                return 1
                            | true, resultValue ->
                                // Serialize actual output
                                let actualJson =
                                    JsonSerializer.serializeJyroValue (resultValue.ToObjectValue())

                                // Load expected output and compare
                                let! expectedJson = File.ReadAllTextAsync(expectedFile)
                                let mismatches = JsonComparer.compare expectedJson actualJson

                                if mismatches.IsEmpty then
                                    Console.ForegroundColor <- ConsoleColor.Green

                                    Console.WriteLine(
                                        $"[PASS]: {inputFile} correctly produced output identical to {expectedFile}"
                                    )

                                    Console.ResetColor()
                                    logger.LogInformation("Test passed")
                                    return 0
                                else
                                    Console.ForegroundColor <- ConsoleColor.Red

                                    Console.WriteLine(
                                        $"[FAIL]: {inputFile} produced output that differs from {expectedFile}"
                                    )

                                    Console.ResetColor()
                                    Console.WriteLine($"  {mismatches.Length} difference(s) found:")

                                    for m in mismatches do
                                        Console.WriteLine($"    {m.Path}: expected {m.Expected}, got {m.Actual}")

                                    logger.LogWarning("Test failed with {Count} difference(s)", mismatches.Length)
                                    return 1
                    }

                match stats with
                | Some s -> ConsoleOutput.printPipelineStats s
                | None -> ()

                return exitCode
    }
