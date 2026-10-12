module RealTree.Jyro.Cli.ManPageRenderer

open System
open System.Text.RegularExpressions

// ANSI escape sequences
let private esc = "\x1B["
let private reset = $"{esc}0m"
let private bold = $"{esc}1m"
let private dim = $"{esc}2m"
let private underline = $"{esc}4m"
let private inverse = $"{esc}7m"
let private cyan = $"{esc}36m"
let private green = $"{esc}32m"
let private yellow = $"{esc}33m"
let private red = $"{esc}31m"
let private blue = $"{esc}34m"
let private gray = $"{esc}90m"
let private white = $"{esc}37m"
let private boldCyan = $"{bold}{cyan}"
let private boldWhite = $"{bold}{white}"
let private boldRed = $"{bold}{red}"
let private boldYellow = $"{bold}{yellow}"
let private boldBlue = $"{bold}{blue}"
let private boldGreen = $"{bold}{green}"

/// Navigation target for a selectable line. Pages and subsections are named
/// by their unique `Path`; a slug can be shared, as "overview" is.
[<RequireQualifiedAccess>]
type NavTarget =
    | Page of path: string
    | Subsection of path: string
    | Section of slug: string
    | Back

/// A rendered line with optional navigation target.
type RenderedLine =
    { Text: string
      Link: NavTarget option }

let plainLine text = { Text = text; Link = None }
let linkLine text target = { Text = text; Link = Some target }

/// Strip ANSI escape sequences for width measurement.
let stripAnsi (s: string) = Regex.Replace(s, @"\x1B\[[0-9;]*m", "")

/// Render state for the single-pass processor.
type private RenderState =
    | Normal
    | InFencedCode of lang: string
    | InTable

let private inlineCodePattern = Regex(@"`([^`]+)`", RegexOptions.Compiled)
let private boldPattern = Regex(@"\*\*([^*]+)\*\*", RegexOptions.Compiled)
let private linkPattern = Regex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled)

/// Apply inline formatting (links, bold, code) to a line of text. A link
/// renders as its text: the manual's targets are website paths (`/docs/...`)
/// that mean nothing in a terminal.
let private formatInline (line: string) =
    let line = linkPattern.Replace(line, "$1")
    let line = boldPattern.Replace(line, fun m -> $"{bold}{m.Groups.[1].Value}{reset}")
    let line = inlineCodePattern.Replace(line, fun m -> $"{cyan}{m.Groups.[1].Value}{reset}")
    line

/// Render a markdown table block to lines.
let private renderTableToLines (lines: string list) =
    let parseRow (line: string) =
        line.Trim().Trim('|').Split('|')
        |> Array.map (fun cell -> cell.Trim())

    let rows = lines |> List.map parseRow

    let isSeparator (cells: string array) =
        cells |> Array.forall (fun c -> c.TrimStart('-').Length = 0 || c.Trim() = "")

    let formattedRows = rows |> List.map (Array.map formatInline)
    let colCount = rows |> List.map Array.length |> List.max

    let colWidths =
        [| 0 .. colCount - 1 |]
        |> Array.map (fun i ->
            formattedRows
            |> List.map (fun row -> if i < row.Length then (stripAnsi row.[i]).Length else 0)
            |> List.max
            |> min 60)

    [ for (rawRow, fmtRow) in List.zip rows formattedRows do
          if isSeparator rawRow then
              let sep =
                  colWidths
                  |> Array.map (fun w -> String('-', w + 2))
                  |> String.concat "+"

              plainLine $"{gray}+{sep}+{reset}"
          else
              let cells =
                  [| 0 .. colCount - 1 |]
                  |> Array.map (fun i ->
                      let formatted = if i < fmtRow.Length then fmtRow.[i] else ""
                      // Measured as displayed, as colWidths is: the raw cell
                      // still holds markup such as a link's target.
                      let pad = max 0 (colWidths.[i] - (stripAnsi formatted).Length)
                      $" {formatted}{String(' ', pad)} ")

              let line = cells |> String.concat $"{gray}|{reset}"
              plainLine $"{gray}|{reset}{line}{gray}|{reset}" ]

