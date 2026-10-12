module RealTree.Jyro.Cli.Tests.ManIndexTests

open Xunit
open RealTree.Jyro.Cli

/// Contract for resolving `jyro man <topic>`.
///
/// The manual has pages that share a name - most sections have an "Overview" -
/// so a page is named uniquely by its path, and a bare name that several pages
/// share asks which one rather than opening the first.

let private index =
    ManIndex.buildIndexFrom
        [| "man/20 Language/10 Overview.md"
           "man/20 Language/30 Types and Values/20 Numbers.md"
           "man/30 Standard Library/10 Overview.md"
           "man/30 Standard Library/20 String/10 Overview.md"
           "man/30 Standard Library/20 String/20 ToUpper.md"
           "man/30 Standard Library/30 Array/10 Overview.md"
           "man/50 Command Line/20 run.md"
           "not-a-man-page.txt" |]

let private allPages =
    index |> List.collect (fun s -> s.Subsections |> List.collect (fun sub -> sub.Pages))

/// The path of the page a query opens, or a description of what it did instead.
let private opens (query: string) : string =
    match ManIndex.resolveQuery query index with
    | ManIndex.PageFound page -> page.Path
    | ManIndex.Ambiguous choices -> "ambiguous: " + (choices |> List.map fst |> String.concat ", ")
    | other -> string other

[<Theory>]
[<InlineData("toupper", "standard-library/string/toupper")>]
[<InlineData("run", "command-line/run")>]
[<InlineData("string/overview", "standard-library/string/overview")>]
[<InlineData("standard-library/overview", "standard-library/overview")>]
[<InlineData("language/overview", "language/overview")>]
[<InlineData("standard-library/string/overview", "standard-library/string/overview")>]
[<InlineData("/docs/language/types-and-values/numbers", "language/types-and-values/numbers")>]
[<InlineData("String/Overview", "standard-library/string/overview")>]
let ``a path or unique name opens its page`` (query: string, path: string) =
    Assert.Equal(path, opens query)

[<Fact>]
let ``a name several pages share lists each one by a query that opens it`` () =
    Assert.Equal(
        "ambiguous: language/overview, standard-library/overview, string/overview, array/overview",
        opens "overview"
    )

[<Fact>]
let ``every page opens from its query, its path and its site path`` () =
    for page in allPages do
        for query in [ page.Query; page.Path; "/docs/" + page.Path ] do
            Assert.Equal(page.Path, opens query)

[<Fact>]
let ``a page's query is its slug when no other page shares it`` () =
    let toUpper = allPages |> List.find (fun p -> p.Slug = "toupper")
    Assert.Equal("toupper", toUpper.Query)

[<Fact>]
let ``a path no page ends with is not found`` () =
    Assert.Equal(ManIndex.NotFound, ManIndex.resolveQuery "nope/overview" index)

[<Fact>]
let ``a path to a subsection opens its index`` () =
    match ManIndex.resolveQuery "standard-library/string" index with
    | ManIndex.SubsectionFound(sub, _) -> Assert.Equal("standard-library/string", sub.Path)
    | other -> failwithf "expected the String subsection, got %A" other

[<Fact>]
let ``navigation finds a page by its exact path`` () =
    let found = ManIndex.tryFindPage "standard-library/string/overview" index
    Assert.Equal(Some "String", found |> Option.map (fun p -> p.Breadcrumb.Split(" > ").[1]))
