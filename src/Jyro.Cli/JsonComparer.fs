module RealTree.Jyro.Cli.JsonComparer

open System
open System.Collections.Generic
open System.Text.Json

let private isNumeric (element: JsonElement) =
    element.ValueKind = JsonValueKind.Number

let rec private compareElements (expected: JsonElement) (actual: JsonElement) (path: string) (mismatches: ResizeArray<JsonMismatch>) =
    if expected.ValueKind <> actual.ValueKind then
        if isNumeric expected && isNumeric actual then
            compareNumbers expected actual path mismatches
        else
            mismatches.Add(
                { Path = path
                  Expected = $"{expected.ValueKind}: {expected.GetRawText()}"
                  Actual = $"{actual.ValueKind}: {actual.GetRawText()}" }
            )
    else
        match expected.ValueKind with
        | JsonValueKind.Object -> compareObjects expected actual path mismatches
        | JsonValueKind.Array -> compareArrays expected actual path mismatches
        | JsonValueKind.Number -> compareNumbers expected actual path mismatches
        | JsonValueKind.String ->
            let es = expected.GetString()
            let a = actual.GetString()

            if es <> a then
                mismatches.Add(
                    { Path = path
                      Expected = es |> Option.ofObj |> Option.defaultValue "null"
                      Actual = a |> Option.ofObj |> Option.defaultValue "null" }
                )
        | JsonValueKind.True
        | JsonValueKind.False ->
            if expected.GetBoolean() <> actual.GetBoolean() then
                mismatches.Add(
                    { Path = path
                      Expected = expected.GetRawText()
                      Actual = actual.GetRawText() }
                )
        | _ -> () // Null == Null: no mismatch

and private compareNumbers (expected: JsonElement) (actual: JsonElement) (path: string) (mismatches: ResizeArray<JsonMismatch>) =
    let e = expected.GetDouble()
    let a = actual.GetDouble()

    if e <> a then
        let mag = Math.Max(Math.Abs(e), Math.Abs(a))
        let tol = if mag = 0.0 then 1e-15 else mag * 1e-10

        if Math.Abs(e - a) > tol then
            mismatches.Add(
                { Path = path
                  Expected = expected.GetRawText()
                  Actual = actual.GetRawText() }
            )

and private compareObjects (expected: JsonElement) (actual: JsonElement) (path: string) (mismatches: ResizeArray<JsonMismatch>) =
    let expectedProps = HashSet<string>()

    for prop in expected.EnumerateObject() do
        expectedProps.Add(prop.Name) |> ignore

        match actual.TryGetProperty(prop.Name) with
        | true, actualProp -> compareElements prop.Value actualProp $"{path}.{prop.Name}" mismatches
        | _ ->
            mismatches.Add(
                { Path = $"{path}.{prop.Name}"
                  Expected = prop.Value.GetRawText()
                  Actual = "(missing)" }
            )

    for prop in actual.EnumerateObject() do
        if not (expectedProps.Contains(prop.Name)) then
            mismatches.Add(
                { Path = $"{path}.{prop.Name}"
                  Expected = "(missing)"
                  Actual = prop.Value.GetRawText() }
            )

and private compareArrays (expected: JsonElement) (actual: JsonElement) (path: string) (mismatches: ResizeArray<JsonMismatch>) =
    let expectedLen = expected.GetArrayLength()
    let actualLen = actual.GetArrayLength()

    if expectedLen <> actualLen then
        mismatches.Add(
            { Path = $"{path}.length"
              Expected = string expectedLen
              Actual = string actualLen }
        )

    let minLen = Math.Min(expectedLen, actualLen)

    for i in 0 .. minLen - 1 do
        compareElements expected[i] actual[i] $"{path}[{i}]" mismatches

/// Compares two JSON strings semantically, returning a list of mismatches.
/// An empty list means the documents are equivalent.
let compare (expectedJson: string) (actualJson: string) =
    use expected = JsonDocument.Parse(expectedJson)
    use actual = JsonDocument.Parse(actualJson)
    let mismatches = ResizeArray<JsonMismatch>()
    compareElements expected.RootElement actual.RootElement "$" mismatches
    mismatches |> Seq.toList
