Add-Type -AssemblyName System.Drawing

# Sizes for the .ico (multi-size icon)
$sizes = @(16, 32, 48, 64, 128, 256)
$bitmaps = @()

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    # Background gradient (deep blue → cyan)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 30, 60, 130),
        [System.Drawing.Color]::FromArgb(255, 0, 140, 200),
        45
    )
    # Rounded rect background
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = [Math]::Max(2, [int]($size * 0.15))
    $path.AddArc(0, 0, $r*2, $r*2, 180, 90)
    $path.AddArc($size - $r*2, 0, $r*2, $r*2, 270, 90)
    $path.AddArc($size - $r*2, $size - $r*2, $r*2, $r*2, 0, 90)
    $path.AddArc(0, $size - $r*2, $r*2, $r*2, 90, 90)
    $path.CloseFigure()
    $g.FillPath($brush, $path)

    # Draw "log" lines - three horizontal bars
    $padding = [int]($size * 0.20)
    $lineHeight = [Math]::Max(1, [int]($size * 0.10))
    $lineSpacing = [int]($size * 0.16)
    $lineWidthFull = $size - $padding * 2
    $startY = [int]($size * 0.30)

    $lineBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $accentBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 252, 211, 91))

    # Bar 1 (accent yellow - warning indicator)
    $g.FillRectangle($accentBrush, $padding, $startY, [int]($lineWidthFull * 0.7), $lineHeight)
    # Bar 2 (white)
    $g.FillRectangle($lineBrush, $padding, $startY + $lineSpacing, $lineWidthFull, $lineHeight)
    # Bar 3 (white, shorter)
    $g.FillRectangle($lineBrush, $padding, $startY + $lineSpacing * 2, [int]($lineWidthFull * 0.85), $lineHeight)

    $g.Dispose()
    $bitmaps += $bmp
}

# Save largest as PNG too (for reference)
$bitmaps[5].Save("$PSScriptRoot\Assets\AppIcon.png", [System.Drawing.Imaging.ImageFormat]::Png)

# Write multi-size .ico file manually
$icoPath = "$PSScriptRoot\Assets\AppIcon.ico"
$stream = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($stream)

# ICONDIR header
$writer.Write([UInt16]0)       # Reserved
$writer.Write([UInt16]1)       # Type: 1 = icon
$writer.Write([UInt16]$sizes.Count)  # Number of images

# Convert bitmaps to PNG byte arrays first to compute offsets
$pngData = @()
foreach ($bmp in $bitmaps) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngData += , $ms.ToArray()
}

# ICONDIRENTRY entries
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $writer.Write([Byte]($sz % 256))     # Width (0 = 256)
    $writer.Write([Byte]($sz % 256))     # Height
    $writer.Write([Byte]0)                # Color count
    $writer.Write([Byte]0)                # Reserved
    $writer.Write([UInt16]1)              # Color planes
    $writer.Write([UInt16]32)             # Bits per pixel
    $writer.Write([UInt32]$pngData[$i].Length)  # Image size
    $writer.Write([UInt32]$offset)        # Offset
    $offset += $pngData[$i].Length
}

# Image data
foreach ($data in $pngData) {
    $writer.Write($data)
}

$writer.Close()
$stream.Close()

foreach ($bmp in $bitmaps) { $bmp.Dispose() }

Write-Host "Icon generated at $icoPath"
