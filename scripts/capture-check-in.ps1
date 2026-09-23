param([string]$Output='artifacts/check-in-window.png')
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WindowCapture {
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle,out Rect rectangle);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr handle,IntPtr dc,uint flags);
}
"@
$null=[WindowCapture]::SetProcessDPIAware()
$p=Get-Process | Where-Object MainWindowTitle -eq 'miLife | PC check-in' | Select-Object -First 1
if(!$p){throw 'Check-in window not found'}
$rect=New-Object WindowCapture+Rect
$null=[WindowCapture]::GetWindowRect($p.MainWindowHandle,[ref]$rect)
$bitmap=New-Object Drawing.Bitmap(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top))
$graphics=[Drawing.Graphics]::FromImage($bitmap);$dc=$graphics.GetHdc()
try{$null=[WindowCapture]::PrintWindow($p.MainWindowHandle,$dc,2)}finally{$graphics.ReleaseHdc($dc);$graphics.Dispose()}
$bitmap.Save((Join-Path $PWD $Output),[Drawing.Imaging.ImageFormat]::Png);$bitmap.Dispose()
