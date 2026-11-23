$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$outPath = Join-Path $PSScriptRoot 'app.ico'
$sizes = @(16, 32, 48, 256)

function New-RoundedRectPath {
    param([float]$x, [float]$y, [float]$w, [float]$h, [float]$r)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Render-IconBitmap {
    param([int]$size)

    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [float]($size * 0.06)
    if ($size -le 16) { $pad = 0.5 }
    $boxSize = [float]($size - $pad * 2)
    $radius = [float]($boxSize * 0.22)

    $rectPath = New-RoundedRectPath -x $pad -y $pad -w $boxSize -h $boxSize -r $radius

    $gradBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF($pad, $pad)),
        (New-Object System.Drawing.PointF(($pad + $boxSize), ($pad + $boxSize))),
        ([System.Drawing.Color]::FromArgb(255, 79, 70, 229)),
        ([System.Drawing.Color]::FromArgb(255, 37, 99, 235))
    )
    $g.FillPath($gradBrush, $rectPath)

    $borderPenWidth = [Math]::Max(1.0, $size * 0.025)
    $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(80, 255, 255, 255), $borderPenWidth)
    $g.DrawPath($borderPen, $rectPath)

    # Subtle accent orb at top-right
    if ($size -ge 32) {
        $dotR = [float]($size * 0.09)
        $dotX = [float]($pad + $boxSize * 0.72)
        $dotY = [float]($pad + $boxSize * 0.14)
        $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70, 147, 197, 253))
        $g.FillEllipse($dotBrush, $dotX, $dotY, $dotR, $dotR)
        $dotBrush.Dispose()
    }

    # Draw crisp white "汉" glyph
    $fontSize = [float]($size * 0.54)
    $fontFamily = New-Object System.Drawing.FontFamily('Microsoft YaHei UI')
    $font = New-Object System.Drawing.Font($fontFamily, $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center

    $textRect = New-Object System.Drawing.RectangleF(0, ($size * 0.03), $size, $size)
    $whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.DrawString('汉', $font, $whiteBrush, $textRect, $sf)

    $whiteBrush.Dispose()
    $sf.Dispose()
    $font.Dispose()
    $fontFamily.Dispose()
    $borderPen.Dispose()
    $gradBrush.Dispose()
    $rectPath.Dispose()
    $g.Dispose()
    return $bmp
}

$entries = New-Object System.Collections.Generic.List[object]

foreach ($sz in $sizes) {
    $bmp = Render-IconBitmap -size $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()

    $entries.Add([PSCustomObject]@{
        Size = $sz
        Bytes = $bytes
    })
}

$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR header
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$entries.Count)

$offset = 6 + (16 * $entries.Count)
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { [byte]0 } else { [byte]$e.Size }
    $bw.Write($dim)                 # bWidth
    $bw.Write($dim)                 # bHeight
    $bw.Write([byte]0)              # bColorCount
    $bw.Write([byte]0)              # bReserved
    $bw.Write([uint16]1)            # wPlanes
    $bw.Write([uint16]32)           # wBitCount
    $bw.Write([uint32]$e.Bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $e.Bytes.Length
}

foreach ($e in $entries) {
    $bw.Write($e.Bytes)
}

$bw.Flush()
$bw.Dispose()
$fs.Dispose()

Write-Host "Generated icon: $outPath ($((Get-Item $outPath).Length) bytes)"
