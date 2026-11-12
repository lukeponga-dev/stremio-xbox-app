# Rasterizes the geometry and palette in stremio-logo-800px.svg for UWP package assets.
Add-Type -AssemblyName System.Drawing

$assetRoot = Join-Path $PSScriptRoot '..\src\StremioXboxPrototype\Assets'
$icon = [System.Drawing.Bitmap]::new(800, 800)
try {
    $drawing = [System.Drawing.Graphics]::FromImage($icon)
    try {
        $drawing.Clear([System.Drawing.Color]::Transparent)
        $drawing.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $drawing.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $diamond = [System.Drawing.Drawing2D.GraphicsPath]::new()
        try {
            $x = 102.55; $y = 102.55; $width = 594.89; $height = 594.89; $diameter = 99.72
            $diamond.AddArc($x, $y, $diameter, $diameter, 180, 90)
            $diamond.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
            $diamond.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
            $diamond.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
            $diamond.CloseFigure()
            $rotation = [System.Drawing.Drawing2D.Matrix]::new()
            try {
                $rotation.RotateAt(45, [System.Drawing.PointF]::new(400, 400))
                $diamond.Transform($rotation)
            }
            finally { $rotation.Dispose() }

            $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                [System.Drawing.PointF]::new(150, 750), [System.Drawing.PointF]::new(650, 50),
                [System.Drawing.Color]::FromArgb(17, 85, 217), [System.Drawing.Color]::FromArgb(123, 91, 245))
            try {
                $blend = [System.Drawing.Drawing2D.ColorBlend]::new()
                $blend.Positions = [single[]]@(0, 0.07, 0.39, 0.54, 0.76, 1)
                $blend.Colors = [System.Drawing.Color[]]@(
                    [System.Drawing.ColorTranslator]::FromHtml('#1155d9'),
                    [System.Drawing.ColorTranslator]::FromHtml('#1c55dc'),
                    [System.Drawing.ColorTranslator]::FromHtml('#5058eb'),
                    [System.Drawing.ColorTranslator]::FromHtml('#655af1'),
                    [System.Drawing.ColorTranslator]::FromHtml('#7b5bf5'),
                    [System.Drawing.ColorTranslator]::FromHtml('#7b5bf5'))
                $gradient.InterpolationColors = $blend
                $drawing.FillPath($gradient, $diamond)
            }
            finally { $gradient.Dispose() }
        }
        finally { $diamond.Dispose() }

        $play = [System.Drawing.Drawing2D.GraphicsPath]::new()
        try {
            $play.StartFigure()
            $play.AddLine(537.71, 390.84, 358.31, 266.15)
            $play.AddBezier(358.31, 266.15, 349.12, 259.77, 336.55, 266.34, 336.55, 277.53)
            $play.AddLine(336.55, 277.53, 336.55, 526.91)
            $play.AddBezier(336.55, 526.91, 336.55, 538.10, 349.12, 544.67, 358.31, 538.29)
            $play.AddLine(358.31, 538.29, 537.71, 413.60)
            $play.AddBezier(537.71, 413.60, 545.64, 408.09, 545.64, 396.36, 537.71, 390.84)
            $play.CloseFigure()
            $drawing.FillPath([System.Drawing.Brushes]::White, $play)
        }
        finally { $play.Dispose() }
    }
    finally { $drawing.Dispose() }

    $tiles = @(
        @{ Name = 'Square44x44Logo.scale-200.png'; Width = 88; Height = 88; Icon = 78 },
        @{ Name = 'Square150x150Logo.scale-200.png'; Width = 300; Height = 300; Icon = 252 },
        @{ Name = 'StoreLogo.png'; Width = 300; Height = 300; Icon = 252 },
        @{ Name = 'Wide310x150Logo.scale-200.png'; Width = 620; Height = 300; Icon = 252 },
        @{ Name = 'SplashScreen.scale-200.png'; Width = 1240; Height = 600; Icon = 360 }
    )
    foreach ($tile in $tiles) {
        $bitmap = [System.Drawing.Bitmap]::new($tile.Width, $tile.Height)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::FromArgb(8, 10, 18))
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $left = [int](($tile.Width - $tile.Icon) / 2)
                $top = [int](($tile.Height - $tile.Icon) / 2)
                $graphics.DrawImage($icon, $left, $top, $tile.Icon, $tile.Icon)
            }
            finally { $graphics.Dispose() }
            $bitmap.Save((Join-Path $assetRoot $tile.Name), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bitmap.Dispose() }
    }
}
finally { $icon.Dispose() }
