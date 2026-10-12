module RealTree.Jyro.Cli.ManIndex

open System
open System.IO
open System.Reflection
open System.Text.RegularExpressions

/// A single documentation page.
type DocPage =
    { Slug: string
      /// Unique: the section, subsection and page slugs joined with '/',
      /// as in `standard-library/string/overview`.
      Path: string
      /// The shortest query that opens this page and no other: the slug when
      /// no other page shares it, otherwise the fewest trailing segments of
      /// `Path` that are unique, as in `string/overview`.
      Query: string
      DisplayName: string
      Breadcrumb: string
      /// Every page of the subsection, this one included, in order. Their
      /// own `Siblings` are empty.
      Siblings: DocPage list
      ResourceName: string }

/// A subsection containing ordered pages.
type Subsection =
    { SortOrder: int
      Slug: string
      /// Unique: `section/subsection`, or the section slug for a section's
      /// own pages.
      Path: string
      DisplayName: string
      Pages: DocPage list }

/// A top-level section containing ordered subsections.
type Section =
    { SortOrder: int
      Slug: string
      DisplayName: string
      Subsections: Subsection list }

/// Result of resolving a user query against the index.
type QueryResult =
    | PageFound of DocPage
    | SubsectionFound of Subsection * Section
    | SectionFound of Section
    | Ambiguous of (string * string) list
    | NotFound

let private numericPrefixPattern = Regex(@"^\d+\s+", RegexOptions.Compiled)

/// Strip numeric prefix and .md extension, lowercase, replace whitespace with hyphens.
let toSlug (s: string) =
    let name = numericPrefixPattern.Replace(s, "")
    let name = if name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) then name.[.. name.Length - 4] else name
    Regex.Replace(name.Trim().ToLowerInvariant(), @"[\s]+", "-")

/// Strip numeric prefix, return display name (and .md extension removed).
let toDisplayName (s: string) =
    let name = numericPrefixPattern.Replace(s, "")
    if name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) then name.[.. name.Length - 4] else name

/// Parse the numeric sort order from a segment name like "20 Types".
let parseSortOrder (s: string) =
    match Int32.TryParse(s.Split(' ').[0]) with
    | true, n -> n
    | _ -> 999

/// HTML comments, with the line break that follows one. jyro-doc marks pages
/// `<!-- generated ... -->` or `<!-- hand-written -->` for its own tooling;
/// they are not part of the page.
let private htmlCommentPattern = Regex(@"<!--.*?-->[ \t]*\r?\n?", RegexOptions.Compiled ||| RegexOptions.Singleline)

/// Load markdown content from an embedded resource.
let loadContent (page: DocPage) =
    use stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(page.ResourceName)
    match stream with
    | null -> $"[Content not found for '{page.DisplayName}']"
    | s ->
        use reader = new StreamReader(s)
        htmlCommentPattern.Replace(reader.ReadToEnd(), "")

/// Get the first non-heading, non-blank line as a summary.
let getPageSummary (page: DocPage) =
    let content = loadContent page
    content.Split('\n')
    |> Array.tryFind (fun line ->
        let trimmed = line.Trim()
        trimmed.Length > 0 && not (trimmed.StartsWith("#")))
    |> Option.map (fun s -> s.Trim())
    |> Option.defaultValue ""

/// Whether `path` ends with the segments `tail`, whole segments only.
let private endsWith (tail: string array) (path: string array) =
    tail.Length <= path.Length && Array.forall2 (=) tail path.[path.Length - tail.Length ..]

