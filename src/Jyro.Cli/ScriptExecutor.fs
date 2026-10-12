module RealTree.Jyro.Cli.ScriptExecutor

open System
open System.IO
open RealTree.Jyro
open Microsoft.Extensions.Logging

/// Loads -i contents as Input. If -p is also supplied, attaches it as Input._parameters.
/// When both are supplied, the input file must contain a JSON object so that _parameters
/// can be attached as a sibling property. Returns Error with a host-friendly message on
/// missing files or invalid combinations; Ok with the resulting JyroValue otherwise.
let loadInputAsync (inputJsonFile: string option) (parametersSource: string option) =
    task {
        // 1. Resolve baseInput from -i (or JyroNull if not supplied / file missing-error)
        let! baseInputResult =
            task {
                match inputJsonFile with
                | Some path when not (String.IsNullOrWhiteSpace path) ->
                    if not (File.Exists path) then
                        return Error $"Input JSON file not found: {path}"
                    else
                        use stream = File.OpenRead(path)
                        let! v = JyroValue.FromJsonAsync(stream, Threading.CancellationToken.None)
                        return Ok v
                | _ -> return Ok(JyroNull.Instance :> JyroValue)
            }

        match baseInputResult with
        | Error msg -> return Error msg
        | Ok baseInput ->
            // 2. Resolve parameters from -p (inline JSON or file path)
            let! parametersResult =
                task {
                    match parametersSource with
                    | Some src when not (String.IsNullOrWhiteSpace src) ->
                        let trimmed = src.Trim()
                        if trimmed.StartsWith("{") then
                            return Ok(Some(JyroValue.FromJson(trimmed)))
                        elif not (File.Exists trimmed) then
                            return Error $"Parameters file not found: {trimmed}"
                        else
                            use stream = File.OpenRead(trimmed)
                            let! v = JyroValue.FromJsonAsync(stream, Threading.CancellationToken.None)
                            return Ok(Some v)
                    | _ -> return Ok None
                }

            match parametersResult with
            | Error msg -> return Error msg
            | Ok None -> return Ok baseInput
            | Ok(Some parameters) ->
                // 3. Merge: parameters live at Input._parameters
                match baseInput with
                | :? JyroNull ->
                    let obj = JyroObject()
                    obj.SetProperty("_parameters", parameters)
                    return Ok(obj :> JyroValue)
                | :? JyroObject as obj ->
                    obj.SetProperty("_parameters", parameters)
                    return Ok(obj :> JyroValue)
                | _ ->
                    return
                        Error
                            "When --parameters is supplied alongside --input-json-file, the input file must contain a JSON object so that _parameters can be attached."
    }

/// A script to run: Jyro source text, or a compiled .jyrx artifact.
type Script =
    | Source of string
    | Artifact of byte array

/// Reads `path` as a .jyrx artifact when it has that extension, otherwise as
/// source text.
let readScriptAsync (path: string) =
    task {
        if Path.GetExtension(path).Equals(".jyrx", StringComparison.OrdinalIgnoreCase) then
            let! bytes = File.ReadAllBytesAsync(path)
            return Artifact bytes
        else
            let! source = File.ReadAllTextAsync(path)
            return Source source
    }

/// Loads plugin functions.
let private loadPlugins (opts: JyroCommandOptions) (logger: ILogger) =
    let pluginFunctions =
        PluginLoader.loadAll opts.PluginAssemblies opts.PluginDirectories opts.PluginRecursiveDirectories

    if pluginFunctions.Length > 0 then
        logger.LogInformation("Loaded {Count} plugin function(s)", pluginFunctions.Length)

    pluginFunctions

/// Compiles `script` from source, or loads it from an artifact, with the
/// stdlib and `functions` available to it.
let private compileScript
    (script: Script)
    (functions: IJyroFunction seq)
    (stats: JyroPipelineStats option)
    : JyroResult<CompiledProgram> =
    match script with
    | Source source ->
        let compiler = JyroCompiler().WithSource(source).WithStdlib().AddFunctions(functions)
        stats |> Option.iter (fun s -> compiler.WithStats(s) |> ignore)
        compiler.Compile()
    | Artifact bytes ->
        let loader = JyroArtifactLoader(bytes).WithStdlib().AddFunctions(functions)
        stats |> Option.iter (fun s -> loader.WithStats(s) |> ignore)
        loader.Load()

