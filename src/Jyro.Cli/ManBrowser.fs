module RealTree.Jyro.Cli.ManBrowser

open System
open ManPageRenderer

/// A view that the browser can display.
type private View =
    { Lines: RenderedLine list
      Title: string }

/// Resolve a NavTarget into a View, or None if not found.
let private resolveTarget (index: ManIndex.Section list) (target: NavTarget) : View option =
    match target with
    | NavTarget.Page path ->
        ManIndex.tryFindPage path index
        |> Option.map (fun page -> { Lines = renderPage page; Title = page.DisplayName })
    | NavTarget.Subsection path ->
        ManIndex.tryFindSubsection path index
        |> Option.map (fun (sub, sec) -> { Lines = renderSubsectionIndexLines sub sec; Title = sub.DisplayName })
    | NavTarget.Section slug ->
        ManIndex.tryFindSection slug index
        |> Option.map (fun sec -> { Lines = renderSectionIndexLines sec; Title = sec.DisplayName })
    | NavTarget.Back -> None // handled by caller via stack

/// Build a list of (lineIndex, NavTarget) for all selectable lines in a view.
let private buildLinkMap (lines: RenderedLine list) =
    lines
    |> List.indexed
    |> List.choose (fun (i, line) ->
        match line.Link with
        | Some target -> Some(i, target)
        | None -> None)

let private ansiPattern = System.Text.RegularExpressions.Regex(@"\x1B\[[0-9;]*m", Text.RegularExpressions.RegexOptions.Compiled)
let private eraseToEol = "\x1B[K"

/// Wrap a single ANSI-formatted string into chunks that fit within maxWidth
/// visible columns, breaking at word boundaries when possible. ANSI escape
/// sequences are preserved verbatim but do not count toward visible width.
let private wrapAnsiText (maxWidth: int) (text: string) : string list =
    if maxWidth <= 0 || text.Length = 0 then
        [ text ]
    else
        let chunks = ResizeArray<string>()
        let sb = System.Text.StringBuilder()
        let mutable visibleCol = 0
        let mutable lastSpaceSbIdx = -1
        let mutable i = 0

        let flushAtLastSpace () =
            let content = sb.ToString()
            let chunk = content.Substring(0, lastSpaceSbIdx)
            chunks.Add(chunk + "\x1B[0m")
            // Drop the space, keep everything after it
            let remainder = content.Substring(lastSpaceSbIdx + 1)
            sb.Clear() |> ignore
            sb.Append(remainder) |> ignore
            visibleCol <- (ManPageRenderer.stripAnsi remainder).Length
            lastSpaceSbIdx <- -1

        let hardBreak () =
            chunks.Add(sb.ToString() + "\x1B[0m")
            sb.Clear() |> ignore
            visibleCol <- 0
            lastSpaceSbIdx <- -1

        while i < text.Length do
            if text.[i] = '\x1B' then
                let m = ansiPattern.Match(text, i)

                if m.Success && m.Index = i then
                    sb.Append(m.Value) |> ignore
                    i <- i + m.Length
                else
                    sb.Append(text.[i]) |> ignore
                    visibleCol <- visibleCol + 1
                    i <- i + 1
            else
                if visibleCol >= maxWidth then
                    if lastSpaceSbIdx >= 0 then flushAtLastSpace ()
                    else hardBreak ()

                let ch = text.[i]

                if ch = ' ' && visibleCol > 0 then
                    lastSpaceSbIdx <- sb.Length

                sb.Append(ch) |> ignore
                visibleCol <- visibleCol + 1
                i <- i + 1

        if sb.Length > 0 then
            chunks.Add(sb.ToString() + "\x1B[0m")

        if chunks.Count = 0 then [ "" ] else chunks |> Seq.toList

/// Wrap a list of RenderedLines to fit the given width. Each logical line
/// may produce multiple physical lines; the Link (if any) is kept only on
/// the first physical line of a wrapped group.
let private wrapRenderedLines (maxWidth: int) (lines: RenderedLine list) : RenderedLine list =
    lines
    |> List.collect (fun line ->
        let chunks = wrapAnsiText maxWidth line.Text

        match chunks with
        | [] -> [ { Text = ""; Link = line.Link } ]
        | [ _ ] -> [ { Text = List.head chunks; Link = line.Link } ]
        | first :: rest ->
            { Text = first; Link = line.Link }
            :: (rest |> List.map (fun t -> { Text = t; Link = None })))

