namespace RealTree.Jyro.Cli

open System.Collections.Generic
open RealTree.Jyro
open Microsoft.Extensions.Logging

/// Host function that lets Jyro scripts write to the CLI logging pipeline.
/// Usage: WriteLog("Info", "message text")
///
/// The stdlib already provides 'Log()' as the natural logarithm, and a host function
/// may not shadow a stdlib name
type internal LogFunction(logger: ILogger) =
    inherit
        JyroFunctionBase(
            "WriteLog",
            JyroFunctionSignature.Create(
                [ Parameter.Required("level", ParameterType.String)
                  Parameter.Required("message", ParameterType.String) ],
                ParameterType.Null
            )
        )

    // Writing to the host log pipeline is a host-side effect, but a benign
    // diagnostic one - classified NonDeterministic so scripts may log from
    // anywhere a library function is permitted.
    override _.Effect = FunctionEffect.NonDeterministic

    override _.Execute(args: IReadOnlyList<JyroValue>, _ctx: JyroExecutionContext) =
        let levelStr = args.GetString(0)
        let message = args.GetString(1)

        let logLevel =
            match levelStr.ToLowerInvariant() with
            | "trace" -> LogLevel.Trace
            | "debug" -> LogLevel.Debug
            | "information"
            | "info" -> LogLevel.Information
            | "warning"
            | "warn" -> LogLevel.Warning
            | "error" -> LogLevel.Error
            | "critical" -> LogLevel.Critical
            // Valid log levels are a CLI concern, not a language one, so this
            // reports as a host error named by a reason rather than minting a
            // MessageCode in Jyro.Core.
            | _ ->
                raise (
                    JyroError.Host(
                        "InvalidLogLevel",
                        levelStr,
                        "Valid values: Trace, Debug, Info/Information, Warn/Warning, Error, Critical"
                    )
                )

        if logger.IsEnabled(logLevel) then
            logger.Log(logLevel, "[Script] {Message}", [| message :> obj |])

        JyroNull.Instance
