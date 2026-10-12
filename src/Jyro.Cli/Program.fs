module RealTree.Jyro.Cli.Program

open System
open System.IO
open System.Reflection
open System.Text.Json
open Argu

[<EntryPoint>]
let main argv =
    // Subcommand names, aliases and descriptions come from the JyroArgs union
    // via Argu, which reports an unknown or missing subcommand itself. Listing
    // them here as well would be a second copy to keep in step.
    try
        let parser =
            ArgumentParser.Create<JyroArgs>(
                programName = "jyro",
                helpTextMessage =
                    "Jyro - an imperative data manipulation language for secure, sandboxed processing of JSON-like data structures."
            )

        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)

        if results.Contains JyroArgs.Version then
            let version =
                Assembly
                    .GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()

            match version with
            | null -> Console.WriteLine("jyro (version unknown)")
            | v -> Console.WriteLine($"jyro {v.InformationalVersion}")

            0
        else
            match results.GetSubCommand() with
            | JyroArgs.Compile args -> (Commands.runCompile args).Result
            | JyroArgs.Run args -> (Commands.runRun args).Result
            | JyroArgs.Validate args -> (Commands.runValidate args).Result
            | JyroArgs.Test args -> (Commands.runTest args).Result
            | JyroArgs.Man args ->
                let topic =
                    args.TryGetResult ManArgs.Topic
                    |> Option.map (fun words -> String.concat " " words)

                ManCommand.runMan topic
            | JyroArgs.Init_Config args -> (Commands.runInitConfig args).Result
            | JyroArgs.Version -> 0 // Already handled above
    with
    | :? ArguParseException as ex ->
        // HelpText means --help or a bare invocation: usage on stdout, exit 0.
        // Anything else is a usage error: message on stderr, exit 1.
        if ex.ErrorCode = ErrorCode.HelpText then
            Console.WriteLine(ex.Message)
            0
        else
            Console.Error.WriteLine(ex.Message)
            1
    | :? FileNotFoundException as ex ->
        ConsoleOutput.writeError $"Error: File not found: {ex.FileName |> Option.ofObj |> Option.defaultValue ex.Message}"
        1
    | :? DirectoryNotFoundException as ex ->
        ConsoleOutput.writeError $"Error: Directory not found: {ex.Message}"
        1
    | :? UnauthorizedAccessException as ex ->
        ConsoleOutput.writeError $"Error: Access denied: {ex.Message}"
        1
    | :? JsonException as ex ->
        ConsoleOutput.writeError $"Error: Invalid JSON: {ex.Message}"
        1
    | :? IOException as ex ->
        ConsoleOutput.writeError $"Error: {ex.Message}"
        1
