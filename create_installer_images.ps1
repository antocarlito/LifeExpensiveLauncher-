Add-Type -AssemblyName System.Drawing

$outDir = "F:\Arma3Servermaps\LifeExpensiveLauncher\installer_assets"
if (!(Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

# === Image gauche du wizard (164x314) ===
$w = 164; $h = 314
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

# Fond gradient indigo -> dark
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 0)),
    (New-Object System.Drawing.Point(0, $h)),
    [System.Drawing.ColorTranslator]::FromHtml("#2200CC"),
    [System.Drawing.ColorTranslator]::FromHtml("#0D0D1A")
)
$g.FillRectangle($brush, 0, 0, $w, $h)

# Barres gradient
$cyanBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#00BBDD"))
$orangeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#FF8C00"))
$whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)

$g.FillRectangle($cyanBrush, 0, 0, $w, 3)

# Texte
$fontTitle = New-Object System.Drawing.Font("Segoe UI", 20, [System.Drawing.FontStyle]::Bold)
$fontSub = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Regular)

$g.DrawString("LIFE", $fontTitle, $cyanBrush, 15, 90)
$g.DrawString("EXPENSIVE", $fontTitle, $whiteBrush, 15, 118)
$g.FillRectangle($orangeBrush, 20, 155, 120, 2)
$g.DrawString("R O L E P L A Y", $fontSub, $orangeBrush, 22, 162)

# URL en bas
$fontUrl = New-Object System.Drawing.Font("Segoe UI", 7, [System.Drawing.FontStyle]::Regular)
$dimBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#666688"))
$g.DrawString("lifeexpensive.com", $fontUrl, $dimBrush, 25, $h - 25)
$g.FillRectangle($orangeBrush, 0, $h - 3, $w, 3)

$g.Dispose()
$bmp.Save("$outDir\wizard_image.bmp", [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()
Write-Host "wizard_image.bmp cree"

# === Petite image header (55x55) ===
$bmp2 = New-Object System.Drawing.Bitmap(55, 55)
$g2 = [System.Drawing.Graphics]::FromImage($bmp2)
$g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g2.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

# Fond gradient cyan -> orange (comme le logo site)
$brush2 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 0)),
    (New-Object System.Drawing.Point(55, 55)),
    [System.Drawing.ColorTranslator]::FromHtml("#00BBDD"),
    [System.Drawing.ColorTranslator]::FromHtml("#FF8C00")
)
$g2.FillRectangle($brush2, 0, 0, 55, 55)

# Cercle interieur sombre
$darkBrush2 = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#0D0D1A"))
$g2.FillEllipse($darkBrush2, 5, 5, 45, 45)

# Texte LE
$fontLE = New-Object System.Drawing.Font("Segoe UI", 16, [System.Drawing.FontStyle]::Bold)
$g2.DrawString("LE", $fontLE, $cyanBrush, 10, 13)

$g2.Dispose()
$bmp2.Save("$outDir\wizard_small.bmp", [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp2.Dispose()
Write-Host "wizard_small.bmp cree"
