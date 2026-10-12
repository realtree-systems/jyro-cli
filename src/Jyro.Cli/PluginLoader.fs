module RealTree.Jyro.Cli.PluginLoader

open System
open System.Diagnostics.CodeAnalysis
open System.IO
open System.Reflection
open RealTree.Jyro

[<Literal>]
let private PluginLoadingMessage =
    "Plugin loading requires reflection on external assemblies that are not subject to trimming."

/// Discovers all concrete IJyroFunction implementations in an assembly and instantiates them.
[<UnconditionalSuppressMessage("Trimming", "IL2026", Justification = PluginLoadingMessage)>]
[<UnconditionalSuppressMessage("Trimming", "IL2072", Justification = PluginLoadingMessage)>]
let private discoverFunctions (assembly: Assembly) (skipInstantiationErrors: bool) =
    let functionTypes =
        assembly.GetTypes()
        |> Array.filter (fun t ->
            not t.IsAbstract
            && not t.IsInterface
            && typeof<IJyroFunction>.IsAssignableFrom(t))

    [| for functionType in functionTypes do
           try
               match Activator.CreateInstance(functionType) with
               | :? IJyroFunction as instance -> yield instance
               | _ -> ()
           with ex ->
               if not skipInstantiationErrors then
                   raise (
                       InvalidOperationException(
                           $"Failed to instantiate JyroFunction type '{functionType.FullName}'. "
                           + $"Ensure the type has a public parameterless constructor. Inner exception: {ex.Message}",
                           ex
                       )
                   ) |]

/// Splits a comma-separated string into trimmed, non-empty paths.
let private splitPaths (value: string) =
    value.Split(',', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)

/// Loads all IJyroFunction implementations from a DLL file at the specified path.
[<UnconditionalSuppressMessage("Trimming", "IL2026", Justification = PluginLoadingMessage)>]
let loadFromAssembly (assemblyPath: string) =
    if not (File.Exists assemblyPath) then
        raise (FileNotFoundException($"Plugin assembly not found: {assemblyPath}", assemblyPath))

    let assembly = Assembly.LoadFrom(assemblyPath)
    discoverFunctions assembly false

/// Loads all IJyroFunction implementations from all DLL files in the specified directory.
/// Non-.NET assemblies and DLLs with missing dependencies are silently skipped.
[<UnconditionalSuppressMessage("Trimming", "IL2026", Justification = PluginLoadingMessage)>]
let loadFromDirectory (directoryPath: string) (searchOption: SearchOption) =
    if not (Directory.Exists directoryPath) then
        raise (DirectoryNotFoundException($"Plugin directory not found: {directoryPath}"))

    let functions = ResizeArray<IJyroFunction>()
    let dllFiles = Directory.GetFiles(directoryPath, "*.dll", searchOption)

    for dllFile in dllFiles do
        try
            let assembly = Assembly.LoadFrom(dllFile)
            functions.AddRange(discoverFunctions assembly true)
        with
        | :? BadImageFormatException -> () // Skip non-.NET assemblies
        | :? FileLoadException -> () // Skip DLLs that can't be loaded
        | :? FileNotFoundException -> ()

    functions |> Seq.toArray

/// Loads all plugin functions from the specified assemblies and directories.
/// All parameters accept comma-separated values.
let loadAll (assemblies: string option) (directories: string option) (recursiveDirectories: string option) =
    let functions = ResizeArray<IJyroFunction>()

    match assemblies with
    | Some a when not (String.IsNullOrWhiteSpace a) ->
        for path in splitPaths a do
            functions.AddRange(loadFromAssembly path)
    | _ -> ()

    match directories with
    | Some d when not (String.IsNullOrWhiteSpace d) ->
        for dir in splitPaths d do
            functions.AddRange(loadFromDirectory dir SearchOption.TopDirectoryOnly)
    | _ -> ()

    match recursiveDirectories with
    | Some r when not (String.IsNullOrWhiteSpace r) ->
        for dir in splitPaths r do
            functions.AddRange(loadFromDirectory dir SearchOption.AllDirectories)
    | _ -> ()

    functions |> Seq.toArray
