// UseWPF and UseWindowsForms both implicit-import their own Color/Point/Size/Image/Application.
// This project is WPF first -- WinForms is only here for the tray icon -- so the unqualified
// names resolve to WPF and the few GDI+ types we need are spelled out in full.
global using Application = System.Windows.Application;
global using Border = System.Windows.Controls.Border;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using CheckBox = System.Windows.Controls.CheckBox;
global using Color = System.Windows.Media.Color;
global using Colors = System.Windows.Media.Colors;
global using ComboBox = System.Windows.Controls.ComboBox;
global using FontFamily = System.Windows.Media.FontFamily;
global using GroupBox = System.Windows.Controls.GroupBox;
global using Image = System.Windows.Controls.Image;
global using Pen = System.Windows.Media.Pen;
global using Point = System.Windows.Point;
global using Rectangle = System.Windows.Shapes.Rectangle;
global using Size = System.Windows.Size;