/// Render a single normal-state line to a RenderedLine.
let private renderNormalLine (trimmed: string) =
    if trimmed.StartsWith("# ") then
        [ plainLine ""; plainLine $"{bold}{underline}{white}{trimmed.[2..]}{reset}"; plainLine "" ]
    elif trimmed.StartsWith("## ") then
        [ plainLine ""; plainLine $"{boldCyan}{trimmed.[3..]}{reset}" ]
    elif trimmed.StartsWith("### ") then
        [ plainLine ""; plainLine $"{boldWhite}{trimmed.[4..]}{reset}" ]
    elif trimmed.StartsWith("- ") then
        [ plainLine $"  {cyan}•{reset} {formatInline trimmed.[2..]}" ]
    elif trimmed.StartsWith("> [!CAUTION]") then
        [ plainLine $"{boldRed}  CAUTION:{reset} {formatInline (trimmed.[12..].TrimStart())}" ]
    elif trimmed.StartsWith("> [!WARNING]") then
        [ plainLine $"{boldYellow}  WARNING:{reset} {formatInline (trimmed.[12..].TrimStart())}" ]
    elif trimmed.StartsWith("> [!NOTE]") then
        [ plainLine $"{boldBlue}  NOTE:{reset} {formatInline (trimmed.[9..].TrimStart())}" ]
    elif trimmed.StartsWith("> [!TIP]") then
        [ plainLine $"{boldGreen}  TIP:{reset} {formatInline (trimmed.[8..].TrimStart())}" ]
    elif trimmed.StartsWith("> ") then
        [ plainLine $"{gray}  │ {reset}{formatInline trimmed.[2..]}" ]
    elif trimmed.Length = 0 then
        [ plainLine "" ]
    else
        [ plainLine (formatInline trimmed) ]

// ──────────────────────────────────────────────────
// Buffer-based rendering (for interactive browser)
// ──────────────────────────────────────────────────

/// Render markdown content to a line buffer.
let renderToLines (markdown: string) : RenderedLine list =
    let lines = markdown.Split('\n')
    let result = ResizeArray<RenderedLine>()
    let mutable state = Normal
    let mutable tableBuffer: string list = []

    let flushTable () =
        if not tableBuffer.IsEmpty then
            result.AddRange(renderTableToLines (List.rev tableBuffer))
            tableBuffer <- []

    for line in lines do
        let trimmed = line.TrimEnd('\r').TrimEnd()

        match state with
        | InFencedCode lang ->
            if trimmed.StartsWith("```") then
                result.Add(plainLine $"{gray}  {'─' |> string |> String.replicate 40}{reset}")
                state <- Normal
            // A directive to jyro-doc's example checker, not part of the
            // example. `# input:` stays: it tells the reader what Input is.
            elif lang = "jyro" && trimmed = "# fragment" then
                ()
            else
                let color = if lang = "jyro" then green else yellow
                result.Add(plainLine $"{color}  {trimmed}{reset}")

        | InTable ->
            if trimmed.StartsWith("|") then
                tableBuffer <- trimmed :: tableBuffer
            else
                flushTable ()
                state <- Normal

                if trimmed.StartsWith("```") then
                    let lang = trimmed.[3..].Trim()
                    result.Add(plainLine $"{gray}  {'─' |> string |> String.replicate 40}{reset}")
                    state <- InFencedCode lang
                elif trimmed.StartsWith("|") then
                    state <- InTable
                    tableBuffer <- [ trimmed ]
                else
                    result.AddRange(renderNormalLine trimmed)

        | Normal ->
            if trimmed.StartsWith("```") then
                let lang = trimmed.[3..].Trim()
                result.Add(plainLine $"{gray}  {'─' |> string |> String.replicate 40}{reset}")
                state <- InFencedCode lang
            elif trimmed.StartsWith("|") then
                state <- InTable
                tableBuffer <- [ trimmed ]
            else
                result.AddRange(renderNormalLine trimmed)

    flushTable ()
    result |> Seq.toList

