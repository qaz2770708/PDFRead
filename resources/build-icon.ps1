$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$images=@()
foreach($size in @(16,24,32,48,64,128,256)) {
    $bitmap=New-Object Drawing.Bitmap($size,$size)
    $g=[Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode='AntiAlias'
    $g.ScaleTransform($size/256.0,$size/256.0)
    $green=New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#3f7057'))
    $fold=New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#bed6c5'))
    $gold=New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#e6bd63'))
    $path=New-Object Drawing.Drawing2D.GraphicsPath
    foreach($arc in @(@(4,4,80,80,180),@(172,4,80,80,270),@(172,172,80,80,0),@(4,172,80,80,90))) {$path.AddArc($arc[0],$arc[1],$arc[2],$arc[3],$arc[4],90)}
    $path.CloseFigure();$g.FillPath($green,$path)
    $page=[Drawing.PointF[]]@((New-Object Drawing.PointF(68,36)),(New-Object Drawing.PointF(151,36)),(New-Object Drawing.PointF(192,77)),(New-Object Drawing.PointF(192,210)),(New-Object Drawing.PointF(68,210)))
    $g.FillPolygon([Drawing.Brushes]::White,$page)
    $g.FillPolygon($fold,[Drawing.PointF[]]@((New-Object Drawing.PointF(151,36)),(New-Object Drawing.PointF(151,77)),(New-Object Drawing.PointF(192,77))))
    $pen=New-Object Drawing.Pen($green,12);$pen.StartCap='Round';$pen.EndCap='Round'
    foreach($line in @(@(109,168),@(135,155),@(161,146))) {$g.DrawLine($pen,92,$line[0],$line[1],$line[0])}
    $g.FillPolygon($gold,[Drawing.PointF[]]@((New-Object Drawing.PointF(153,182)),(New-Object Drawing.PointF(199,136)),(New-Object Drawing.PointF(213,150)),(New-Object Drawing.PointF(167,196)),(New-Object Drawing.PointF(148,201))))
    $stream=New-Object IO.MemoryStream;$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $images+=,@{Size=$size;Bytes=$stream.ToArray()}
    $stream.Dispose();$pen.Dispose();$path.Dispose();$green.Dispose();$fold.Dispose();$gold.Dispose();$g.Dispose();$bitmap.Dispose()
}
$output=[IO.File]::Create((Join-Path $PSScriptRoot 'PDFRead.ico'));$writer=New-Object IO.BinaryWriter($output)
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$images.Count)
    $offset=6+16*$images.Count
    foreach($entry in $images){$dimension=if($entry.Size -eq 256){0}else{$entry.Size};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$entry.Bytes.Length);$writer.Write([uint32]$offset);$offset+=$entry.Bytes.Length}
    foreach($entry in $images){$writer.Write([byte[]]$entry.Bytes)}
} finally {$writer.Dispose();$output.Dispose()}
