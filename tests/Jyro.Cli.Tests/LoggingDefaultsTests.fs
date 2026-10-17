module RealTree.Jyro.Cli.Tests.LoggingDefaultsTests

open System
open System.IO
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Xunit
open RealTree.Jyro.Cli

/// Contract for where log output goes by default (issue #2).
///
/// Console logging is off unless asked for, and when it is on it goes to
/// stderr, so stdout carries only a command's own output.

[<Fact>]
let ``console logging is off by default`` () =
    Assert.False(JyroCommandOptions.Default.ConsoleLogging)

[<Fact>]
let ``console logging stays off when no configuration sets it`` () =
    let config = ConfigurationBuilder().Build()
    Assert.False((Configuration.bindOptions config).ConsoleLogging)

[<Fact>]
let ``console logging can be turned on by configuration`` () =
    let config =
        ConfigurationBuilder()
            .AddInMemoryCollection(dict [ "ConsoleLogging", "true" ])
            .Build()

    Assert.True((Configuration.bindOptions config).ConsoleLogging)

/// Runs `log` against a logger factory built with console logging on and
/// returns what reached stdout and stderr. The console logger takes its
/// writers when the factory is built, and disposing the factory drains its
/// queue, so both streams are complete when this returns.
let private captureConsole (log: ILogger -> unit) =
    let originalOut = Console.Out
    let originalError = Console.Error
    use out = new StringWriter()
    use err = new StringWriter()
    Console.SetOut(out)
    Console.SetError(err)

    try
        do
            use factory =
                Configuration.createLoggerFactory None LogLevel.Information false true "text"

            log (factory.CreateLogger("Jyro.Cli.Test"))
    finally
        Console.SetOut(originalOut)
        Console.SetError(originalError)

    out.ToString(), err.ToString()

[<Fact>]
let ``console logging goes to stderr, never stdout`` () =
    let stdout, stderr =
        captureConsole (fun logger ->
            logger.LogInformation("an information message")
            logger.LogError("an error message"))

    Assert.Equal("", stdout)
    Assert.Contains("an information message", stderr)
    Assert.Contains("an error message", stderr)
