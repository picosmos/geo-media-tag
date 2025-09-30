module picosmos.geo.MediaTag.BusinessLogic

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open picosmos.geo.MediaTag.Gpx
open picosmos.geo.MediaTag.Exif

type GeotagSummary =
    { Success: int
      Skipped: int
      Total: int }

let private summarize success skipped =
    { Success = success
      Skipped = skipped
      Total = success + skipped }

let private validateGeoFiles (logger: ILogger) (paths: string seq) =
    let missing =
        paths
        |> Seq.filter (fun path -> not (File.Exists path))
        |> Seq.toArray

    if missing.Length > 0 then
        missing |> Array.iter (fun path -> logger.LogError("GPX file not found: {File}", path))
        false
    else
        true

let private loadSupportedMedia (logger: ILogger) (folderPath: string) =
    if String.IsNullOrWhiteSpace folderPath then
        logger.LogError("A media folder path must be provided.")
        []
    elif not (Directory.Exists folderPath) then
        logger.LogError("Media folder not found: {Folder}", folderPath)
        []
    else
        let allFiles =
            folderPath
            |> Directory.EnumerateFiles
            |> Seq.toList

        let supported, unsupported = allFiles |> List.partition isSupportedImage

        unsupported
        |> List.iter (fun file -> logger.LogDebug("Ignoring unsupported media type: {File}", file))

        if supported.IsEmpty then
            logger.LogWarning("No supported media files found in {Folder}", folderPath)

        supported

let private geotagFile (logger: ILogger) (trackPoints: GeoPoint list) (path: string) =
    if not (File.Exists path) then
        logger.LogWarning("Media file not found: {File}", path)
        None
    else
        match tryGetCaptureTime logger path with
        | None ->
            logger.LogWarning("No capture timestamp available for {File}. Skipping.", path)
            None
        | Some timestamp ->
            match locatePoint trackPoints timestamp with
            | Some geoPoint ->
                if updateImageMetadata logger path geoPoint then Some true else None
            | None ->
                logger.LogWarning(
                    "No matching geo data found for {File} at {Timestamp}. Skipping.",
                    path,
                    timestamp)
                None

let geotagMediaFolder (logger: ILogger) (geoPaths: string seq) (mediaFolder: string) : Task<int> =
    task {
        if Seq.isEmpty geoPaths then
            logger.LogError("At least one GPX file must be provided via --geo")
            return 1
        elif not (validateGeoFiles logger geoPaths) then
            return 1
        else
            let trackPoints = geoPaths |> loadTracks logger

            if trackPoints.IsEmpty then
                logger.LogError("No track points found across provided GPX files")
                return 1
            else
                let minTimestamp = trackPoints |> List.minBy (fun p -> p.Timestamp) |> (fun x -> x.Timestamp)
                let maxTimestamp = trackPoints |> List.maxBy (fun p -> p.Timestamp) |> (fun x -> x.Timestamp)
                logger.LogInformation(
                    "Loaded {PointCount} track points from {FileCount} GPX files (from {MinTime} to {MaxTime})",
                    trackPoints.Length,
                    geoPaths |> Seq.length,
                    minTimestamp,
                    maxTimestamp)
                let mediaFiles = loadSupportedMedia logger mediaFolder

                if mediaFiles.IsEmpty then
                    return 1
                else
                    let mutable successCount = 0
                    let mutable skippedCount = 0

                    for mediaPath in mediaFiles do
                        match geotagFile logger trackPoints mediaPath with
                        | Some true -> successCount <- successCount + 1
                        | _ -> skippedCount <- skippedCount + 1

                    let summary = summarize successCount skippedCount

                    logger.LogInformation(
                        "Completed geotagging. Success: {SuccessCount}, Skipped: {SkippedCount}",
                        summary.Success,
                        summary.Skipped)

                    return (if summary.Success > 0 then 0 else 1)
    }
