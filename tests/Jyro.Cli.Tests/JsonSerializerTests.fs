module RealTree.Jyro.Cli.Tests.JsonSerializerTests

open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open Xunit
open RealTree.Jyro
open RealTree.Jyro.Cli

/// Contract for the JSON the `run` command prints and writes.
///
/// Values reach the serializer as `ToObjectValue` gives them, and an exact
/// non-integer number arrives as a boxed `decimal`. Every number is written
/// as a JSON number, never as a string (issue #1).

/// Both serializer entry points, so the string and stream paths stay alike.
let private serializeBoth (value: obj) =
    let viaString = JsonSerializer.serializeJyroValue value
    use stream = new MemoryStream()
    JsonSerializer.serializeJyroValueToStream value stream
    let viaStream = Encoding.UTF8.GetString(stream.ToArray())
    [ viaString; viaStream ]

let private numberOf (value: JyroValue) = value.ToObjectValue()

[<Theory>]
[<InlineData("3.14")>]
[<InlineData("-0.5")>]
[<InlineData("0.1")>]
let ``an exact decimal is written as a JSON number`` (text: string) =
    let value = numberOf (JyroNumber(System.Decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture)))
    Assert.IsType<decimal>(value) |> ignore

    for json in serializeBoth value do
        use doc = JsonDocument.Parse(json)
        Assert.Equal(JsonValueKind.Number, doc.RootElement.ValueKind)
        Assert.Equal(text, json)

[<Fact>]
let ``an exact decimal inside an array is written as a JSON number`` () =
    let list = List<obj>()
    list.Add(numberOf (JyroNumber(1.5m)))

    for json in serializeBoth list do
        use doc = JsonDocument.Parse(json)
        Assert.Equal(JsonValueKind.Number, doc.RootElement[0].ValueKind)
        Assert.Equal(1.5m, doc.RootElement[0].GetDecimal())

[<Fact>]
let ``an exact decimal inside an object is written as a JSON number`` () =
    let dict = Dictionary<string, obj>()
    dict["pi"] <- numberOf (JyroNumber(3.14m))

    for json in serializeBoth dict do
        use doc = JsonDocument.Parse(json)
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("pi").ValueKind)
        Assert.Equal(3.14m, doc.RootElement.GetProperty("pi").GetDecimal())

[<Fact>]
let ``an exact integer beyond int64 is written as a JSON number`` () =
    // ToObjectValue keeps an integral decimal past int64 range as a decimal.
    let value = numberOf (JyroNumber(100000000000000000000m))
    Assert.IsType<decimal>(value) |> ignore

    for json in serializeBoth value do
        Assert.Equal("100000000000000000000", json)
