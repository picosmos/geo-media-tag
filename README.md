# ImageGeoTag

ImageGeoTag is a quick-and-dirty F# CLI tool for geotagging images using GPX track data. It matches image timestamps with GPS coordinates from GPX files and embeds the location data into image EXIF metadata.

**Note:** This tool was mostly generated using GitHub Copilot to quickly create a functional solution for batch geotagging images.

## What it does

- Reads GPX files containing GPS track data with timestamps
- Scans image folders for supported image formats
- Matches image capture times with GPS coordinates from tracks
- Embeds GPS data (latitude, longitude, altitude) into image EXIF metadata
- Supports JPEG and TIFF

## Prerequisites

- .NET 6.0 SDK or later
- F# support (included with .NET SDK)

## Setup

1. **Clone or download the project:**
   ```bash
   git clone <repository-url>
   cd ImageGeoTag
   ```

2. **Restore NuGet packages:**
   ```bash
   dotnet restore
   ```

3. **Build the project:**
   ```bash
   dotnet build
   ```

## Usage

The CLI takes GPX files and a folder containing images to geotag:

```bash
dotnet run -- --geo <path-to-gpx-file> --media <path-to-image-folder>
```

### Examples

**Single GPX file:**
```bash
dotnet run -- --geo ./tracks/activity.gpx --media ./photos/
```

**Multiple GPX files:**
```bash
dotnet run -- --geo ./tracks/day1.gpx --geo ./tracks/day2.gpx --media ./photos/
```

### Command Line Options

- `--geo` (required): Path to GPX file(s) containing GPS track data. Can be specified multiple times.
- `--media` (required): Path to folder containing images to geotag.

## Supported Formats

- **Input images:** JPEG (.jpg, .jpeg), TIFF (.tif, .tiff)
- **GPX files:** Standard GPX format with track points and timestamps
- **Output:** Same format as input

## How it works

1. Loads all GPS track points from provided GPX files
2. Scans the media folder for supported image files
3. For each image:
   - Extracts the capture timestamp from EXIF data
   - Finds the closest GPS coordinate in time from the track data
   - Embeds GPS coordinates, altitude, and timestamp into EXIF metadata
4. Reports success/failure statistics

## Notes

- Images without valid timestamps are skipped
- Images with timestamps outside the GPX track time range are skipped
- This is a quick utility tool generated mostly via Copilot - use appropriate caution for important image collections
- Always backup your images before batch processing

## Dependencies

- **Magick.NET:** Image processing and EXIF manipulation
- **System.CommandLine:** CLI parsing
- **Microsoft.Extensions.Logging:** Logging framework
