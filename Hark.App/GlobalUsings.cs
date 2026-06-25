// HARK desktop host uses WPF for UI and WinForms only for the tray NotifyIcon. Several type names
// exist in both worlds (Application, MessageBox, Clipboard, Brush, Color, ...). These aliases make
// the WPF type the default everywhere; WinForms equivalents are referenced via their full
// namespace (e.g. System.Windows.Forms.NotifyIcon, System.Drawing.SystemIcons) when needed.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Clipboard = System.Windows.Clipboard;
global using Brush = System.Windows.Media.Brush;
global using Color = System.Windows.Media.Color;
global using SolidColorBrush = System.Windows.Media.SolidColorBrush;
