param([string]$ProjectRoot = "C:\Users\lukeg\source\repos\stremio-xbox-prototype\stremio-xbox-prototype")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$project = Join-Path $ProjectRoot "src\StremioXboxPrototype"
$assets = Join-Path $project "Assets"
$store = Join-Path $ProjectRoot "store-assets"
$legacy = Join-Path $ProjectRoot "StoreAssets"
$backup = Join-Path $env:TEMP ("OpenStreamTV-Rebrand-Backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item $assets (Join-Path $backup "Assets") -Recurse
Copy-Item $store (Join-Path $backup "store-assets") -Recurse
Copy-Item $legacy (Join-Path $backup "StoreAssets") -Recurse
Write-Output "Backup: $backup"

function New-RoundedPath([single]$x,[single]$y,[single]$w,[single]$h,[single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2*$r
    $p.AddArc($x,$y,$d,$d,180,90); $p.AddArc(($x+$w-$d),$y,$d,$d,270,90)
    $p.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90); $p.AddArc($x,($y+$h-$d),$d,$d,90,90)
    $p.CloseFigure(); return $p
}
function New-BrandGradient([System.Drawing.RectangleF]$rect) {
    $b = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,[System.Drawing.Color]::Magenta,[System.Drawing.Color]::Cyan,0)
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend
    $blend.Colors = @([System.Drawing.Color]::FromArgb(255,214,76,255),[System.Drawing.Color]::FromArgb(255,117,75,255),[System.Drawing.Color]::FromArgb(255,37,118,255),[System.Drawing.Color]::FromArgb(255,35,226,255))
    $blend.Positions = @(0.0,0.34,0.68,1.0); $b.InterpolationColors = $blend; return $b
}
function Draw-OpenStreamMark([System.Drawing.Graphics]$g,[single]$x,[single]$y,[single]$size) {
    $state=$g.Save(); $g.TranslateTransform($x,$y); $g.ScaleTransform($size/1000.0,$size/1000.0)
    $g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $outer=New-RoundedPath 115 120 770 760 210; $outerRect=New-Object System.Drawing.RectangleF(115,120,770,760)
    $glowBrush=New-BrandGradient $outerRect; $glowPen=New-Object System.Drawing.Pen($glowBrush,145); $glowPen.LineJoin=[System.Drawing.Drawing2D.LineJoin]::Round; $g.DrawPath($glowPen,$outer)
    $mainBrush=New-BrandGradient $outerRect; $mainPen=New-Object System.Drawing.Pen($mainBrush,76); $mainPen.LineJoin=[System.Drawing.Drawing2D.LineJoin]::Round; $g.DrawPath($mainPen,$outer)
    $inner=New-RoundedPath 300 285 400 430 135; $innerRect=New-Object System.Drawing.RectangleF(300,285,400,430)
    $innerBrush=New-BrandGradient $innerRect; $innerPen=New-Object System.Drawing.Pen($innerBrush,48); $innerPen.LineJoin=[System.Drawing.Drawing2D.LineJoin]::Round; $g.DrawPath($innerPen,$inner)
    $beamRect=New-Object System.Drawing.RectangleF(330,485,340,30); $beamBrush=New-BrandGradient $beamRect
    $g.FillRectangle($beamBrush,330,490,340,20); $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,235,249,255))),486,486,28,28)
    $g.Restore($state)
    $outer.Dispose();$inner.Dispose();$glowPen.Dispose();$mainPen.Dispose();$innerPen.Dispose();$glowBrush.Dispose();$mainBrush.Dispose();$innerBrush.Dispose();$beamBrush.Dispose()
}
function New-BrandBitmap([int]$w,[int]$h,[string]$kind) {
    $bmp=New-Object System.Drawing.Bitmap($w,$h,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g=[System.Drawing.Graphics]::FromImage($bmp);$g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint=[System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit;$g.Clear([System.Drawing.Color]::Transparent)
    if($kind -eq "wide") {
        Draw-OpenStreamMark $g 8 ($h*0.12) ($h*0.76)
        $font=New-Object System.Drawing.Font("Segoe UI Semibold",[single]($h*0.145),[System.Drawing.FontStyle]::Bold,[System.Drawing.GraphicsUnit]::Pixel)
        $g.DrawString("OpenStream TV",$font,[System.Drawing.Brushes]::White,([single]($h*0.88)),([single]($h*0.38)));$font.Dispose()
    } elseif($kind -eq "splash") {
        $g.Clear([System.Drawing.Color]::FromArgb(255,8,10,18));$mark=[single]($h*0.64)
        $left=[single](($w-($mark+($h*1.2)))/2);Draw-OpenStreamMark $g $left ([single](($h-$mark)/2)) $mark
        $font=New-Object System.Drawing.Font("Segoe UI Semibold",[single]($h*0.145),[System.Drawing.FontStyle]::Bold,[System.Drawing.GraphicsUnit]::Pixel)
        $sub=New-Object System.Drawing.Font("Segoe UI",[single]($h*0.055),[System.Drawing.FontStyle]::Regular,[System.Drawing.GraphicsUnit]::Pixel)
        $g.DrawString("OpenStream TV",$font,[System.Drawing.Brushes]::White,($left+$mark+($h*0.12)),($h*0.39))
        $sb=New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,151,178,220))
        $g.DrawString("DISCOVER  â€¢  CONNECT  â€¢  WATCH",$sub,$sb,($left+$mark+($h*0.13)),($h*0.59));$font.Dispose();$sub.Dispose();$sb.Dispose()
    } else {
        $mark=[single]([Math]::Min($w,$h)*0.82);Draw-OpenStreamMark $g ([single](($w-$mark)/2)) ([single](($h-$mark)/2)) $mark
    }
    $g.Dispose();return $bmp
}
Get-ChildItem $assets -File -Filter "*.png" | ForEach-Object {
    $name=$_.Name;$old=[System.Drawing.Image]::FromFile($_.FullName);$w=$old.Width;$h=$old.Height;$old.Dispose()
    $kind="icon";if($name -like "Wide310x150Logo*"){$kind="wide"}elseif($name -like "SplashScreen*"){$kind="splash"}
    $bmp=New-BrandBitmap $w $h $kind;$bmp.Save($_.FullName,[System.Drawing.Imaging.ImageFormat]::Png);$bmp.Dispose()
    Write-Output "Updated app asset $name $($w)x$($h)"
}
$svg=@'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1000 1000"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#D64CFF"/><stop offset=".38" stop-color="#754BFF"/><stop offset=".72" stop-color="#2576FF"/><stop offset="1" stop-color="#23E2FF"/></linearGradient></defs><g fill="none" stroke="url(#g)" stroke-linecap="round" stroke-linejoin="round"><rect x="153" y="158" width="694" height="684" rx="190" stroke-width="76"/><rect x="326" y="323" width="348" height="354" rx="126" stroke-width="48"/><path d="M350 500h300" stroke-width="22"/></g><circle cx="500" cy="500" r="14" fill="#EBF9FF"/></svg>
'@
Set-Content (Join-Path $assets "MediaBrowserMark.svg") $svg -Encoding UTF8

