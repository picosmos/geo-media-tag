module ImageGeoTag.Exif

open System
open System.IO
open System.Globalization
open ImageMagick
open Microsoft.Extensions.Logging
open ImageGeoTag.Gpx

let private getSupportedFormats () =
    try
        MagickNET.SupportedFormats
        |> Seq.map (fun format -> format.Format.ToString().ToLowerInvariant())
        |> Set.ofSeq
    with
    | _ -> Set.empty

let isSupportedImage (path: string) =
    let extension = Path.GetExtension(path).ToLowerInvariant()
    let basicSupported = [ ".jpg"; ".jpeg"; ".tif"; ".tiff" ]
    let potentiallySupported = [ ".heic" ]
    
    if basicSupported |> List.contains extension then
        true
    elif potentiallySupported |> List.contains extension then
        // Check if ImageMagick actually supports this format
        let supportedFormats = getSupportedFormats()
        let formatName = extension.TrimStart('.')
        supportedFormats.Contains(formatName) || supportedFormats.Contains("heic")
    else
        false

let private toRational (value: float) (denominator: uint32) =
    let numerator = Math.Round(value * float denominator) |> uint32
    Rational(numerator, denominator)

let private toDms (value: float) =
    let absValue = Math.Abs value
    let degrees = Math.Floor absValue
    let minutesFloat = (absValue - degrees) * 60.0
    let minutes = Math.Floor minutesFloat
    let seconds = (minutesFloat - minutes) * 60.0

    let degreesComponent = uint32 degrees
    let minutesComponent = uint32 minutes

    [| Rational(degreesComponent, 1u)
       Rational(minutesComponent, 1u)
       toRational seconds 1000u |]

let private tryGetExifDate (image: MagickImage) =
    match image.GetExifProfile() with
    | null -> None
    | exif ->
        let parseValue (value: IExifValue) =
            let text = value.ToString()
            if String.IsNullOrWhiteSpace text then None else tryParseDateTime text

        let tryTag tag =
            match exif.GetValue(tag) with
            | null -> None
            | value -> parseValue value

        match tryTag ExifTag.DateTimeOriginal with
        | Some result -> Some result
        | None ->
            match tryTag ExifTag.DateTimeDigitized with
            | Some result -> Some result
            | None -> tryTag ExifTag.DateTime

let tryGetCaptureTime (path: string) =
    try
        use image = new MagickImage(path)

        match tryGetExifDate image with
        | Some timestamp -> Some timestamp
        | None ->
            let lastWrite = File.GetLastWriteTimeUtc(path)
            Some(lastWrite)
    with
    | :? MagickCorruptImageErrorException as ex ->
        // HEIC files may not be supported by this ImageMagick build
        eprintfn "Error reading image %s: %s" path ex.Message
        None
    | _ -> None

let updateImageMetadata (logger: ILogger) (path: string) (geoPoint: GeoPoint) =
    try
        use image = new MagickImage(path)

        let latitudeRef = if geoPoint.Latitude >= 0.0 then "N" else "S"
        let longitudeRef = if geoPoint.Longitude >= 0.0 then "E" else "W"

        let mutable exifProfile = image.GetExifProfile()
        if isNull exifProfile then
            exifProfile <- new ExifProfile()

        exifProfile.SetValue(ExifTag.GPSLatitude, toDms geoPoint.Latitude)
        exifProfile.SetValue(ExifTag.GPSLatitudeRef, latitudeRef)
        exifProfile.SetValue(ExifTag.GPSLongitude, toDms geoPoint.Longitude)
        exifProfile.SetValue(ExifTag.GPSLongitudeRef, longitudeRef)
        exifProfile.SetValue(ExifTag.GPSMapDatum, "WGS-84")
        exifProfile.SetValue(ExifTag.GPSVersionID, [| byte 2; byte 3; byte 0; byte 0 |])

        let utcTime = geoPoint.Timestamp.ToUniversalTime()
        exifProfile.SetValue(ExifTag.GPSDateStamp, utcTime.ToString("yyyy:MM:dd", CultureInfo.InvariantCulture))
        exifProfile.SetValue(
            ExifTag.GPSTimestamp,
            [| Rational(uint32 utcTime.Hour, 1u)
               Rational(uint32 utcTime.Minute, 1u)
               toRational (float utcTime.Second + float utcTime.Millisecond / 1000.0) 1000u |]
        )

        match geoPoint.Elevation with
        | Some elevation ->
            let altitudeRef = if elevation >= 0.0 then byte 0 else byte 1
            exifProfile.SetValue(ExifTag.GPSAltitudeRef, altitudeRef)
            exifProfile.SetValue(ExifTag.GPSAltitude, toRational (Math.Abs elevation) 100u)
        | None -> ()

        image.SetProfile(exifProfile)
        
        // Handle HEIC files specially - convert to JPEG if writing back as HEIC fails
        let extension = Path.GetExtension(path).ToLowerInvariant()
        if extension = ".heic" then
            try
                // Try to write as HEIC first
                image.Write(path)
                logger.LogInformation("Geotagged {File} @ ({Latitude}, {Longitude})", path, geoPoint.Latitude, geoPoint.Longitude)
            with
            | :? MagickMissingDelegateErrorException ->
                // Fall back to creating a JPEG version
                let jpegPath = Path.ChangeExtension(path, ".jpg")
                image.Format <- MagickFormat.Jpeg
                image.Write(jpegPath)
                logger.LogInformation("Geotagged {File} @ ({Latitude}, {Longitude}) - saved as {JpegFile}", path, geoPoint.Latitude, geoPoint.Longitude, jpegPath)
        else
            image.Write(path)
            logger.LogInformation("Geotagged {File} @ ({Latitude}, {Longitude})", path, geoPoint.Latitude, geoPoint.Longitude)
        
        true
    with 
    | :? MagickCorruptImageErrorException as ex ->
        logger.LogError("Image format not supported or corrupted: {File} - {Message}", path, ex.Message)
        false
    | ex ->
        logger.LogError(ex, "Failed to update metadata for {File}", path)
        false
