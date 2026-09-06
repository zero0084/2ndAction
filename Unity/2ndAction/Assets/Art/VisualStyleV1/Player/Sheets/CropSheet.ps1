# Detects individual character frames in a horizontal sprite-sheet PNG by
# scanning columns for alpha content, splitting on runs of fully-transparent
# columns wider than $minGap. Crops each detected frame to its own PNG
# (full sheet height, tight left/right bounds) named frame_00.png,
# frame_01.png, ... in $outDir.
#
# Usage: powershell -File CropSheet.ps1 -InputPath <sheet.png> -OutDir <dir> [-MinGap 8] [-AlphaThreshold 10]
param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [Parameter(Mandatory=$true)][string]$OutDir,
    [int]$MinGap = 8,
    [int]$AlphaThreshold = 10
)

Add-Type -AssemblyName System.Drawing

$img = [System.Drawing.Bitmap]::FromFile($InputPath)
$w = $img.Width
$h = $img.Height

# Fast column-has-content check via LockBits for speed on large images.
$bmpData = $img.LockBits((New-Object System.Drawing.Rectangle(0,0,$w,$h)), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $bmpData.Stride
$bytes = New-Object byte[] ($stride * $h)
[System.Runtime.InteropServices.Marshal]::Copy($bmpData.Scan0, $bytes, 0, $bytes.Length)
$img.UnlockBits($bmpData)

$colHasContent = New-Object bool[] $w
for ($x = 0; $x -lt $w; $x++) {
    $found = $false
    for ($y = 0; $y -lt $h; $y++) {
        $offset = $y * $stride + $x * 4
        $alpha = $bytes[$offset + 3]
        if ($alpha -gt $AlphaThreshold) { $found = $true; break }
    }
    $colHasContent[$x] = $found
}

# Group into runs of content separated by gaps >= MinGap
$segments = New-Object System.Collections.Generic.List[object]
$segStart = -1
$gapLen = 0
for ($x = 0; $x -lt $w; $x++) {
    if ($colHasContent[$x]) {
        if ($segStart -lt 0) { $segStart = $x }
        $gapLen = 0
    } else {
        if ($segStart -ge 0) {
            $gapLen++
            if ($gapLen -ge $MinGap) {
                $segEnd = $x - $gapLen
                $segments.Add(@{ start = $segStart; end = $segEnd })
                $segStart = -1
                $gapLen = 0
            }
        }
    }
}
if ($segStart -ge 0) {
    $segEnd = $w - 1
    while ($segEnd -gt $segStart -and -not $colHasContent[$segEnd]) { $segEnd-- }
    $segments.Add(@{ start = $segStart; end = $segEnd })
}

Write-Output "Detected $($segments.Count) segments in $InputPath (image ${w}x${h}):"
foreach ($s in $segments) {
    Write-Output "  x=$($s.start)..$($s.end) width=$($s.end - $s.start + 1)"
}

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$i = 0
foreach ($s in $segments) {
    $fw = $s.end - $s.start + 1
    $rect = New-Object System.Drawing.Rectangle($s.start, 0, $fw, $h)
    $cropped = New-Object System.Drawing.Bitmap($fw, $h)
    $g = [System.Drawing.Graphics]::FromImage($cropped)
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle(0,0,$fw,$h)), $rect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $outPath = Join-Path $OutDir ("frame_{0:D2}.png" -f $i)
    $cropped.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $cropped.Dispose()
    $i++
}
$img.Dispose()
Write-Output "Saved $i frames to $OutDir"
