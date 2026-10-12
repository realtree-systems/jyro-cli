module RealTree.Jyro.Cli.ConsoleOutput

open System
open RealTree.Jyro

/// Writes an error message to stderr in red.
let writeError (message: string) =
    Console.ForegroundColor <- ConsoleColor.Red
    Console.Error.WriteLine(message)
    Console.ResetColor()

/// Prints diagnostic messages to stderr in red, each as Jyro formats it:
/// `[JM3100] Ln 1, Col 12: Undeclared variable 'y'`, the same line the
/// run command prints.
let printDiagnostics (messages: DiagnosticMessage seq) =
    for msg in messages do
        writeError (DiagnosticFormatter.formatMessage msg)

/// Prints pipeline timing statistics to stderr.
let printPipelineStats (stats: JyroPipelineStats) =
    Console.Error.WriteLine()
    Console.Error.WriteLine("Pipeline Statistics:")
    Console.Error.WriteLine("  ----------------------------")

    // A load from .jyrx records Deserialize and no Parse; a compilation from
    // source records Parse and no Deserialize. Both record Validate and Link.
    if stats.DeserializeTime > TimeSpan.Zero then
        Console.Error.WriteLine($"  Deserialize:  {stats.DeserializeTime.TotalMilliseconds,8:F2} ms")
    else
        Console.Error.WriteLine($"  Parse:        {stats.ParseTime.TotalMilliseconds,8:F2} ms")

    Console.Error.WriteLine($"  Validate:     {stats.ValidateTime.TotalMilliseconds,8:F2} ms")
    Console.Error.WriteLine($"  Link:         {stats.LinkTime.TotalMilliseconds,8:F2} ms")
    Console.Error.WriteLine($"  Compile:      {stats.CompileTime.TotalMilliseconds,8:F2} ms")
    Console.Error.WriteLine($"  Execute:      {stats.ExecuteTime.TotalMilliseconds,8:F2} ms")
    Console.Error.WriteLine("  ----------------------------")
    Console.Error.WriteLine($"  Total:        {stats.TotalTime.TotalMilliseconds,8:F2} ms")
