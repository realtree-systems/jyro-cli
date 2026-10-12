module RealTree.Jyro.Cli.Tests.CommandArgsTests

open Argu
open Xunit
open RealTree.Jyro.Cli

/// Contract for top-level argument parsing.
///
/// Subcommand names, aliases and usage text are derived by Argu from the
/// `JyroArgs` union, which is the only place they are written down. Argu also
/// reports a missing or unknown subcommand itself. These tests pin that
/// arrangement so a second, hand-maintained copy of the list cannot drift back
/// in unnoticed.

let private parser = ArgumentParser.Create<JyroArgs>(programName = "jyro")

let private parse (argv: string list) = parser.ParseCommandLine(inputs = List.toArray argv, raiseOnUsage = true)

/// The subcommand a command line resolves to, as a stable string so the
/// expectation can travel in an `InlineData`.
let private subCommandOf (argv: string list) : string =
    match (parse argv).GetSubCommand() with
    | Compile _ -> "compile"
    | Run _ -> "run"
    | Validate _ -> "validate"
    | Test _ -> "test"
    | Man _ -> "man"
    | Init_Config _ -> "init-config"
    | Version -> "version"

let private errorCodeOf (argv: string list) : ErrorCode =
    let ex = Assert.Throws<ArguParseException>(fun () -> parse argv |> ignore)
    ex.ErrorCode

// -- Subcommand resolution ---------------------------------------------------

[<Fact>]
let ``compile resolves, by name and by alias`` () =
    Assert.Equal("compile", subCommandOf [ "compile"; "-s"; "a.jyro" ])
    Assert.Equal("compile", subCommandOf [ "c"; "-s"; "a.jyro" ])

[<Fact>]
let ``run resolves, by name and by alias`` () =
    Assert.Equal("run", subCommandOf [ "run"; "-s"; "a.jyro" ])
    Assert.Equal("run", subCommandOf [ "r"; "-s"; "a.jyro" ])

[<Fact>]
let ``validate resolves, by name and by alias`` () =
    let args = [ "--compiled"; "a.jyrx"; "--raw"; "a.jyro" ]
    Assert.Equal("validate", subCommandOf ("validate" :: args))
    Assert.Equal("validate", subCommandOf ("v" :: args))

[<Fact>]
let ``test resolves, by name and by alias`` () =
    let args = [ "-s"; "a.jyro"; "-o"; "expected.json" ]
    Assert.Equal("test", subCommandOf ("test" :: args))
    Assert.Equal("test", subCommandOf ("t" :: args))

[<Fact>]
let ``man resolves`` () = Assert.Equal("man", subCommandOf [ "man" ])

[<Fact>]
let ``init-config resolves`` () =
    Assert.Equal("init-config", subCommandOf [ "init-config" ])

// -- Help and version --------------------------------------------------------

[<Theory>]
[<InlineData("--help")>]
[<InlineData("-h")>]
[<InlineData("-?")>]
let ``every help flag is recognised as help rather than an error`` (flag: string) =
    // The entry point routes HelpText to stdout with exit 0 and everything else
    // to stderr with exit 1, so this classification is what separates them.
    Assert.Equal(ErrorCode.HelpText, errorCodeOf [ flag ])

[<Fact>]
let ``version parses without a subcommand`` () =
    let results = parse [ "--version" ]
    Assert.True(results.Contains JyroArgs.Version)

// -- Failure classification --------------------------------------------------

[<Fact>]
let ``no arguments parses, but resolves to no subcommand`` () =
    // An empty command line is accepted; the absence of a subcommand surfaces
    // only when one is asked for. The entry point catches that as a usage
    // error, which is what makes a bare `jyro` exit non-zero.
    let results = parse []
    let ex = Assert.Throws<ArguParseException>(fun () -> results.GetSubCommand() |> ignore)
    Assert.NotEqual(ErrorCode.HelpText, ex.ErrorCode)

[<Fact>]
let ``an unknown subcommand is a usage error`` () =
    Assert.NotEqual(ErrorCode.HelpText, errorCodeOf [ "bogus" ])

[<Fact>]
let ``an unknown subcommand is named in the message`` () =
    let ex = Assert.Throws<ArguParseException>(fun () -> parse [ "bogus" ] |> ignore)
    Assert.Contains("bogus", ex.Message)

[<Fact>]
let ``usage errors list the available subcommands`` () =
    // The listing is generated from JyroArgs, so this is what makes a separate
    // hand-written list unnecessary.
    let ex = Assert.Throws<ArguParseException>(fun () -> parse [ "bogus" ] |> ignore)

    for name in [ "compile"; "run"; "validate"; "test"; "man"; "init-config" ] do
        Assert.Contains(name, ex.Message)

[<Fact>]
let ``a missing mandatory argument is a usage error`` () =
    Assert.NotEqual(ErrorCode.HelpText, errorCodeOf [ "compile" ])
