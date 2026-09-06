Add-Type -AssemblyName System.Drawing

$src = "C:\GameProject\2ndAction\Unity\2ndAction\Assets\Art\VisualStyleV1\Enemy\goblin_reference_01.png"
$img = [System.Drawing.Bitmap]::FromFile($src)
$w = $img.Width; $h = $img.Height

$bmpData = $img.LockBits((New-Object System.Drawing.Rectangle(0,0,$w,$h)), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $bmpData.Stride
$bytes = New-Object byte[] ($stride * $h)
[System.Runtime.InteropServices.Marshal]::Copy($bmpData.Scan0, $bytes, 0, $bytes.Length)
$img.UnlockBits($bmpData)

$minX = $w; $maxX = -1; $minY = $h; $maxY = -1
for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $a = $bytes[$y * $stride + $x * 4 + 3]
        if ($a -gt 15) {
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}

$pad = 6
$minX = [Math]::Max(0, $minX - $pad)
$minY = [Math]::Max(0, $minY - $pad)
$maxX = [Math]::Min($w - 1, $maxX + $pad)
$maxY = [Math]::Min($h - 1, $maxY + $pad)
$cw = $maxX - $minX + 1
$ch = $maxY - $minY + 1

Write-Output "content bounds: x=$minX..$maxX y=$minY..$maxY (${cw}x${ch})"

$rect = New-Object System.Drawing.Rectangle($minX, $minY, $cw, $ch)
$cropped = New-Object System.Drawing.Bitmap($cw, $ch)
$g = [System.Drawing.Graphics]::FromImage($cropped)
$g.DrawImage($img, (New-Object System.Drawing.Rectangle(0,0,$cw,$ch)), $rect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$img.Dispose()

$out = "C:\GameProject\2ndAction\Unity\2ndAction\Assets\Art\VisualStyleV1\Enemy\goblin_cropped.png"
$cropped.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$cropped.Dispose()
Write-Output "Saved $out"
