module picosmos.geo.MediaTag.Program

open System
open System.IO
open System.CommandLine
open System.CommandLine.Parsing
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open picosmos.geo.MediaTag.BusinessLogic
open System.Globalization
open System.Threading

let private buildRootCommand (logger: ILogger) =
    let expandGeoPaths (paths: string array) =
        let hasWildcards (path: string) = path.IndexOfAny([| '*'; '?' |]) >= 0

        let expandPattern (patternPath: string) =
            let directory = Path.GetDirectoryName(patternPath)
            let searchDir =
                if String.IsNullOrWhiteSpace directory then
                    Directory.GetCurrentDirectory()
                else
                    Path.GetFullPath(directory)

            let searchPattern =
                let fileName = Path.GetFileName(patternPath)
                if String.IsNullOrWhiteSpace fileName then "*" else fileName

            if not (Directory.Exists searchDir) then
                logger.LogWarning("GPX pattern directory not found: {Directory}", searchDir)
                Array.empty
            else
                let matches = Directory.GetFiles(searchDir, searchPattern)

                if matches.Length = 0 then
                    logger.LogWarning(
                        "GPX pattern '{Pattern}' matched no files in {Directory}",
                        patternPath,
                        searchDir)

                matches

        paths
        |> Array.collect (fun path ->
            if hasWildcards path then
                expandPattern path
            else
                [| Path.GetFullPath(path) |])
        |> Array.distinct

    let geoOption: System.CommandLine.Option<string[]> =
        new System.CommandLine.Option<string[]>("--geo", Array.empty)
    geoOption.Description <- "One or more GPX files providing track data"
    geoOption.Required <- true
    geoOption.AllowMultipleArgumentsPerToken <- true
    let folderOption: System.CommandLine.Option<string> =
        new System.CommandLine.Option<string>("--folder", [| "-f" |])
    folderOption.Description <- "Folder containing media files to geotag"
    folderOption.Required <- true

    let root = System.CommandLine.RootCommand("Apply GPX track data to image metadata")
    root.Add(geoOption)
    root.Add(folderOption)

    root.SetAction(fun parseResult ->
        task {
            let geoPaths =
                parseResult.GetValue(geoOption)
                |> expandGeoPaths

            let mediaFolder = parseResult.GetValue(folderOption)
            let resolvedFolder = Path.GetFullPath(mediaFolder)
            return! geotagMediaFolder logger geoPaths resolvedFolder
        })

    root

[<EntryPoint>]
let main argv =
    let culture = CultureInfo.InvariantCulture.Clone() :?> CultureInfo
    culture.DateTimeFormat.ShortDatePattern <- "yyyy-MM-dd"
    culture.DateTimeFormat.LongDatePattern <- "dddd, yyyy-MM-dd"
    Thread.CurrentThread.CurrentCulture <- culture
    Thread.CurrentThread.CurrentUICulture <- culture
    CultureInfo.DefaultThreadCurrentCulture <- culture
    CultureInfo.DefaultThreadCurrentUICulture <- culture

    let run =
        task {
            use loggerFactory =
                LoggerFactory.Create(fun builder ->
                    builder
                        .SetMinimumLevel(LogLevel.Information)
                        .AddSimpleConsole(fun options ->
                            options.SingleLine <- true
                            options.TimestampFormat <- "yyyy-MM-dd HH:mm:ss ")
                        |> ignore)

            let logger = loggerFactory.CreateLogger("geo-media-tag")
            let command = buildRootCommand logger
            let parseResult = command.Parse(argv)
            return! parseResult.InvokeAsync()
        }

    run.GetAwaiter().GetResult()