function New-PromoArt([string]$path,[int]$w,[int]$h,[string]$kind) {
    $bmp=New-Object System.Drawing.Bitmap($w,$h,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb);$g=[System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias;$g.TextRenderingHint=[System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $rect=New-Object System.Drawing.Rectangle(0,0,$w,$h);$bg=New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,[System.Drawing.Color]::FromArgb(255,7,9,22),[System.Drawing.Color]::FromArgb(255,20,25,61),35)
    $g.FillRectangle($bg,$rect);$glow=New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(35,119,70,255));$g.FillEllipse($glow,([single]($w*.14)),([single]($h*.12)),([single]($w*.72)),([single]($h*.72)))
    if($kind -eq "square"){$mark=[single]($w*.46);Draw-OpenStreamMark $g ([single](($w-$mark)/2)) ([single]($h*.10)) $mark;$ts=[single]($w*.078);$ss=[single]($w*.023);$ty=[single]($h*.66);$sy=[single]($h*.76)}
    else{$mark=[single]($w*.56);Draw-OpenStreamMark $g ([single](($w-$mark)/2)) ([single]($h*.13)) $mark;$ts=[single]($w*.09);$ss=[single]($w*.035);$ty=[single]($h*.61);$sy=[single]($h*.70)}
    $tf=New-Object System.Drawing.Font("Segoe UI Semibold",$ts,[System.Drawing.FontStyle]::Bold,[System.Drawing.GraphicsUnit]::Pixel);$sf=New-Object System.Drawing.Font("Segoe UI",$ss,[System.Drawing.FontStyle]::Regular,[System.Drawing.GraphicsUnit]::Pixel)
    $tb=New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White);$sb=New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,170,190,235));$fmt=New-Object System.Drawing.StringFormat;$fmt.Alignment=[System.Drawing.StringAlignment]::Center
    $g.DrawString("OpenStream TV",$tf,$tb,(New-Object System.Drawing.RectangleF(0,$ty,$w,($ts*1.5))),$fmt);$g.DrawString("DISCOVER  â€¢  CONNECT  â€¢  WATCH",$sf,$sb,(New-Object System.Drawing.RectangleF(0,$sy,$w,($ss*1.8))),$fmt)
    $g.Dispose();$bg.Dispose();$glow.Dispose();$tf.Dispose();$sf.Dispose();$tb.Dispose();$sb.Dispose();$fmt.Dispose();$bmp.Save($path,[System.Drawing.Imaging.ImageFormat]::Png);$bmp.Dispose()
    Write-Output "Updated promotional artwork $path"
}
New-PromoArt (Join-Path $store "box-art-1080x1080.png") 1080 1080 "square"
New-PromoArt (Join-Path $store "box-art-1080x1080-v2.png") 1080 1080 "square"
New-PromoArt (Join-Path $store "brand-key-art.png") 1254 1254 "square"
New-PromoArt (Join-Path $store "branded-key-art-584x800.png") 584 800 "poster"
New-PromoArt (Join-Path $store "poster-720x1080.png") 720 1080 "poster"
New-PromoArt (Join-Path $store "poster-720x1080-v2.png") 720 1080 "poster"

