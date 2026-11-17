# Преобразует текстовую сетку в PNG: буквы это цвета (легенда ниже), точка это прозрачность. Сообщает о строках неверной длины.
param([string]$Grid, [string]$Out, [int]$Scale = 10)
Add-Type -AssemblyName System.Drawing
$colors = @{
  'K' = '#2B1B17'; 'O' = '#E8923A'; 'D' = '#B5651D'; 'B' = '#3FA7E0'; 'G' = '#7BD88F'; 'I' = '#2B1B17'; 'P' = '#F4A0A0'
  'Y' = '#FFD84A'; 'W' = '#FFFFFF'; 'L' = '#BDE6F7'; 'F' = '#1F6FB5'
}
$name = [IO.Path]::GetFileNameWithoutExtension($Grid)
if ($name -eq 'cat')  { $colors['B'] = '#FFE2B8'; $colors['W'] = '#FFFFFF' }
if ($name -eq 'duck') { $colors['W'] = '#E6B422'; $colors['O'] = '#FF8A1F'; $colors['K'] = '#3A2A10' }
if ($name -eq 'fish') { $colors['K'] = '#10233D'; $colors['I'] = '#10233D' }
$rows = Get-Content -LiteralPath $Grid -Encoding UTF8 | Where-Object { $_.Length -gt 0 }
$w = 0; foreach ($r in $rows) { if ($r.Length -gt $w) { $w = $r.Length } }
$bad = @()
for ($i = 0; $i -lt $rows.Count; $i++) { if ($rows[$i].Length -ne $w) { $bad += "строка $($i+1): $($rows[$i].Length) вместо $w" } }
$bmp = New-Object System.Drawing.Bitmap -ArgumentList ([int]($w * $Scale)), ([int]($rows.Count * $Scale))
for ($y = 0; $y -lt $rows.Count; $y++) {
  for ($x = 0; $x -lt $rows[$y].Length; $x++) {
    $c = [string]$rows[$y][$x]
    if ($colors.ContainsKey($c)) {
      $col = [System.Drawing.ColorTranslator]::FromHtml($colors[$c])
      for ($sy = 0; $sy -lt $Scale; $sy++) { for ($sx = 0; $sx -lt $Scale; $sx++) { $bmp.SetPixel($x * $Scale + $sx, $y * $Scale + $sy, $col) } }
    }
  }
}
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"$name : $w x $($rows.Count), ошибок длины строк: $($bad.Count)"
$bad