/// Build rendered lines for a page, including navigation footer with links.
let renderPage (page: ManIndex.DocPage) : RenderedLine list =
    let content = ManIndex.loadContent page
    let body = renderToLines content
    let sep = $"{gray}{'─' |> string |> String.replicate 50}{reset}"

    let footer =
        [ plainLine ""
          plainLine sep
          plainLine $"{dim}Section:{reset} {page.Breadcrumb}"
          plainLine "" ]

    let siblingLinks =
        page.Siblings
        |> List.map (fun sibling ->
            if sibling.Path = page.Path then
                plainLine $"  {bold}[{sibling.DisplayName}]{reset}"
            else
                linkLine $"  {cyan}{sibling.DisplayName}{reset}" (NavTarget.Page sibling.Path))

    let footerEnd = [ plainLine ""; plainLine sep ]

    body @ footer @ siblingLinks @ footerEnd

/// Build rendered lines for the table of contents with navigation links.
let renderTableOfContentsLines (index: ManIndex.Section list) : RenderedLine list =
    [ plainLine ""
      plainLine $"{bold}{underline}{white}JYRO MANUAL{reset}"
      plainLine ""

      for section in index do
          plainLine $"{boldCyan}{section.DisplayName.ToUpperInvariant()}{reset}"
          plainLine ""

          for sub in section.Subsections do
              linkLine $"  {cyan}{sub.DisplayName}{reset}" (NavTarget.Subsection sub.Path)

          plainLine "" ]

/// Build rendered lines for a subsection index with navigation links.
let renderSubsectionIndexLines (sub: ManIndex.Subsection) (section: ManIndex.Section) : RenderedLine list =
    [ plainLine ""
      plainLine $"{bold}{underline}{white}{sub.DisplayName.ToUpperInvariant()}{reset} {gray}- {section.DisplayName}{reset}"
      plainLine ""

      for page in sub.Pages do
          let summary = ManIndex.getPageSummary page

          let truncated =
              if summary.Length > 60 then summary.[..56] + "..."
              else summary

          linkLine $"  {cyan}{page.DisplayName,-24}{reset} {truncated}" (NavTarget.Page page.Path)

      plainLine ""
      linkLine $"  {gray}← Back{reset}" NavTarget.Back ]

/// Build rendered lines for a section index with navigation links.
let renderSectionIndexLines (section: ManIndex.Section) : RenderedLine list =
    [ plainLine ""
      plainLine $"{bold}{underline}{white}{section.DisplayName.ToUpperInvariant()}{reset}"
      plainLine ""

      for sub in section.Subsections do
          let pageCount = sub.Pages.Length
          linkLine $"  {cyan}{sub.DisplayName,-28}{reset} {gray}({pageCount} pages){reset}" (NavTarget.Subsection sub.Path)

      plainLine ""
      linkLine $"  {gray}← Back{reset}" NavTarget.Back ]

// ──────────────────────────��───────────────────────
// Direct console output (for non-interactive / piped)
// ──────────────────────────────────────────────────

/// Write rendered lines directly to console.
let writeLines (lines: RenderedLine list) =
    for line in lines do
        Console.WriteLine(line.Text)

/// Render markdown content directly to console.
let renderToConsole (markdown: string) =
    renderToLines markdown |> writeLines

