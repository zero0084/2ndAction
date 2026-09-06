# Splits a horizontal sprite-sheet PNG into $Count equal-width vertical
# slices (full sheet height), saved as frame_00.png, frame_01.png, ... in
# $OutDir. Used instead of gap-based auto-detection because these
# AI-generated sheets have overlapping capes/swords between adjacent poses
# with no fully-transparent separator column - even slicing (frames were
# explicitly requested "evenly spaced, same character scale") is the
# practical fallback; a little cape/blade clipping at slice edges is an
# acceptable Ver.1 tradeoff.
param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [Parameter(Mandatory=$true)][string]$OutDir,
    [Parameter(Mandatory=$true)][int]$Count
)

Add-Type -AssemblyName System.Drawing

$img = [System.Drawing.Bitmap]::FromFile($InputPath)
$w = $img.Width
$h = $img.Height
$sliceW = [int][Math]::Floor($w / $Count)

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

for ($i = 0; $i -lt $Count; $i++) {
    $x0 = [int]($i * $sliceW)
    $thisW = [int]$(if ($i -eq $Count - 1) { $w - $x0 } else { $sliceW })
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
