param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\store-assets')
)

Add-Type -AssemblyName System.Drawing

$output = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($output) | Out-Null

function New-Brush([string]$hex) { New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex)) }
function New-Font([float]$size, [System.Drawing.FontStyle]$style = [System.Drawing.FontStyle]::Regular) {
    New-Object System.Drawing.Font 'Segoe UI', $size, $style, ([System.Drawing.GraphicsUnit]::Pixel)
}
function Draw-Text($g, [string]$text, [float]$x, [float]$y, [float]$size, [string]$color = '#FFFFFF', [System.Drawing.FontStyle]$style = [System.Drawing.FontStyle]::Regular) {
    $font = New-Font $size $style; $brush = New-Brush $color
    $g.DrawString($text, $font, $brush, $x, $y)
    $font.Dispose(); $brush.Dispose()
}
function Draw-RoundRect($g, [System.Drawing.Brush]$brush, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90); $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure(); $g.FillPath($brush, $path); $path.Dispose()
}
function Draw-Brand($g, [float]$x, [float]$y, [float]$scale = 1) {
    $purple = New-Brush '#8B5CF6'; $white = New-Brush '#FFFFFF'
    $g.FillEllipse($purple, $x, $y, 48*$scale, 48*$scale)
    $points = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($x+19*$scale,$y+13*$scale),
        [System.Drawing.PointF]::new($x+37*$scale,$y+24*$scale),
        [System.Drawing.PointF]::new($x+19*$scale,$y+35*$scale))
    $g.FillPolygon($white, $points); $purple.Dispose(); $white.Dispose()
}
function Draw-Sidebar($g, [string]$active) {
    $panel = New-Brush '#10131F'; $g.FillRectangle($panel, 0, 0, 236, 1080); $panel.Dispose()
    Draw-Brand $g 42 42 1
    Draw-Text $g 'watchstream' 104 46 27 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    $items = @('Home','Discover','Library','Add-ons')
    for ($i=0; $i -lt $items.Count; $i++) {
        $y = 150 + $i*72
        if ($items[$i] -eq $active) { $b = New-Brush '#6D45D6'; Draw-RoundRect $g $b 26 $y 184 54 12; $b.Dispose() }
        Draw-Text $g $items[$i] 58 ($y+11) 23 ($(if ($items[$i] -eq $active) {'#FFFFFF'} else {'#B9B5C8'}))
    }
    Draw-Text $g 'ADVANCED' 42 826 14 '#777186' ([System.Drawing.FontStyle]::Bold)
    Draw-Text $g 'Playback lab' 58 866 20 '#9D98AA'
    Draw-Text $g 'Server' 58 916 20 '#9D98AA'
    Draw-Text $g 'Settings' 58 966 20 '#9D98AA'
}
function Draw-Poster($g, [float]$x, [float]$y, [string]$title, [string]$subtitle, [string]$c1, [string]$c2, [bool]$focused=$false) {
    $rect = [System.Drawing.RectangleF]::new($x,$y,236,354)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.ColorTranslator]::FromHtml($c1)), ([System.Drawing.ColorTranslator]::FromHtml($c2)), 35
    Draw-RoundRect $g $gradient $x $y 236 354 14; $gradient.Dispose()
    $orb = New-Brush '#44FFFFFF'; $g.FillEllipse($orb,$x+46,$y+72,144,144); $orb.Dispose()
    $dark = New-Brush '#55000000'; Draw-RoundRect $g $dark ($x+24) ($y+260) 188 64 12; $dark.Dispose()
    Draw-Text $g ($title.Substring(0,1).ToUpper()) ($x+91) ($y+105) 72 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    if ($focused) { $pen = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#F5E8FF')), 7; $g.DrawRectangle($pen,$x-5,$y-5,246,364); $pen.Dispose() }
    Draw-Text $g $title $x ($y+370) 22 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    Draw-Text $g $subtitle $x ($y+403) 17 '#A8A3B5'
}
function New-Screen([string]$name, [scriptblock]$draw) {
    $bmp = New-Object System.Drawing.Bitmap 1920,1080
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode='AntiAlias'; $g.TextRenderingHint='AntiAliasGridFit'
    $bg = New-Brush '#080A12'; $g.FillRectangle($bg,0,0,1920,1080); $bg.Dispose()
    & $draw $g
    $bmp.Save((Join-Path $output $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

New-Screen '01-home.png' {
    param($g); Draw-Sidebar $g 'Home'
    Draw-Text $g 'Home' 294 64 48 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    Draw-Text $g 'Movies and series, ready for the big screen' 296 126 23 '#9D98AA'
    $b=New-Brush '#181B29'; Draw-RoundRect $g $b 1600 58 246 58 16; $b.Dispose(); Draw-Text $g 'Search' 1680 72 22 '#FFFFFF'
    Draw-Text $g 'Movies · Popular' 296 204 27 '#FFFFFF' ([System.Drawing.FontStyle]::Bold); Draw-Text $g 'See all' 1740 210 19 '#B99CFF'
    $cards=@(
      @('The Matrix','1999 · 8.7 · 136 min','#4338CA','#0EA5E9'),@('Dune','2021 · 8.0 · 155 min','#B45309','#7C2D12'),
      @('Arcane','2021 · 9.0 · Series','#7C3AED','#DB2777'),@('Arrival','2016 · 7.9 · 116 min','#0F766E','#334155'),
      @('Foundation','2021 · 7.6 · Series','#1D4ED8','#6D28D9'))
    for($i=0;$i-lt$cards.Count;$i++){Draw-Poster $g (296+$i*284) 270 $cards[$i][0] $cards[$i][1] $cards[$i][2] $cards[$i][3] ($i-eq0)}
    Draw-Text $g 'Continue exploring' 296 782 27 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    $bar=New-Brush '#151826'; Draw-RoundRect $g $bar 296 840 1440 122 18; $bar.Dispose()
    Draw-Text $g 'Browse public catalogs, open a title, and choose Find streams.' 336 866 24 '#FFFFFF'
    Draw-Text $g 'Controller-first navigation · clear focus · TV-safe spacing' 336 910 19 '#9D98AA'
}
New-Screen '02-discover.png' {
    param($g); Draw-Sidebar $g 'Discover'
    Draw-Text $g 'Discover' 294 64 48 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    Draw-Text $g 'Search movies and series' 296 126 23 '#9D98AA'
    $search=New-Brush '#181B29'; Draw-RoundRect $g $search 296 190 1190 72 16; $search.Dispose(); Draw-Text $g 'the matrix' 330 207 27 '#FFFFFF'
    $voice=New-Brush '#6D45D6'; Draw-RoundRect $g $voice 1510 190 220 72 16; $voice.Dispose(); Draw-Text $g 'Voice search' 1540 209 23 '#FFFFFF'
    Draw-Text $g 'Search results' 296 320 27 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    $cards=@(@('The Matrix','1999 · Movie','#4338CA','#0EA5E9'),@('Reloaded','2003 · Movie','#0F766E','#164E63'),@('Revolutions','2003 · Movie','#991B1B','#581C87'),@('Resurrections','2021 · Movie','#1D4ED8','#7E22CE'))
    for($i=0;$i-lt$cards.Count;$i++){Draw-Poster $g (296+$i*310) 382 $cards[$i][0] $cards[$i][1] $cards[$i][2] $cards[$i][3] ($i-eq0)}
}
New-Screen '03-details.png' {
    param($g); Draw-Sidebar $g 'Home'
    $rect=[System.Drawing.RectangleF]::new(236,0,1684,1080); $grad=New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,([System.Drawing.ColorTranslator]::FromHtml('#312E81')),([System.Drawing.ColorTranslator]::FromHtml('#080A12')),0; $g.FillRectangle($grad,$rect); $grad.Dispose()
    $poster=New-Brush '#4F46E5'; Draw-RoundRect $g $poster 304 170 340 510 20; $poster.Dispose(); Draw-Text $g 'M' 410 328 140 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    Draw-Text $g 'The Matrix' 710 174 58 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    Draw-Text $g '1999  ·  8.7  ·  136 min  ·  Science fiction' 714 254 23 '#C9C5D6'
    Draw-Text $g 'A computer hacker discovers that the world he knows is a constructed reality.' 714 330 25 '#E6E2EF'
    Draw-Text $g 'Choose an available source and continue on the big screen.' 714 372 22 '#A9A4B4'
    $primary=New-Brush '#6D45D6'; Draw-RoundRect $g $primary 710 454 270 70 16; $primary.Dispose(); Draw-Text $g 'Find streams' 756 473 25 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    $secondary=New-Brush '#191C2A'; Draw-RoundRect $g $secondary 1000 454 280 70 16; $secondary.Dispose(); Draw-Text $g 'Add to library' 1043 473 25 '#FFFFFF'
    Draw-Text $g 'Available through configured Stremio add-ons' 714 590 21 '#B99CFF'
    $card=New-Brush '#151826'; Draw-RoundRect $g $card 710 638 850 166 18; $card.Dispose()
    Draw-Text $g 'Hosted streaming server' 746 670 23 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    Draw-Text $g 'Connected securely · ready to prepare playback' 746 713 20 '#76E5A4'
}
New-Screen '04-server.png' {
    param($g); Draw-Sidebar $g 'Server'
    Draw-Text $g 'Server' 294 64 48 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    Draw-Text $g 'Developer / Advanced' 296 126 21 '#9D98AA'
    $card=New-Brush '#141725'; Draw-RoundRect $g $card 296 202 1240 510 22; $card.Dispose()
    Draw-Brand $g 344 248 1.15; Draw-Text $g 'Connect your Stremio server' 424 252 30 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    Draw-Text $g 'Streaming service URL' 348 350 19 '#A8A3B5'
    $field=New-Brush '#0D101A'; Draw-RoundRect $g $field 348 390 1080 70 14; $field.Dispose(); Draw-Text $g 'https://watchstream-stremio-server.onrender.com/' 374 409 23 '#FFFFFF'
    Draw-Text $g 'Connected' 350 518 29 '#76E5A4' ([System.Drawing.FontStyle]::Bold)
    Draw-Text $g 'Stremio service is ready for playback.' 350 564 21 '#C9C5D6'
    $button=New-Brush '#6D45D6'; Draw-RoundRect $g $button 350 622 286 68 14; $button.Dispose(); Draw-Text $g 'Connect and save' 388 641 23 '#FFFFFF' ([System.Drawing.FontStyle]::Bold)
    Draw-Text $g 'The free hosted server may take about a minute to wake after being idle.' 296 770 22 '#A8A3B5'
}

function New-Art([string]$name,[int]$width,[int]$height,[bool]$showTitle) {
    $bmp=New-Object System.Drawing.Bitmap $width,$height; $g=[System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode='AntiAlias'; $g.TextRenderingHint='AntiAliasGridFit'
    $rect=[System.Drawing.RectangleF]::new(0,0,$width,$height); $grad=New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,([System.Drawing.ColorTranslator]::FromHtml('#090B15')),([System.Drawing.ColorTranslator]::FromHtml('#6D28D9')),65; $g.FillRectangle($grad,$rect); $grad.Dispose()
    $glow=New-Brush '#337C3AED'; $g.FillEllipse($glow,-180,$height*0.44,$width*1.2,$width*1.2); $glow.Dispose()
    $s=[Math]::Min($width,$height)/280; Draw-Brand $g (($width-48*$s)/2) ($height*0.25) $s
    if($showTitle){Draw-Text $g 'watchstream' ($width*0.13) ($height*0.56) ($width*0.105) '#FFFFFF' ([System.Drawing.FontStyle]::Bold); Draw-Text $g 'Made for the big screen' ($width*0.19) ($height*0.66) ($width*0.045) '#D8CFFF'}
    $bmp.Save((Join-Path $output $name),[System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}
New-Art 'poster-720x1080.png' 720 1080 $true
New-Art 'box-art-1080x1080.png' 1080 1080 $true

Get-ChildItem -LiteralPath $output -Filter *.png | Select-Object FullName, Length
