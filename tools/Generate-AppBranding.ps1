# Package-sized renditions of the supplied Watchstream artwork.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\src\StremioXboxPrototype\Assets'
function Export-BrandAsset([string]$source, [string]$name, [int]$width, [int]$height) {
    $image = [System.Drawing.Image]::FromFile((Join-Path $assets $source))
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#131315'))
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $scale = [Math]::Min($width / $image.Width, $height / $image.Height)
        $w = [int]($image.Width * $scale)
        $h = [int]($image.Height * $scale)
        $graphics.DrawImage($image, [int](($width - $w) / 2), [int](($height - $h) / 2), $w, $h)
        $bitmap.Save((Join-Path $assets $name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose(); $image.Dispose() }
}
Export-BrandAsset 'WatchstreamVisual.png' 'Square44x44Logo.scale-200.png' 88 88
Export-BrandAsset 'WatchstreamVisual.png' 'Square150x150Logo.scale-200.png' 300 300
Export-BrandAsset 'WatchstreamVisual.png' 'StoreLogo.png' 50 50
Export-BrandAsset 'WatchstreamSplashSource.png' 'SplashScreen.scale-200.png' 1240 600