/// Build the documentation index from resource names such as
/// `man/20 Language/30 Types and Values/20 Numbers.md`; others are ignored.
let buildIndexFrom (resourceNames: string array) =
    let manResources =
        resourceNames
        |> Array.filter (fun n -> n.StartsWith("man/") || n.StartsWith(@"man\"))

    // Parse each resource into (sectionName, subsectionName option, fileName, originalResourceName)
    let parsed =
        manResources
        |> Array.choose (fun originalName ->
            let normalized = originalName.Replace('\\', '/')
            let segments = normalized.Split('/')
            match segments.Length with
            | 3 ->
                // Flat: man/50 Command Line/20 run.md
                Some(segments.[1], None, segments.[2], originalName)
            | 4 ->
                // Nested: man/20 Language/30 Types and Values/20 Numbers.md
                Some(segments.[1], Some segments.[2], segments.[3], originalName)
            | _ -> None)

    // Group by section
    let sectionGroups =
        parsed
        |> Array.groupBy (fun (sec, _, _, _) -> sec)
        |> Array.sortBy (fun (sec, _) -> parseSortOrder sec)

    let sections =
        sectionGroups
        |> Array.map (fun (sectionName, items) ->
            let sectionSlug = toSlug sectionName
            let sectionDisplay = toDisplayName sectionName

            // Group by subsection
            let subGroups =
                items
                |> Array.groupBy (fun (_, sub, _, _) -> sub)
                |> Array.sortBy (fun (sub, _) ->
                    match sub with
                    | Some s -> parseSortOrder s
                    | None -> 0)

            let subsections =
                subGroups
                |> Array.map (fun (subName, pages) ->
                    let subSlug, subDisplay, subSort, subPath =
                        match subName with
                        | Some s -> toSlug s, toDisplayName s, parseSortOrder s, $"{sectionSlug}/{toSlug s}"
                        | None -> sectionSlug, sectionDisplay, 0, sectionSlug

                    let breadcrumb =
                        match subName with
                        | Some _ -> $"{sectionDisplay} > {subDisplay}"
                        | None -> sectionDisplay

                    // Queries and siblings are filled in once every page is known
                    let pageList =
                        pages
                        |> Array.sortBy (fun (_, _, f, _) -> parseSortOrder f)
                        |> Array.map (fun (_, _, fileName, resName) ->
                            let slug = toSlug fileName

                            { Slug = slug
                              Path = $"{subPath}/{slug}"
                              Query = slug
                              DisplayName = toDisplayName fileName
                              Breadcrumb = breadcrumb
                              Siblings = []
                              ResourceName = resName })
                        |> Array.toList

                    { SortOrder = subSort
                      Slug = subSlug
                      Path = subPath
                      DisplayName = subDisplay
                      Pages = pageList })
                |> Array.toList

            { SortOrder = parseSortOrder sectionName
              Slug = sectionSlug
              DisplayName = sectionDisplay
              Subsections = subsections })
        |> Array.toList

    // Each page's query: the fewest trailing path segments no other page
    // shares, starting from the slug alone.
    let allPaths =
        sections
        |> List.collect (fun s -> s.Subsections |> List.collect (fun sub -> sub.Pages))
        |> List.map (fun p -> p.Path.Split('/'))

    let queryFor (path: string) =
        let segments = path.Split('/')

        [ 1 .. segments.Length ]
        |> List.map (fun n -> segments.[segments.Length - n ..])
        |> List.find (fun tail ->
            allPaths |> List.filter (fun other -> endsWith tail other) |> List.length = 1)
        |> String.concat "/"

    sections
    |> List.map (fun s ->
        { s with
            Subsections =
                s.Subsections
                |> List.map (fun sub ->
                    let pages = sub.Pages |> List.map (fun p -> { p with Query = queryFor p.Path })
                    { sub with Pages = pages |> List.map (fun p -> { p with Siblings = pages }) }) })

/// Build the documentation index from the embedded manual.
let buildIndex () =
    buildIndexFrom (Assembly.GetExecutingAssembly().GetManifestResourceNames())

/// Normalize user query to match against slugs.
let normalizeQuery (query: string) =
    query.Trim().ToLowerInvariant().Replace(" ", "-")

/// The page whose `Path` is `path`.
let tryFindPage (path: string) (index: Section list) =
    index
    |> List.tryPick (fun s ->
        s.Subsections |> List.tryPick (fun sub -> sub.Pages |> List.tryFind (fun p -> p.Path = path)))

/// The subsection whose `Path` is `path`, with its section.
let tryFindSubsection (path: string) (index: Section list) =
    index
    |> List.tryPick (fun s -> s.Subsections |> List.tryFind (fun sub -> sub.Path = path) |> Option.map (fun sub -> sub, s))

/// The section whose slug is `slug`.
let tryFindSection (slug: string) (index: Section list) =
    index |> List.tryFind (fun s -> s.Slug = slug)

/// The pages of the index, as a choice to offer when a query is ambiguous.
let private asChoices (pages: DocPage list) =
    Ambiguous(pages |> List.map (fun p -> (p.Query, p.Breadcrumb)))

/// Resolve a path query, such as `string/overview` or the site's
/// `/docs/standard-library/string/overview`: the trailing segments of a
/// page's path, else of a subsection's, else a section's slug.
let private resolvePath (q: string) (allPages: DocPage list) (allSubsections: (Subsection * Section) list) (index: Section list) =
    let q = q.Trim('/')
    let q = if q.StartsWith("docs/") then q.Substring(5) else q
    let tail = q.Split('/', StringSplitOptions.RemoveEmptyEntries)

    match allPages |> List.filter (fun p -> endsWith tail (p.Path.Split('/'))) with
    | [ page ] -> PageFound page
    | _ :: _ as pages -> asChoices pages
    | [] ->

    match allSubsections |> List.filter (fun (sub, _) -> endsWith tail (sub.Path.Split('/'))) with
    | [ (sub, sec) ] -> SubsectionFound(sub, sec)
    | _ :: _ as subs ->
        Ambiguous(subs |> List.map (fun (sub, sec) -> (sub.Path, $"{sec.DisplayName} > {sub.DisplayName}")))
    | [] ->

    match index |> List.tryFind (fun s -> tail = [| s.Slug |]) with
    | Some sec -> SectionFound sec
    | None -> NotFound

/// Resolve a user query against the index.
let resolveQuery (query: string) (index: Section list) =
    let q = normalizeQuery query

    let allPages =
        index
        |> List.collect (fun s -> s.Subsections |> List.collect (fun sub -> sub.Pages))

    let allSubsections =
        index
        |> List.collect (fun s -> s.Subsections |> List.map (fun sub -> (sub, s)))

    if q.Contains('/') then resolvePath q allPages allSubsections index else

    // Phase 1: exact page slug. Several sections have an "Overview", so a
    // slug that names more than one page asks which.
    match allPages |> List.filter (fun p -> p.Slug = q) with
    | [ page ] -> PageFound page
    | _ :: _ as pages -> asChoices pages
    | [] ->

    // Phase 2: exact subsection slug
    match allSubsections |> List.tryFind (fun (sub, _) -> sub.Slug = q) with
    | Some(sub, sec) -> SubsectionFound(sub, sec)
    | None ->

    // Phase 3: exact section slug
    match index |> List.tryFind (fun s -> s.Slug = q) with
    | Some sec -> SectionFound sec
    | None ->

    // Word-boundary prefix match: slug starts with query, or query appears
    // after a hyphen in the slug. Prevents false positives like "man" matching
    // "selectmany" while still allowing "control" -> "control-flow".
    let isWordMatch (slug: string) =
        slug.StartsWith(q) || slug.Contains("-" + q)

    // Phase 4: word-boundary match on page slugs
    let pageMatches = allPages |> List.filter (fun p -> isWordMatch p.Slug)

    match pageMatches with
    | [ single ] -> PageFound single
    | [] ->
        // Phase 5: word-boundary match on subsection slugs
        let subMatches = allSubsections |> List.filter (fun (sub, _) -> isWordMatch sub.Slug)

        match subMatches with
        | [ (sub, sec) ] -> SubsectionFound(sub, sec)
        | [] -> NotFound
        | multiple ->
            Ambiguous(
                multiple
                |> List.map (fun (sub, sec) -> (sub.Path, $"{sec.DisplayName} > {sub.DisplayName}"))
            )
    | multiple ->
        asChoices multiple
