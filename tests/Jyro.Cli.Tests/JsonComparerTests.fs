module RealTree.Jyro.Cli.Tests.JsonComparerTests

open Xunit
open RealTree.Jyro.Cli

/// Contract for the semantic JSON comparison behind the `test` command's
/// PASS/FAIL verdict.
///
/// Comparison is structural rather than textual: key order and whitespace do
/// not matter, numbers compare within a relative tolerance, and every
/// difference is reported with the path at which it occurs rather than only as
/// a boolean verdict.

let private mismatches expected actual = JsonComparer.compare expected actual

let private paths expected actual =
    mismatches expected actual |> List.map (fun m -> m.Path)

// -- Equivalence -------------------------------------------------------------

[<Fact>]
let ``a document equals itself`` () =
    let doc = """{"a":1,"b":[1,2,3],"c":{"d":"x"},"e":null,"f":true}"""
    Assert.Empty(mismatches doc doc)

[<Fact>]
let ``key order is not significant`` () =
    Assert.Empty(mismatches """{"a":1,"b":2}""" """{"b":2,"a":1}""")

[<Fact>]
let ``whitespace is not significant`` () =
    Assert.Empty(mismatches """{"a":1}""" "{\n  \"a\" : 1\n}")

[<Fact>]
let ``integers and their decimal spelling compare equal`` () =
    Assert.Empty(mismatches """{"a":1}""" """{"a":1.0}""")

// -- Differences -------------------------------------------------------------

[<Fact>]
let ``a differing scalar is reported at its path`` () =
    Assert.Equal<string list>([ "$.a" ], paths """{"a":1}""" """{"a":2}""")

[<Fact>]
let ``a missing property is reported`` () =
    let m = mismatches """{"a":1,"b":2}""" """{"a":1}""" |> List.exactlyOne
    Assert.Equal("$.b", m.Path)
    Assert.Equal("(missing)", m.Actual)

[<Fact>]
let ``an unexpected property is reported`` () =
    let m = mismatches """{"a":1}""" """{"a":1,"b":2}""" |> List.exactlyOne
    Assert.Equal("$.b", m.Path)
    Assert.Equal("(missing)", m.Expected)

[<Fact>]
let ``a type difference is reported`` () =
    Assert.Equal<string list>([ "$.a" ], paths """{"a":1}""" """{"a":"1"}""")

[<Fact>]
let ``nested paths are reported in full`` () =
    Assert.Equal<string list>([ "$.a.b.c" ], paths """{"a":{"b":{"c":1}}}""" """{"a":{"b":{"c":2}}}""")

// -- Arrays ------------------------------------------------------------------

[<Fact>]
let ``an array length difference is reported`` () =
    let ps = paths """{"a":[1,2,3]}""" """{"a":[1,2]}"""
    Assert.Contains("$.a.length", ps)

[<Fact>]
let ``a differing array element is reported at its index`` () =
    Assert.Equal<string list>([ "$.a[1]" ], paths """{"a":[1,2,3]}""" """{"a":[1,9,3]}""")

[<Fact>]
let ``array order is significant`` () =
    Assert.NotEmpty(mismatches """[1,2]""" """[2,1]""")

// -- Numeric tolerance -------------------------------------------------------

[<Fact>]
let ``numbers within the relative tolerance compare equal`` () =
    // Floating-point results that differ only in the last places must not fail
    // a comparison - Log(1000, 10) landing on 2.9999999999999996 is the case
    // this exists for.
    Assert.Empty(mismatches """{"a":3.0}""" """{"a":2.9999999999999996}""")

[<Fact>]
let ``numbers outside the relative tolerance are reported`` () =
    Assert.Equal<string list>([ "$.a" ], paths """{"a":1.0}""" """{"a":1.01}""")

[<Fact>]
let ``zero is compared against an absolute tolerance`` () =
    Assert.Empty(mismatches """{"a":0.0}""" """{"a":0.0}""")
    Assert.NotEmpty(mismatches """{"a":0.0}""" """{"a":0.001}""")

// -- Reporting ---------------------------------------------------------------

[<Fact>]
let ``every difference is reported, not just the first`` () =
    let ps = paths """{"a":1,"b":2,"c":3}""" """{"a":9,"b":9,"c":9}"""
    Assert.Equal<string list>([ "$.a"; "$.b"; "$.c" ], ps)
