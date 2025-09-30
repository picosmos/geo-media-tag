module picosmos.geo.MediaTag.Gpx

open System
open System.Globalization
open System.Xml.Linq
open Microsoft.Extensions.Logging

type GeoPoint =
    { Timestamp: DateTime
      Latitude: float
      Longitude: float
      Elevation: float option }

let private gpxNamespaces =
    [ "http://www.topografix.com/GPX/1/1"
      "http://www.topografix.com/GPX/1/0"
      "" ]

let tryParseDateTime (value: string) =
    let mutable timestamp = DateTime.MinValue

    if
        DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal,
            &timestamp
        )
    then
        Some timestamp
    elif DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, &timestamp) then
        Some timestamp
    elif DateTime.TryParseExact(value, "yyyy':'MM':'dd HH':'mm':'ss", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, &timestamp) then
        Some timestamp
    elif DateTime.TryParseExact(value, "yyyy':'MM':'dd HH':'mm", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, &timestamp) then
        Some timestamp
    else
        None

let private tryParseDouble (value: string) =
    match Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, result -> Some result
    | _ -> None

let private interpolate (beforePoint: GeoPoint) (afterPoint: GeoPoint) (timestamp: DateTime) =
    let totalSeconds = (afterPoint.Timestamp - beforePoint.Timestamp).TotalSeconds
    if Math.Abs totalSeconds < 0.000001 then
        beforePoint
    else
        let deltaSeconds = (timestamp - beforePoint.Timestamp).TotalSeconds
        let ratio = deltaSeconds / totalSeconds

        { Timestamp = timestamp
          Latitude = beforePoint.Latitude + ratio * (afterPoint.Latitude - beforePoint.Latitude)
          Longitude = beforePoint.Longitude + ratio * (afterPoint.Longitude - beforePoint.Longitude)
          Elevation =
            match beforePoint.Elevation, afterPoint.Elevation with
            | Some a, Some b -> Some(a + ratio * (b - a))
            | Some value, _ -> Some value
            | _, Some value -> Some value
            | _ -> None }

let loadTrackPoints (logger: ILogger) (filePath: string) : GeoPoint list =
    try
        let doc = XDocument.Load(filePath)

        let tryGetTime (elem: XElement) =
            match elem.Element(elem.Name.Namespace + "time") with
            | null -> None
            | timeElem ->
                match tryParseDateTime timeElem.Value with
                | Some dto -> Some dto
                | None ->
                    logger.LogWarning("Could not parse GPX time '{Time}' in {File}", timeElem.Value, filePath)
                    None

        let rec collectPoints (ns: XNamespace option) =
            let nameFor name =
                match ns with
                | Some nsValue -> nsValue + name
                | None -> XName.Get(name)

            doc.Descendants(nameFor "trkpt")
            |> Seq.choose (fun trkpt ->
                let latAttr = trkpt.Attribute(XName.Get "lat")
                let lonAttr = trkpt.Attribute(XName.Get "lon")

                match latAttr, lonAttr with
                | null, _
                | _, null -> None
                | latAttr, lonAttr ->
                    match tryParseDouble latAttr.Value, tryParseDouble lonAttr.Value with
                    | Some lat, Some lon ->
                        match tryGetTime trkpt with
                        | Some timestamp ->
                            let elevation =
                                match trkpt.Element(nameFor "ele") with
                                | null -> None
                                | eleElem -> tryParseDouble eleElem.Value

                            Some
                                { Timestamp = timestamp
                                  Latitude = lat
                                  Longitude = lon
                                  Elevation = elevation }
                        | None -> None
                    | _ -> None)
            |> Seq.toList

        let candidates =
            gpxNamespaces
            |> List.map (fun nsUri -> if String.IsNullOrWhiteSpace nsUri then None else Some(XNamespace.Get(nsUri)))
            |> List.map collectPoints
            |> List.maxBy (fun points -> points.Length)

        if candidates.IsEmpty then
            logger.LogWarning("No track points found in GPX file {File}", filePath)

        candidates |> List.sortBy (fun p -> p.Timestamp)
    with ex ->
        logger.LogError(ex, "Failed to read GPX file {File}", filePath)
        []

let loadTracks (logger: ILogger) (filePaths: seq<string>) : GeoPoint list =
    filePaths
    |> Seq.collect (fun path -> loadTrackPoints logger path)
    |> Seq.sortBy (fun p -> p.Timestamp)
    |> Seq.toList

let locatePoint (points: GeoPoint list) (timestamp: DateTime) =
    let rec loop previous remaining =
        match previous, remaining with
        | None, [] -> None
        | Some _, [] -> None
        | None, current :: rest ->
            if timestamp < current.Timestamp then None
            elif timestamp = current.Timestamp then Some current
            else loop (Some current) rest
        | Some prev, current :: rest ->
            if timestamp = current.Timestamp then Some current
            elif timestamp < current.Timestamp then Some(interpolate prev current timestamp)
            else loop (Some current) rest

    match points with
    | [] -> None
    | first :: _ ->
        if timestamp < first.Timestamp then None
        elif timestamp > (List.last points).Timestamp then None
        else loop None points
