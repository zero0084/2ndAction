# Crops the single wide platform reference image into a left end-cap and a
# middle tiling strip, plus a right end-cap made by horizontally flipping
# the left cap (guarantees the two ends match stylistically without a
# second generation/crop).
Add-Type -AssemblyName System.Drawing

$src = "C:\GameProject\2ndAction\Unity\2ndAction\Assets\Art\VisualStyleV1\Ground\platform_reference_01.png"
$outDir = "C:\GameProject\2ndAction\Unity\2ndAction\Assets\Art\VisualStyleV1\Ground"

function Crop($img, $x0, $w) {
    $h = $img.Height
    $rect = New-Object System.Drawing.Rectangle($x0, 0, $w, $h)
    $cropped = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($cropped)
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle(0,0,$w,$h)), $rect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $cropped
}

$img = [System.Drawing.Bitmap]::FromFile($src)

$left = Crop $img 0 450
$left.Save("$outDir\platform_left.png", [System.Drawing.Imaging.ImageFormat]::Png)
$left.Dispose()

$mid = Crop $img 450 1150
$mid.Save("$outDir\platform_mid.png", [System.Drawing.Imaging.ImageFormat]::Png)
$mid.Dispose()

$img.Dispose()

# Right cap = left cap flipped horizontally
$leftForFlip = [System.Drawing.Bitmap]::FromFile("$outDir\platform_left.png")
$leftForFlip.RotateFlip([System.Drawing.RotateFlipType]::RotateNoneFlipX)
$leftForFlip.Save("$outDir\platform_right.png", [System.Drawing.Imaging.ImageFormat]::Png)
$leftForFlip.Dispose()

Write-Output "Saved platform_left.png, platform_mid.png, platform_right.png to $outDir"
