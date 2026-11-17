$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
$root = Split-Path $PSScriptRoot -Parent
[xml]$app = Get-Content -LiteralPath (Join-Path $root 'DocTwin\App.xaml') -Raw
$resources = $app.Application.'Application.Resources'.ResourceDictionary.InnerXml
$context = New-Object System.Windows.Markup.ParserContext
$context.BaseUri = [Uri](Join-Path $root 'DocTwin\App.xaml')
$dictionary = [System.Windows.Markup.XamlReader]::Parse('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">' + $resources + '</ResourceDictionary>', $context)
$application = New-Object System.Windows.Application
$application.Resources = $dictionary
$template = $dictionary['BrandMarkTemplate']
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
	$visual = $template.LoadContent()
	$visual.Width = $size
	$visual.Height = $size
	$visual.Measure((New-Object System.Windows.Size($size, $size)))
	$visual.Arrange((New-Object System.Windows.Rect(0, 0, $size, $size)))
	$visual.UpdateLayout()
	$bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32))
	$bitmap.Render($visual)
	$encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
	$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
	$stream = New-Object System.IO.MemoryStream
	try { $encoder.Save($stream); $images += ,$stream.ToArray() } finally { $stream.Dispose() }
}
$assets = Join-Path $root 'DocTwin\Assets'
[System.IO.Directory]::CreateDirectory($assets) | Out-Null
$file = [System.IO.File]::Create((Join-Path $assets 'DocTwin.ico'))
$writer = New-Object System.IO.BinaryWriter($file)
try {
	$writer.Write([uint16]0)
	$writer.Write([uint16]1)
	$writer.Write([uint16]$sizes.Count)
	$offset = 6 + 16 * $sizes.Count
	for ($i = 0; $i -lt $sizes.Count; $i++) {
		$dimension = $sizes[$i]
		if ($dimension -eq 256) { $dimension = 0 }
		$writer.Write([byte]$dimension)
		$writer.Write([byte]$dimension)
		$writer.Write([byte]0)
		$writer.Write([byte]0)
		$writer.Write([uint16]1)
		$writer.Write([uint16]32)
		$writer.Write([uint32]$images[$i].Length)
		$writer.Write([uint32]$offset)
		$offset += $images[$i].Length
	}
	foreach ($image in $images) { $writer.Write([byte[]]$image) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Output 'Generated the multi-resolution Windows icon from BrandMarkTemplate.'
