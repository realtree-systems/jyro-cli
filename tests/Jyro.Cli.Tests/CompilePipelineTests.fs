module RealTree.Jyro.Cli.Tests.CompilePipelineTests

open System
open System.IO
open Argu
open Xunit
open RealTree.Jyro.Cli

/// End-to-end contract for the `compile` command.
///
/// Compilation never invokes a host function, but it does link, and the linker
/// resolves call sites by name. A host function registered for execution but
/// not for compilation is therefore an `UndefinedFunction` in every script that
/// calls it: the script runs but cannot be precompiled. These tests drive the
/// real command, with real files, so that asymmetry between the two pipelines
/// cannot return unnoticed.

let private compileParser = ArgumentParser.Create<CompileArgs>(programName = "jyro compile")

/// Write `source` to a scratch directory, run the real `compile` command over
/// it, and hand the exit code and output path to `body`. The directory is
/// removed afterwards whether or not the assertions pass.
let private compiling (source: string) (body: int -> string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), "jyro-cli-tests", Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore

    try
        let scriptPath = Path.Combine(dir, "script.jyro")
        let outputPath = Path.Combine(dir, "script.jyrx")
        File.WriteAllText(scriptPath, source)

        let args =
            compileParser.Parse(
                [| "-s"; scriptPath; "-o"; outputPath; "--quiet" |],
                raiseOnUsage = true
            )

        body (Commands.runCompile args).Result outputPath
    finally
        try
            Directory.Delete(dir, true)
        with _ ->
            ()

[<Fact>]
let ``a script that logs can be precompiled`` () =
    let source =
        """
        WriteLog("info", "starting")
        Output.ok = true
        """

    compiling source (fun exitCode outputPath ->
        Assert.Equal(0, exitCode)
        Assert.True(File.Exists outputPath, "Expected a .jyrx file to be written")
        Assert.NotEqual(0L, FileInfo(outputPath).Length))

[<Fact>]
let ``a script that only computes can be precompiled`` () =
    compiling "Output.value = Log(1)" (fun exitCode outputPath ->
        Assert.Equal(0, exitCode)
        Assert.True(File.Exists outputPath, "Expected a .jyrx file to be written"))

[<Fact>]
let ``a call to a genuinely undefined function still fails`` () =
    // Confirms the pipeline really is resolving names, so the tests above are
    // evidence of registration rather than of linking being skipped.
    compiling "NoSuchFunction(1)" (fun exitCode outputPath ->
        Assert.NotEqual(0, exitCode)
        Assert.False(File.Exists outputPath, "No output should be written for a failed compile"))

[<Fact>]
let ``a syntactically invalid script fails`` () =
    compiling "Output.ok = " (fun exitCode _ -> Assert.NotEqual(0, exitCode))