/// Print the navigation footer after a page (non-interactive).
let renderNavigationFooter (page: ManIndex.DocPage) =
    let separator = $"{gray}{'─' |> string |> String.replicate 50}{reset}"
    Console.WriteLine()
    Console.WriteLine(separator)
    Console.WriteLine($"{dim}Section:{reset} {page.Breadcrumb}")

    if not page.Siblings.IsEmpty then
        let siblingDisplay =
            page.Siblings
            |> List.map (fun sibling ->
                if sibling.Path = page.Path then $"{bold}[{sibling.DisplayName}]{reset}"
                else sibling.DisplayName)
            |> String.concat $"  {gray}·{reset}  "

        Console.WriteLine($"{dim}Pages:{reset}   {siblingDisplay}")
        Console.WriteLine()

        let hint =
            page.Siblings
            |> List.filter (fun sibling -> sibling.Path <> page.Path)
            |> List.truncate 3
            |> List.map (fun sibling -> $"jyro man {sibling.Query}")
            |> String.concat "  |  "

        if hint.Length > 0 then
            Console.WriteLine($"{gray}{hint}{reset}")

    Console.WriteLine(separator)

/// Render the full table of contents (non-interactive).
let renderTableOfContents (index: ManIndex.Section list) =
    Console.WriteLine()
    Console.WriteLine($"{bold}{underline}{white}JYRO MANUAL{reset}")
    Console.WriteLine()

    for section in index do
        Console.WriteLine($"{boldCyan}{section.DisplayName.ToUpperInvariant()}{reset}")

        let items =
            section.Subsections
            |> List.map (fun sub -> sub.DisplayName)
            |> String.concat $"  {gray}·{reset}  "

        Console.WriteLine($"  {items}")
        Console.WriteLine()

    Console.WriteLine($"{gray}Use 'jyro man <topic>' to view a page. Topics are case-insensitive.{reset}")
    Console.WriteLine($"{gray}Examples: jyro man types  |  jyro man append  |  jyro man string/overview{reset}")
    Console.WriteLine()

/// Render a subsection index (non-interactive).
let renderSubsectionIndex (sub: ManIndex.Subsection) (section: ManIndex.Section) =
    Console.WriteLine()
    Console.WriteLine(
        $"{bold}{underline}{white}{sub.DisplayName.ToUpperInvariant()}{reset} {gray}- {section.DisplayName}{reset}"
    )

    Console.WriteLine()

    for page in sub.Pages do
        let summary = ManIndex.getPageSummary page

        let truncated =
            if summary.Length > 70 then summary.[..66] + "..."
            else summary

        Console.WriteLine($"  {cyan}{page.DisplayName,-28}{reset} {truncated}")

    Console.WriteLine()
    Console.WriteLine($"{gray}Use 'jyro man <name>' to view a specific page.{reset}")
    Console.WriteLine()

/// Render a section index (non-interactive).
let renderSectionIndex (section: ManIndex.Section) =
    Console.WriteLine()
    Console.WriteLine($"{bold}{underline}{white}{section.DisplayName.ToUpperInvariant()}{reset}")
    Console.WriteLine()

    for sub in section.Subsections do
        let pageCount = sub.Pages.Length
        Console.WriteLine($"  {cyan}{sub.DisplayName,-28}{reset} {gray}({pageCount} pages){reset}")

    Console.WriteLine()
    Console.WriteLine($"{gray}Use 'jyro man <subsection>' to browse a subsection.{reset}")
    Console.WriteLine()

/// Render the disambiguation list.
let renderAmbiguous (query: string) (matches: (string * string) list) =
    Console.WriteLine()
    Console.WriteLine($"{boldYellow}Ambiguous topic '{query}'. Did you mean:{reset}")
    Console.WriteLine()

    for (query, breadcrumb) in matches do
        Console.WriteLine($"  {cyan}{query,-28}{reset} {gray}{breadcrumb}{reset}")

    Console.WriteLine()

/// Highlight a line as selected (inverse video).
let highlightLine (line: RenderedLine) =
    $"{inverse}{stripAnsi line.Text}{reset}"
