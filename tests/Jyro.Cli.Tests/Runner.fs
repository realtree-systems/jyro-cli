namespace RealTree.Jyro.Cli.Tests

open System
open Microsoft.Extensions.Logging
open RealTree.Jyro
open RealTree.Jyro.Cli

/// Thin wrapper around `JyroCompiler`/`JyroRunner` configured the way the CLI
/// configures them.
/// Every test goes through `execute` / `compile` so there is a single place
/// where the host-function registrations under test are declared - if the CLI
/// gains or loses a host function, this is the one file that changes.
module Runner =

    /// An `ILogger` that records what a script wrote instead of emitting it.
    /// `IsEnabled` mirrors the real providers (everything except `None`) so
    /// level filtering is exercised rather than bypassed.
    type CapturingLogger() =
        let entries = ResizeArray<LogLevel * string>()

        /// Every captured entry, in the order the script produced it.
        member _.Entries: (LogLevel * string) list = List.ofSeq entries

        /// Captured message text, without levels.
        member _.Messages: string list = entries |> Seq.map snd |> List.ofSeq

        /// The levels captured, without message text.
        member _.Levels: LogLevel list = entries |> Seq.map fst |> List.ofSeq

        interface ILogger with
            member _.BeginScope<'TState>(_state: 'TState) = null :> IDisposable

            member _.IsEnabled(logLevel) = logLevel <> LogLevel.None

            member _.Log<'TState>(logLevel, _eventId, state: 'TState, ``exception``, formatter) =
                entries.Add(logLevel, formatter.Invoke(state, ``exception``))

    /// Build with the registrations the `run` and `test` commands use: the
    /// standard library plus the WriteLog host function.
    let private compilerFor (source: string) (logger: ILogger) =
        JyroCompiler()
            .WithStdlib()
            .WithSource(source)
            .AddFunction(new LogFunction(logger))

    /// Execute `source`, returning the result alongside the logger that
    /// captured whatever the script logged. A compile failure is returned as a
    /// failed execution result so tests handle one result shape.
    let executeWithLog (source: string) : JyroResult<JyroValue> * CapturingLogger =
        let logger = CapturingLogger()
        let compiled = (compilerFor source logger).Compile()

        let result =
            match compiled.TryGetValue() with
            | true, program -> JyroRunner(program).Execute() :> JyroResult<JyroValue>
            | _ -> JyroResult<JyroValue>.Failure(compiled.Messages)

        result, logger

    /// Execute `source` and return the result only.
    let execute (source: string) : JyroResult<JyroValue> = fst (executeWithLog source)

    /// Compile `source` without executing it, mirroring what the `compile`
    /// command registers. Returns success plus the diagnostic codes, so callers
    /// need no reference to the compiled-program type.
    let compile (source: string) : bool * MessageCode list =
        let result = (compilerFor source (CapturingLogger())).Compile()
        result.IsSuccess, (result.Messages |> List.ofSeq |> List.map (fun m -> m.Code))

    /// The diagnostic codes carried by a result.
    let codesOf (result: JyroResult<JyroValue>) : MessageCode list =
        result.Messages |> List.ofSeq |> List.map (fun m -> m.Code)

    // -- Helpers for reading values off a result ------------------------------

    let prop (name: string) (v: JyroValue) : JyroValue = v.GetProperty(name)

    let asNumber (v: JyroValue) : float =
        match v with
        | :? JyroNumber as n -> n.Value
        | _ -> failwithf "expected JyroNumber, got %O" (v.GetType())

    /// Assert execution succeeded and return the output value.
    let expectSuccess (source: string) : JyroValue =
        let result = execute source
        if not result.IsSuccess then
            failwithf
                "Expected success but got failure.\nSource:\n%s\nDiagnostics: %A"
                source
                (result.Messages |> List.ofSeq |> List.map (fun m -> (m.Code, m.Message)))
        match result.TryGetValue() with
        | true, v -> v
        | _ -> failwith "IsSuccess=true but no value present"

    /// Assert execution failed and return the single diagnostic. Tests here are
    /// about one specific failure, so more than one diagnostic is itself a
    /// failure worth surfacing rather than silently taking the head.
    let expectSingleFailure (source: string) : DiagnosticMessage =
        let result = execute source
        if result.IsSuccess then
            failwithf "Expected failure but got success.\nSource:\n%s" source
        match List.ofSeq result.Messages with
        | [ m ] -> m
        | many ->
            failwithf
                "Expected exactly one diagnostic, got %d.\nSource:\n%s\nDiagnostics: %A"
                many.Length
                source
                (many |> List.map (fun m -> (m.Code, m.Message)))
