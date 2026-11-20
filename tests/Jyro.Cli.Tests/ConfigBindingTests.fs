module RealTree.Jyro.Cli.Tests.ConfigBindingTests

open Microsoft.Extensions.Configuration
open Xunit
open RealTree.Jyro.Cli

/// The template `init-config` writes holds an empty string for every path.
/// An empty or blank path is a setting left unset, not a file called "".

let private bind (pairs: (string * string) list) =
    ConfigurationBuilder()
        .AddInMemoryCollection(dict pairs)
        .Build()
    |> Configuration.bindOptions

let private pathKeys =
    [ "InputScriptFile"
      "InputJsonFile"
      "ParametersSource"
      "OutputJsonFile"
      "LogFile"
      "PluginAssemblies"
      "PluginDirectories"
      "PluginRecursiveDirectories" ]

let private pathsOf (opts: JyroCommandOptions) =
    [ opts.InputScriptFile
      opts.InputJsonFile
      opts.ParametersSource
      opts.OutputJsonFile
      opts.LogFile
      opts.PluginAssemblies
      opts.PluginDirectories
      opts.PluginRecursiveDirectories ]

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
let ``empty or blank paths bind as unset`` (value: string) =
    let opts = bind [ for k in pathKeys -> k, value ]
    Assert.All(pathsOf opts, fun p -> Assert.Equal(None, p))

[<Fact>]
let ``non-empty paths bind as given`` () =
    let opts = bind [ for k in pathKeys -> k, $"{k}.json" ]
    Assert.Equal<string option list>([ for k in pathKeys -> Some $"{k}.json" ], pathsOf opts)