/// Paint the viewport to the console using pre-wrapped lines.
let private paint
    (title: string)
    (wrappedLines: RenderedLine array)
    (scrollOffset: int)
    (selectedLinkIdx: int)
    (linkMap: (int * NavTarget) array)
    =
    let height = Console.WindowHeight
    let width = Console.WindowWidth
    let contentHeight = height - 2 // reserve 2 lines for status bar

    // Determine which physical row is selected (if any)
    let selectedLineIdx =
        if selectedLinkIdx >= 0 && selectedLinkIdx < linkMap.Length then
            fst linkMap.[selectedLinkIdx]
        else
            -1

    let visibleEnd = min (scrollOffset + contentHeight) wrappedLines.Length

    for row in 0 .. contentHeight - 1 do
        Console.SetCursorPosition(0, row)
        let lineIdx = scrollOffset + row

        if lineIdx < visibleEnd then
            let line = wrappedLines.[lineIdx]

            let text =
                if lineIdx = selectedLineIdx then highlightLine line
                else line.Text

            Console.Write(text)

        // Erase from cursor to end of line (clears leftover chars from previous paint)
        Console.Write(eraseToEol)

    // Status bar (last 2 lines)
    Console.SetCursorPosition(0, height - 2)

    let scrollPct =
        if wrappedLines.Length <= contentHeight then "All"
        else
            let pct = (float scrollOffset / float (max 1 (wrappedLines.Length - contentHeight))) * 100.0
            $"{int pct}%%"

    let statusText = $" {title}  ({scrollPct})"
    let padded = statusText + String(' ', max 0 (width - statusText.Length - 1))
    Console.Write($"\x1B[7m{padded}\x1B[0m{eraseToEol}")

    Console.SetCursorPosition(0, height - 1)
    let helpText = " \u2191\u2193 scroll  Tab/Shift+Tab links  Enter open  Backspace back  q quit"
    let paddedHelp = helpText + String(' ', max 0 (width - helpText.Length - 1))
    Console.Write($"\x1B[90m{paddedHelp}\x1B[0m{eraseToEol}")

/// Ensure the selected link is visible in the viewport and adjust scroll if needed.
let private ensureVisible (lineIdx: int) (scrollOffset: int) (contentHeight: int) =
    if lineIdx < scrollOffset then lineIdx
    elif lineIdx >= scrollOffset + contentHeight then lineIdx - contentHeight + 1
    else scrollOffset

