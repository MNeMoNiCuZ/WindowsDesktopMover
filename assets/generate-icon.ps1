# Pack the approved PNG frames into multi-size ICOs. No extra tools required.
$ErrorActionPreference = 'Stop'
$colors = @('orange', 'azure', 'cyan-teal', 'emerald', 'violet', 'periwinkle', 'coral', 'magenta', 'amber', 'silver')
$sizes = @(16, 20, 24, 32, 48, 64, 96, 128, 256)

foreach ($color in $colors) {
    $frames = foreach ($size in $sizes) {
        $path = Join-Path $PSScriptRoot "icons/$color/$size.png"
        @{ Size = $size; Bytes = [System.IO.File]::ReadAllBytes($path) }
    }
    $file = [System.IO.File]::Create((Join-Path $PSScriptRoot "icons/$color.ico"))
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally {
        $writer.Dispose()
    }
}

# Silver is the executable icon and the default tray color.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'icons/silver.ico') -Destination (Join-Path $PSScriptRoot 'app.ico') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'icons/silver/256.png') -Destination (Join-Path $PSScriptRoot 'app.png') -Force
