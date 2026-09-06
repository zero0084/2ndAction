# Splits a horizontal sprite-sheet PNG into $Count frames by finding, near
# each of the (Count-1) even-division boundaries, the column with the
# LOWEST total alpha ("ink density") within a search window around that
# boundary - i.e. nudging each cut into the thinnest nearby gap between
# poses instead of blindly slicing at a fixed position (which cut through
# an outstretched fist/cape in testing, since these AI-generated sheets
# have no fully-transparent separator column between poses).
param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [Parameter(Mandatory=$true)][string]$OutDir,
    [Parameter(Mandatory=$true)][int]$Count,
    [double]$SearchFraction = 0.35
)

Add-Type -AssemblyName System.Drawing

$img = [System.Drawing.Bitmap]::FromFile($InputPath)
$w = $img.Width
$h = $img.Height

$bmpData = $img.LockBits((New-Object System.Drawing.Rectangle(0,0,$w,$h)), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $bmpData.Stride
$bytes = New-Object byte[] ($stride * $h)
[System.Runtime.InteropServices.Marshal]::Copy($bmpData.Scan0, $bytes, 0, $bytes.Length)
$img.UnlockBits($bmpData)

$density = New-Object int[] $w
for ($x = 0; $x -lt $w; $x++) {
    $sum = 0
    for ($y = 0; $y -lt $h; $y++) {
        $offset = $y * $stride + $x * 4
        $sum += $bytes[$offset + 3]
    }
    $density[$x] = $sum
}

$sliceW = $w / [double]$Count
$searchRadius = [int]($sliceW * $SearchFraction)

$boundaries = New-Object System.Collections.Generic.List[int]
$boundaries.Add(0)
for ($k = 1; $k -lt $Count; $k++) {
    $center = [int]([Math]::Round($k * $sliceW))
    $lo = [Math]::Max(1, $center - $searchRadius)
    $hi = [Math]::Min($w - 2, $center + $searchRadius)
    $bestX = $center
    $bestVal = [int]::MaxValue
    for ($x = $lo; $x -le $hi; $x++) {
        if ($density[$x] -lt $bestVal) { $bestVal = $density[$x]; $bestX = $x }
    }
    $boundaries.Add($bestX)
}
$boundaries.Add($w)

Write-Output "Boundaries for $InputPath : $($boundaries -join ', ')"

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

for ($i = 0; $i -lt $Count; $i++) {
    $x0 = $boundaries[$i]
    $x1 = $boundaries[$i + 1]
    $thisW = $x1 - $x0
    $rect = New-Object System.Drawing.Rectangle($x0, 0, $thisW, $h)
    $cropped = New-Object System.Drawing.Bitmap($thisW, $h)
    $g = [System.Drawing.Graphics]::FromImage($cropped)
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle(0,0,$thisW,$h)), $rect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $outPath = Join-Path $OutDir ("frame_{0:D2}.png" -f $i)
    $cropped.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $cropped.Dispose()
}
$img.Dispose()
Write-Output "Saved $Count frames to $OutDir"
