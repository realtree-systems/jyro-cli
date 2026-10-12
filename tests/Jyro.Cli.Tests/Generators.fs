namespace RealTree.Jyro.Cli.Tests

open System
open FsCheck
open FsCheck.FSharp

/// Generators for CLI property tests.
///
/// JSON is generated as text rather than as a document model, because the
/// comparer's contract is stated over JSON strings and generating text keeps
/// the property honest about parsing as well as comparison.
module Generators =

    /// A short lowercase property name.
    let private genKey: Gen<string> =
        gen {
            let! len = Gen.choose (1, 6)
            let! cs = Gen.listOfLength len (Gen.elements [ for c in 'a' .. 'z' -> c ])
            return String(Array.ofList cs)
        }

    /// A quoted string with no characters needing escapes.
    let private genStringLiteral: Gen<string> =
        gen {
            let! len = Gen.choose (0, 6)
            let chars = [ for c in 'a' .. 'z' -> c ] @ [ for c in '0' .. '9' -> c ] @ [ ' '; '-'; '_' ]
            let! cs = Gen.listOfLength len (Gen.elements chars)
            return sprintf "\"%s\"" (String(Array.ofList cs))
        }

    /// Integers only, so the literal text is culture-independent and survives a
    /// parse/serialise round trip unchanged.
    let private genNumberLiteral: Gen<string> =
        Gen.choose (-10000, 10000) |> Gen.map string

    let private genScalar: Gen<string> =
        Gen.oneof
            [ Gen.constant "null"
              Gen.elements [ "true"; "false" ]
              genNumberLiteral
              genStringLiteral ]

    let rec private genValue (size: int) : Gen<string> =
        if size <= 0 then
            genScalar
        else
            Gen.frequency
                [ 3, genScalar
                  1, genArray (size / 2)
                  1, genObject (size / 2) ]

    and private genArray (size: int) : Gen<string> =
        gen {
            let! n = Gen.choose (0, 3)
            let! items = Gen.listOfLength n (genValue size)
            return "[" + String.Join(",", items) + "]"
        }

    and private genObject (size: int) : Gen<string> =
        gen {
            let! n = Gen.choose (0, 3)
            let! rawKeys = Gen.listOfLength n genKey
            // Duplicate names are legal JSON but ill-defined for a structural
            // comparison, so objects are generated with distinct keys.
            let keys = List.distinct rawKeys
            let! values = Gen.listOfLength keys.Length (genValue size)
            let pairs = List.map2 (fun k v -> sprintf "\"%s\":%s" k v) keys values
            return "{" + String.Join(",", pairs) + "}"
        }

    /// A generated JSON document, wrapped so it is distinguishable from an
    /// arbitrary string.
    type GenJson = { Json: string }

    type JsonArbitraries =
        static member Json() : Arbitrary<GenJson> =
            Gen.sized (fun s -> genValue (min s 6))
            |> Gen.map (fun j -> { Json = j })
            |> Arb.fromGen
