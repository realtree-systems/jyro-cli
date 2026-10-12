module RealTree.Jyro.Cli.Tests.LogFunctionTests

open Xunit
open RealTree.Jyro
open RealTree.Jyro.Cli.Tests

/// Contract for the `WriteLog` host function, which bridges a script to the
/// CLI's logging pipeline.
///
/// Two properties here are regression guards rather than plain coverage:
///   * the function is named `WriteLog`, not `Log`, because the standard
///     library already provides `Log` as the natural logarithm and a host
///     function may not shadow a stdlib name - registering one rejects every
///     script with `HostFunctionOverridesStdlib`, whether or not the script
///     ever calls it;
///   * an unrecognised level is a host-defined rejection, reported as
///     `HostFunctionError` with the calling statement's location, not as the
///     `RuntimeError` catch-all the runtime assigns to a function that crashed.

[<Theory>]
[<InlineData("trace", "Trace")>]
[<InlineData("debug", "Debug")>]
[<InlineData("info", "Information")>]
[<InlineData("information", "Information")>]
[<InlineData("warn", "Warning")>]
[<InlineData("warning", "Warning")>]
[<InlineData("error", "Error")>]
[<InlineData("critical", "Critical")>]
let ``each accepted level string maps to its LogLevel`` (level: string, expected: string) =
    let _, logger = Runner.executeWithLog (sprintf "WriteLog(\"%s\", \"m\")" level)
    Assert.Equal<string list>([ expected ], logger.Levels |> List.map string)

[<Theory>]
[<InlineData("INFO")>]
[<InlineData("Info")>]
[<InlineData("iNfO")>]
let ``level matching ignores case`` (level: string) =
    let _, logger = Runner.executeWithLog (sprintf "WriteLog(\"%s\", \"m\")" level)
    Assert.Equal<string list>([ "Information" ], logger.Levels |> List.map string)

[<Fact>]
let ``script messages are prefixed to separate them from runtime output`` () =
    let _, logger = Runner.executeWithLog "WriteLog(\"info\", \"from the script\")"
    Assert.Equal<string list>([ "[Script] from the script" ], logger.Messages)

[<Fact>]
let ``repeated calls are captured in order`` () =
    let source =
        """
        WriteLog("info", "first")
        WriteLog("error", "second")
        WriteLog("debug", "third")
        """

    let _, logger = Runner.executeWithLog source

    Assert.Equal<string list>(
        [ "[Script] first"; "[Script] second"; "[Script] third" ],
        logger.Messages
    )

[<Fact>]
let ``an unrecognised level is a host function error`` () =
    let d = Runner.expectSingleFailure "WriteLog(\"shouty\", \"m\")"
    Assert.Equal(MessageCode.HostFunctionError, d.Code)

[<Fact>]
let ``an unrecognised level names the offending value and the valid ones`` () =
    let d = Runner.expectSingleFailure "WriteLog(\"shouty\", \"m\")"
    Assert.Contains("shouty", d.Message)
    Assert.Contains("Trace", d.Message)
    Assert.Contains("Critical", d.Message)

[<Fact>]
let ``an unrecognised level reports the calling statement's location`` () =
    // The host function itself raises without a location; the runtime attaches
    // the position of the statement that called it on the way out.
    let source = "var x = 1\nWriteLog(\"shouty\", \"m\")\n"
    let d = Runner.expectSingleFailure source

    if d.Location.HasValue then Assert.Equal(2, d.Location.Value.Line)
    else Assert.Fail("Expected the diagnostic to carry a source location")

[<Fact>]
let ``a rejected level is distinguishable from a crashed host function`` () =
    // RuntimeError is what the runtime assigns to an exception escaping a host
    // function. A deliberate rejection must not be reported under that code, or
    // a host cannot tell a bad argument from a bug.
    let d = Runner.expectSingleFailure "WriteLog(\"shouty\", \"m\")"
    Assert.NotEqual(MessageCode.RuntimeError, d.Code)

[<Fact>]
let ``nothing is logged when the level is rejected`` () =
    let _, logger = Runner.executeWithLog "WriteLog(\"shouty\", \"m\")"
    Assert.Empty(logger.Messages)

[<Fact>]
let ``registering WriteLog does not collide with a standard library name`` () =
    // Collision detection is registration-based, so a colliding host function
    // would fail this script even though it never logs.
    let ok, codes = Runner.compile "Output.ok = true"
    Assert.True(ok, sprintf "Expected compilation to succeed; got: %A" codes)
    Assert.DoesNotContain(MessageCode.HostFunctionOverridesStdlib, codes)

[<Fact>]
let ``the standard library Log is still the natural logarithm`` () =
    let output = Runner.expectSuccess "Output.value = Log(1)"
    Assert.Equal(0.0, output |> Runner.prop "value" |> Runner.asNumber)

[<Fact>]
let ``WriteLog and the standard library Log coexist in one script`` () =
    let source =
        """
        var x = Log(1)
        WriteLog("info", "computed")
        Output.value = x
        """

    let result, logger = Runner.executeWithLog source

    Assert.True(result.IsSuccess, sprintf "Diagnostics: %A" (Runner.codesOf result))
    Assert.Equal<string list>([ "[Script] computed" ], logger.Messages)