/// Run the interactive browser starting from an initial view.
let private run (index: ManIndex.Section list) (initialStack: View list) (initialView: View) =
    let mutable running = true
    let mutable viewStack: View list = initialStack
    let mutable currentView = initialView
    let mutable wrappedLines: RenderedLine array = [||]
    let mutable linkMap: (int * NavTarget) array = [||]
    let mutable scrollOffset = 0
    let mutable selectedLinkIdx = -1
    let mutable needsRepaint = true
    let mutable lastWidth = Console.WindowWidth

    let rewrap () =
        let width = max 20 (Console.WindowWidth - 1)
        wrappedLines <- wrapRenderedLines width currentView.Lines |> List.toArray
        linkMap <-
            wrappedLines
            |> Array.indexed
            |> Array.choose (fun (i, line) ->
                match line.Link with
                | Some t -> Some(i, t)
                | None -> None)
        lastWidth <- Console.WindowWidth

    let loadView (newView: View) =
        currentView <- newView
        scrollOffset <- 0
        rewrap ()
        selectedLinkIdx <- if linkMap.Length = 0 then -1 else 0
        Console.Clear()
        needsRepaint <- true

    rewrap ()
    selectedLinkIdx <- if linkMap.Length = 0 then -1 else 0

    // Setup terminal
    let origCursorVisible = Console.CursorVisible
    Console.CursorVisible <- false
    Console.Clear()

    try
        while running do
            // Detect window resize
            if Console.WindowWidth <> lastWidth then
                rewrap ()
                Console.Clear()
                needsRepaint <- true

            if needsRepaint then
                paint currentView.Title wrappedLines scrollOffset selectedLinkIdx linkMap
                needsRepaint <- false

            let key = Console.ReadKey(true)
            let contentHeight = Console.WindowHeight - 2
            let maxScroll = max 0 (wrappedLines.Length - contentHeight)

            match key.Key with
            | ConsoleKey.Q -> running <- false

            | ConsoleKey.Escape
            | ConsoleKey.Backspace ->
                match viewStack with
                | prev :: rest ->
                    viewStack <- rest
                    loadView prev
                | [] -> running <- false

            | ConsoleKey.Enter ->
                if selectedLinkIdx >= 0 && selectedLinkIdx < linkMap.Length then
                    let (_, target) = linkMap.[selectedLinkIdx]

                    match target with
                    | NavTarget.Back ->
                        match viewStack with
                        | prev :: rest ->
                            viewStack <- rest
                            loadView prev
                        | [] -> running <- false
                    | _ ->
                        match resolveTarget index target with
                        | Some newView ->
                            viewStack <- currentView :: viewStack
                            loadView newView
                        | None -> ()

            | ConsoleKey.UpArrow ->
                if scrollOffset > 0 then
                    scrollOffset <- scrollOffset - 1
                    needsRepaint <- true

            | ConsoleKey.DownArrow ->
                if scrollOffset < maxScroll then
                    scrollOffset <- scrollOffset + 1
                    needsRepaint <- true

            | ConsoleKey.PageUp ->
                scrollOffset <- max 0 (scrollOffset - contentHeight)
                needsRepaint <- true

            | ConsoleKey.PageDown ->
                scrollOffset <- min maxScroll (scrollOffset + contentHeight)
                needsRepaint <- true

            | ConsoleKey.Home ->
                scrollOffset <- 0
                needsRepaint <- true

            | ConsoleKey.End ->
                scrollOffset <- maxScroll
                needsRepaint <- true

            | ConsoleKey.Tab ->
                if linkMap.Length > 0 then
                    if key.Modifiers.HasFlag(ConsoleModifiers.Shift) then
                        selectedLinkIdx <-
                            if selectedLinkIdx <= 0 then linkMap.Length - 1
                            else selectedLinkIdx - 1
                    else
                        selectedLinkIdx <-
                            if selectedLinkIdx >= linkMap.Length - 1 then 0
                            else selectedLinkIdx + 1

                    let (lineIdx, _) = linkMap.[selectedLinkIdx]
                    scrollOffset <- ensureVisible lineIdx scrollOffset contentHeight
                    scrollOffset <- min maxScroll (max 0 scrollOffset)
                    needsRepaint <- true

            | _ -> ()
    finally
        Console.CursorVisible <- origCursorVisible
        Console.SetCursorPosition(0, Console.WindowHeight - 1)
        Console.WriteLine()

/// Start the browser from the table of contents.
let startFromToc (index: ManIndex.Section list) =
    let view = { Lines = renderTableOfContentsLines index; Title = "Jyro Manual" }
    run index [] view

/// Start the browser from a specific query result.
let startFromQuery (index: ManIndex.Section list) (result: ManIndex.QueryResult) =
    match result with
    | ManIndex.PageFound page ->
        let tocView = { Lines = renderTableOfContentsLines index; Title = "Jyro Manual" }
        let pageView = { Lines = renderPage page; Title = page.DisplayName }
        run index [ tocView ] pageView
    | ManIndex.SubsectionFound(sub, sec) ->
        let tocView = { Lines = renderTableOfContentsLines index; Title = "Jyro Manual" }
        let view = { Lines = renderSubsectionIndexLines sub sec; Title = sub.DisplayName }
        run index [ tocView ] view
    | ManIndex.SectionFound sec ->
        let tocView = { Lines = renderTableOfContentsLines index; Title = "Jyro Manual" }
        let view = { Lines = renderSectionIndexLines sec; Title = sec.DisplayName }
        run index [ tocView ] view
    | _ -> ()
