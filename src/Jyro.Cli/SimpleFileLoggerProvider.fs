namespace RealTree.Jyro.Cli

open System
open System.IO
open System.Text
open System.Text.Json
open Microsoft.Extensions.Logging

/// A simple file-based logger provider that supports both text and JSON Lines output formats.
type SimpleFileLoggerProvider(filePath: string, useJson: bool) =
    let lockObj = obj ()

    do
        let directory = Path.GetDirectoryName(filePath)

        if not (String.IsNullOrEmpty(directory)) && not (Directory.Exists(directory)) then
            Directory.CreateDirectory(directory) |> ignore

    new(filePath: string) = new SimpleFileLoggerProvider(filePath, false)

    interface ILoggerProvider with
        member _.CreateLogger(categoryName) =
            SimpleFileLogger(filePath, categoryName, useJson, lockObj)

        member _.Dispose() = ()

and private SimpleFileLogger(filePath: string, categoryName: string, useJson: bool, lockObj: obj) =

    interface ILogger with
        member _.BeginScope<'TState>(state: 'TState) = null :> IDisposable

        member _.IsEnabled(logLevel) = logLevel <> LogLevel.None

        member _.Log<'TState>(logLevel, _eventId, state: 'TState, ``exception``, formatter) =
            if logLevel = LogLevel.None then
                ()
            else
                let message = formatter.Invoke(state, ``exception``)

                let logLine =
                    if useJson then
                        // Manual JSON construction for trim-safety (no source generators in F#)
                        use stream = new MemoryStream()
                        use writer = new Utf8JsonWriter(stream)
                        writer.WriteStartObject()

                        writer.WriteString(
                            "Timestamp",
                            DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")
                        )

                        writer.WriteString("LogLevel", logLevel.ToString())
                        writer.WriteString("Category", categoryName)
                        writer.WriteString("Message", message)

                        match ``exception`` with
                        | null -> writer.WriteNull("Exception")
                        | ex -> writer.WriteString("Exception", ex.ToString())

                        writer.WriteEndObject()
                        writer.Flush()
                        Encoding.UTF8.GetString(stream.ToArray())
                    else
                        let timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        let baseLine = $"{timestamp} [{logLevel}] {categoryName}: {message}"

                        match ``exception`` with
                        | null -> baseLine
                        | ex -> baseLine + Environment.NewLine + ex.ToString()

                lock lockObj (fun () -> File.AppendAllText(filePath, logLine + Environment.NewLine))
