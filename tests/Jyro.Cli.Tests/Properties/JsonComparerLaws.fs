namespace RealTree.Jyro.Cli.Tests.Properties

open System.Text.Json
open FsCheck.Xunit
open RealTree.Jyro.Cli
open RealTree.Jyro.Cli.Tests.Generators

/// Laws the JSON comparer holds for any document, not just the hand-written
/// cases. The `test` command's verdict is only trustworthy if a document
/// compares equal to itself and to any re-spelling of itself; a comparer that
/// reported spurious differences would fail scripts that are in fact correct.
module JsonComparerLaws =

    /// Re-emit a document from its parsed form, changing whitespace but not
    /// structure.
    let private reformat (json: string) : string =
        use doc = JsonDocument.Parse(json)
        JsonSerializer.Serialize(doc.RootElement, JsonSerializerOptions(WriteIndented = true))

    [<Property(Arbitrary = [| typeof<JsonArbitraries> |])>]
    let ``generated documents are valid JSON`` (doc: GenJson) =
        // Guards the generator itself: were it to emit invalid JSON the laws
        // below would throw rather than fail, which reads as an error, not a
        // refutation.
        use parsed = JsonDocument.Parse(doc.Json)
        parsed.RootElement.ValueKind <> JsonValueKind.Undefined

    [<Property(Arbitrary = [| typeof<JsonArbitraries> |])>]
    let ``a document compares equal to itself`` (doc: GenJson) =
        JsonComparer.compare doc.Json doc.Json |> List.isEmpty

    [<Property(Arbitrary = [| typeof<JsonArbitraries> |])>]
    let ``formatting is not significant`` (doc: GenJson) =
        JsonComparer.compare doc.Json (reformat doc.Json) |> List.isEmpty

    [<Property(Arbitrary = [| typeof<JsonArbitraries> |])>]
    let ``equivalence does not depend on argument order`` (a: GenJson) (b: GenJson) =
        let forward = JsonComparer.compare a.Json b.Json |> List.isEmpty
        let backward = JsonComparer.compare b.Json a.Json |> List.isEmpty
        forward = backward