/// Compiles `script` and, on success, executes the program against `input`,
/// threading the same stats collector through both stages. A compile failure
/// is returned as a failed execution result so callers handle one result
/// shape.
let runPipeline
    (script: Script)
    (functions: IJyroFunction seq)
    (input: JyroValue)
    (stats: JyroPipelineStats option)
    : JyroResult<JyroValue> =
    let compiled = compileScript script functions stats

    match compiled.TryGetValue() with
    | true, program ->
        let runner = JyroRunner(program).WithInput(input)
        stats |> Option.iter (fun s -> runner.WithStats(s) |> ignore)
        runner.Execute()
    | _ -> JyroResult<JyroValue>.Failure(compiled.Messages)

/// Outputs the script execution results to console and/or file. A failed run
/// prints its diagnostics to stderr; any output value that survived the run -
/// partial or complete - is still written, and the exit code reports the
/// failure.
let private outputResultsAsync (opts: JyroCommandOptions) (logger: ILogger) (result: JyroResult<JyroValue>) =
    task {
        if not result.IsSuccess then
            ConsoleOutput.printDiagnostics result.Messages

        match result.TryGetValue() with
        | true, value ->
            let objValue = value.ToObjectValue()

            match opts.OutputJsonFile with
            | Some outputFile when not (String.IsNullOrWhiteSpace outputFile) ->
                use fileStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write)
                JsonSerializer.serializeJyroValueToStream objValue fileStream

                if logger.IsEnabled(LogLevel.Information) then
                    if result.IsSuccess then
                        logger.LogInformation("Execution complete. Output written to {OutputFile}", outputFile)
                    else
                        logger.LogInformation("Execution failed. Partial output written to {OutputFile}", outputFile)
            | _ ->
                use stdoutStream = Console.OpenStandardOutput()
                JsonSerializer.serializeJyroValueToStream objValue stdoutStream
                stdoutStream.WriteByte(byte '\n')

                if result.IsSuccess then
                    logger.LogInformation("Execution complete. Output written to stdout")
                else
                    logger.LogInformation("Execution failed. Partial output written to stdout")
        | _ ->
            if result.IsSuccess then
                ConsoleOutput.writeError "Error: Execution returned null result."

        return if result.IsSuccess && result.HasValue then 0 else 1
    }

/// Executes the Jyro script according to the configured options.
let executeAsync (opts: JyroCommandOptions) (logger: ILogger) =
    task {
        match JyroCommandOptionsValidation.validate opts with
        | Error msg ->
            ConsoleOutput.writeError $"Error: {msg}"
            return 1
        | Ok opts ->

            if logger.IsEnabled(LogLevel.Information) then
                logger.LogInformation(
                    "Starting Jyro execution. Script={Script}, Input={Input}, Output={Output}",
                    (if opts.StdinSource.IsSome then "(stdin)"
                     else opts.InputScriptFile |> Option.defaultValue ""),
                    opts.InputJsonFile |> Option.defaultValue "(none)",
                    opts.OutputJsonFile |> Option.defaultValue "(stdout)"
                )

            let! inputResult = loadInputAsync opts.InputJsonFile opts.ParametersSource

            match inputResult with
            | Error msg ->
                ConsoleOutput.writeError $"Error: {msg}"
                return 1
            | Ok input ->
                let! script =
                    task {
                        match opts.StdinSource with
                        | Some source when not (String.IsNullOrWhiteSpace source) ->
                            logger.LogInformation("Executing script from stdin")
                            return Source source
                        | _ ->
                            let! script = readScriptAsync opts.InputScriptFile.Value

                            match script with
                            | Artifact _ -> logger.LogInformation("Executing binary")
                            | Source _ -> logger.LogInformation("Executing script")

                            return script
                    }

                let functions =
                    Seq.append [ new LogFunction(logger) :> IJyroFunction ] (loadPlugins opts logger)

                let stats = if opts.Stats then Some(JyroPipelineStats()) else None

                let result = runPipeline script functions input stats
                let! exitCode = outputResultsAsync opts logger result

                match stats with
                | Some s -> ConsoleOutput.printPipelineStats s
                | None -> ()

                return exitCode
    }