# Store listing screenshots are intentionally not auto-edited; recapture them from the verified app build.
if(Test-Path (Join-Path $legacy "stremio-xbox-poster-720x1080.png")){New-PromoArt (Join-Path $legacy "openstream-tv-poster-720x1080.png") 720 1080 "poster";Remove-Item (Join-Path $legacy "stremio-xbox-poster-720x1080.png")}
if(Test-Path (Join-Path $legacy "stremio-xbox-poster-v1.png")){New-PromoArt (Join-Path $legacy "openstream-tv-poster-v1.png") 1024 1536 "poster";Remove-Item (Join-Path $legacy "stremio-xbox-poster-v1.png")}
$privacy=Join-Path $legacy "STREMIO_XBOX_PRIVACY_POLICY_DRAFT.md"
if(Test-Path $privacy){$t=Get-Content $privacy -Raw;$t=$t.Replace("Stremio Xbox","OpenStream TV").Replace("media-browsing application for Xbox and Windows","independent media discovery and playback application for Windows and compatible TV devices. OpenStream TV is not affiliated with or endorsed by Stremio");Set-Content (Join-Path $legacy "OPENSTREAM_TV_PRIVACY_POLICY_DRAFT.md") $t -Encoding UTF8;Remove-Item $privacy}

$manifestPath=Join-Path $project "Package.appxmanifest";[xml]$manifest=Get-Content $manifestPath -Raw
$ns=New-Object System.Xml.XmlNamespaceManager($manifest.NameTable);$ns.AddNamespace("f","http://schemas.microsoft.com/appx/manifest/foundation/windows10");$ns.AddNamespace("uap","http://schemas.microsoft.com/appx/manifest/uap/windows10")
$manifest.SelectSingleNode("//f:Properties/f:DisplayName",$ns).InnerText="OpenStream TV";$visual=$manifest.SelectSingleNode("//uap:VisualElements",$ns);$visual.SetAttribute("DisplayName","OpenStream TV");$visual.SetAttribute("Description","Independent movie and TV discovery and playback using compatible third-party add-ons.");$manifest.Save($manifestPath)

