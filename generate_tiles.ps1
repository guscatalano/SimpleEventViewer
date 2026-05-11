Add-Type -AssemblyName System.Drawing

function New-AppIcon([int]$width, [int]$height, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $sq = [Math]::Min($width, $height)
    $offX = ($width - $sq) / 2
    $offY = ($height - $sq) / 2

    # Background gradient
    $rect = New-Object System.Drawing.Rectangle($offX, $offY, $sq, $sq)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 30, 60, 130),
        [System.Drawing.Color]::FromArgb(255, 0, 140, 200),
        45
    )
    $r = [int]($sq * 0.15)
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.AddArc($offX, $offY, $r*2, $r*2, 180, 90)
    $gp.AddArc($offX + $sq - $r*2, $offY, $r*2, $r*2, 270, 90)
    $gp.AddArc($offX + $sq - $r*2, $offY + $sq - $r*2, $r*2, $r*2, 0, 90)
    $gp.AddArc($offX, $offY + $sq - $r*2, $r*2, $r*2, 90, 90)
    $gp.CloseFigure()
    $g.FillPath($brush, $gp)

    # Log bars
    $padding = [int]($sq * 0.20)
    $lineHeight = [Math]::Max(1, [int]($sq * 0.10))
    $lineSpacing = [int]($sq * 0.16)
    $lineWidthFull = $sq - $padding * 2
    $startY = $offY + [int]($sq * 0.30)
    $startX = $offX + $padding

    $lineBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $accentBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 252, 211, 91))

    $g.FillRectangle($accentBrush, $startX, $startY, [int]($lineWidthFull * 0.7), $lineHeight)
    $g.FillRectangle($lineBrush, $startX, $startY + $lineSpacing, $lineWidthFull, $lineHeight)
    $g.FillRectangle($lineBrush, $startX, $startY + $lineSpacing * 2, [int]($lineWidthFull * 0.85), $lineHeight)

    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$assets = "$PSScriptRoot\Assets"

# Generate replacements for all existing PNG assets - keep their dimensions
New-AppIcon 200 200 "$assets\LockScreenLogo.scale-200.png"
New-AppIcon 1240 600 "$assets\SplashScreen.scale-200.png"
New-AppIcon 300 300 "$assets\Square150x150Logo.scale-200.png"
New-AppIcon 88 88 "$assets\Square44x44Logo.scale-200.png"
New-AppIcon 24 24 "$assets\Square44x44Logo.targetsize-24_altform-unplated.png"
New-AppIcon 48 48 "$assets\Square44x44Logo.targetsize-48_altform-lightunplated.png"
New-AppIcon 50 50 "$assets\StoreLogo.png"
New-AppIcon 620 300 "$assets\Wide310x150Logo.scale-200.png"

Write-Host "Tile images generated"
