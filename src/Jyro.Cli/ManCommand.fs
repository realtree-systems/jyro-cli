module RealTree.Jyro.Cli.ManCommand

open System

/// Run the man subcommand with an optional topic query.
let runMan (topic: string option) : int =
    let index = ManIndex.buildIndex ()

    if index.IsEmpty then
        ConsoleOutput.writeError "Manual pages are not available in this build."
        1
    else
        let interactive = not Console.IsOutputRedirected

        match topic with
        | None ->
            if interactive then
                ManBrowser.startFromToc index
                0
            else
                ManPageRenderer.renderTableOfContents index
                0
        | Some query ->
            match ManIndex.resolveQuery query index with
            | ManIndex.PageFound page when interactive ->
                ManBrowser.startFromQuery index (ManIndex.PageFound page)
                0
            | ManIndex.PageFound page ->
                let content = ManIndex.loadContent page
                ManPageRenderer.renderToConsole content
                ManPageRenderer.renderNavigationFooter page
                0
            | ManIndex.SubsectionFound(sub, sec) when interactive ->
                ManBrowser.startFromQuery index (ManIndex.SubsectionFound(sub, sec))
                0
            | ManIndex.SubsectionFound(sub, sec) ->
                ManPageRenderer.renderSubsectionIndex sub sec
                0
            | ManIndex.SectionFound sec when interactive ->
                ManBrowser.startFromQuery index (ManIndex.SectionFound sec)
                0
            | ManIndex.SectionFound sec ->
                ManPageRenderer.renderSectionIndex sec
                0
            | ManIndex.Ambiguous matches ->
                ManPageRenderer.renderAmbiguous query matches
                1
            | ManIndex.NotFound ->
                ConsoleOutput.writeError $"No man page found for '{query}'."
                Console.WriteLine("Use 'jyro man' to list all available topics.")
                1