$mainXaml=Join-Path $project "MainPage.xaml";$s=Get-Content $mainXaml -Raw
$s=$s.Replace("WatchStream Home","OpenStream TV Home").Replace("WATCHSTREAM","OPENSTREAM TV")
$s=$s.Replace('Text="Stremio" FontFamily="{StaticResource HeadingFont}" FontSize="27"','Text="OpenStream TV" FontFamily="{StaticResource HeadingFont}" FontSize="27"')
$s=$s.Replace("Explore movies and series from your Stremio add-ons.","Discover movies and series from your configured add-ons.")
$s=$s.Replace("Paste HTTPS Stremio manifest URLs. Providers are validated before they are saved.","Paste compatible add-on manifest URLs. Providers are validated before they are saved.")
$s=$s.Replace("https://watchstream-stremio-server.onrender.com/","https://your-service.example/")
$notice='                            <TextBlock Text="OpenStream TV is an independent application. It is not affiliated with or endorsed by Stremio. Stremio account and add-on compatibility is provided through third-party services." FontSize="15" Foreground="{StaticResource MutedTextBrush}" TextWrapping="Wrap" />'+[Environment]::NewLine
$s=$s.Replace('                        <StackPanel x:Name="SignedOutAccountContent" Spacing="18">'+[Environment]::NewLine,'                        <StackPanel x:Name="SignedOutAccountContent" Spacing="18">'+[Environment]::NewLine+$notice)
Set-Content $mainXaml $s -Encoding UTF8
$detailsXaml=Join-Path $project "DetailsPage.xaml";$s=Get-Content $detailsXaml -Raw;$s=$s.Replace('Text="Stremio" FontSize="25"','Text="OpenStream TV" FontSize="25"');Set-Content $detailsXaml $s -Encoding UTF8
$detailsCs=Join-Path $project "DetailsPage.xaml.cs";$s=Get-Content $detailsCs -Raw
$s=$s.Replace("Add a provider from Add-ons, or sign in to sync your Stremio providers.","Add a compatible provider from Add-ons, or sign in to sync your configured providers.")
$s=$s.Replace("Connect a Stremio Service to use torrent sources.","Connect a compatible streaming service to use torrent sources.")
$s=$s.Replace("Preparing stream through Stremio Service.","Preparing stream through your configured streaming service.")
$s=$s.Replace("compatible Stremio Service","compatible streaming service");Set-Content $detailsCs $s -Encoding UTF8
$mainCs=Join-Path $project "MainPage.xaml.cs";$s=Get-Content $mainCs -Raw
$s=$s.Replace("Explore more from your Stremio add-ons.","Explore more from your configured add-ons.")
$s=$s.Replace("Stremio Service URL","compatible streaming service URL").Replace("Stremio server","streaming service")
$s=$s.Replace("Titles saved locally on this Xbox","Titles saved locally on this device")
$s=$s.Replace("Test a direct stream through the native Xbox media pipeline","Test a direct stream through the native media pipeline")
$s=$s.Replace("Official movie and series catalog used by WatchStream.","Movie and series catalogue available in OpenStream TV.")
$s=$s.Replace("Manage the catalogs and stream providers connected to WatchStream.","Manage the catalogues and stream providers connected to OpenStream TV.")
Set-Content $mainCs $s -Encoding UTF8
Write-Output "OpenStream TV rebrand updates completed. Existing package version was preserved."


