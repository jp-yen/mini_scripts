using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace GpgUi
{
    public static class ThemeHelper
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeValueSize);

        public static void ApplyDarkTitleBar(Window window)
        {
            if (window == null) return;
            Action apply = () =>
            {
                try
                {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    if (hwnd == IntPtr.Zero) return;

                    int useDarkMode = 1;
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));

                    // Title bar caption background color (#0F172A -> BGR: 0x002A170F)
                    int captionColor = 0x002A170F;
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

                    // Window border color (#334155 -> BGR: 0x00554133)
                    int borderColor = 0x00554133;
                    DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

                    // Title text color (#F8FAFC -> BGR: 0x00FCFAF8)
                    int textColor = 0x00FCFAF8;
                    DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
                }
                catch
                {
                    // Ignore if not supported on older OS versions
                }
            };

            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                apply();
            }
            else
            {
                window.SourceInitialized += (s, e) => apply();
            }
        }

        // Color Palette
        public static readonly Color BgDark = Color.FromRgb(15, 23, 42);
        public static readonly Color SurfaceDark = Color.FromRgb(30, 41, 59);
        public static readonly Color SurfaceHover = Color.FromRgb(51, 65, 85);
        public static readonly Color BorderColor = Color.FromRgb(51, 65, 85);
        public static readonly Color PrimaryCyan = Color.FromRgb(6, 182, 212);
        public static readonly Color PrimaryHover = Color.FromRgb(8, 145, 178);
        public static readonly Color AccentViolet = Color.FromRgb(139, 92, 246);
        public static readonly Color TextMain = Color.FromRgb(248, 250, 252);
        public static readonly Color TextMuted = Color.FromRgb(148, 163, 184);
        public static readonly Color SuccessGreen = Color.FromRgb(16, 185, 129);
        public static readonly Color DangerRed = Color.FromRgb(239, 68, 68);

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static readonly SolidColorBrush _brushBgDark = CreateFrozenBrush(BgDark);
        private static readonly SolidColorBrush _brushSurfaceDark = CreateFrozenBrush(SurfaceDark);
        private static readonly SolidColorBrush _brushSurfaceHover = CreateFrozenBrush(SurfaceHover);
        private static readonly SolidColorBrush _brushBorder = CreateFrozenBrush(BorderColor);
        private static readonly SolidColorBrush _brushPrimaryCyan = CreateFrozenBrush(PrimaryCyan);
        private static readonly SolidColorBrush _brushPrimaryHover = CreateFrozenBrush(PrimaryHover);
        private static readonly SolidColorBrush _brushAccentViolet = CreateFrozenBrush(AccentViolet);
        private static readonly SolidColorBrush _brushTextMain = CreateFrozenBrush(TextMain);
        private static readonly SolidColorBrush _brushTextMuted = CreateFrozenBrush(TextMuted);
        private static readonly SolidColorBrush _brushSuccessGreen = CreateFrozenBrush(SuccessGreen);
        private static readonly SolidColorBrush _brushDangerRed = CreateFrozenBrush(DangerRed);

        public static SolidColorBrush BrushBgDark { get { return _brushBgDark; } }
        public static SolidColorBrush BrushSurfaceDark { get { return _brushSurfaceDark; } }
        public static SolidColorBrush BrushSurfaceHover { get { return _brushSurfaceHover; } }
        public static SolidColorBrush BrushBorder { get { return _brushBorder; } }
        public static SolidColorBrush BrushPrimaryCyan { get { return _brushPrimaryCyan; } }
        public static SolidColorBrush BrushPrimaryHover { get { return _brushPrimaryHover; } }
        public static SolidColorBrush BrushAccentViolet { get { return _brushAccentViolet; } }
        public static SolidColorBrush BrushTextMain { get { return _brushTextMain; } }
        public static SolidColorBrush BrushTextMuted { get { return _brushTextMuted; } }
        public static SolidColorBrush BrushSuccessGreen { get { return _brushSuccessGreen; } }
        public static SolidColorBrush BrushDangerRed { get { return _brushDangerRed; } }

        public static void ApplyGlobalTheme(Application app)
        {
            if (app == null) return;

            string xaml = @"<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
    <Style TargetType=""TabControl"">
        <Setter Property=""Background"" Value=""#0F172A""/>
        <Setter Property=""BorderBrush"" Value=""#334155""/>
        <Setter Property=""BorderThickness"" Value=""1""/>
        <Setter Property=""Padding"" Value=""12""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""TabControl"">
                    <Grid SnapsToDevicePixels=""True"" KeyboardNavigation.TabNavigation=""Local"">
                        <Grid.RowDefinitions>
                            <RowDefinition Height=""Auto""/>
                            <RowDefinition Height=""*""/>
                        </Grid.RowDefinitions>
                        <TabPanel Grid.Row=""0"" IsItemsHost=""True"" Margin=""0,0,0,0"" Panel.ZIndex=""1""/>
                        <Border Grid.Row=""1"" Background=""#1E293B"" BorderBrush=""#334155"" BorderThickness=""1"" CornerRadius=""0,8,8,8"" Padding=""{TemplateBinding Padding}"">
                            <ContentPresenter x:Name=""PART_SelectedContentHost"" ContentSource=""SelectedContent""/>
                        </Border>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType=""TabItem"">
        <Setter Property=""Background"" Value=""#1E293B""/>
        <Setter Property=""Foreground"" Value=""#94A3B8""/>
        <Setter Property=""BorderBrush"" Value=""#334155""/>
        <Setter Property=""FontSize"" Value=""13""/>
        <Setter Property=""FontWeight"" Value=""SemiBold""/>
        <Setter Property=""Padding"" Value=""18,10,18,10""/>
        <Setter Property=""Margin"" Value=""0,0,4,0""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""TabItem"">
                    <Border x:Name=""bd"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""1,1,1,0"" CornerRadius=""6,6,0,0"" Padding=""{TemplateBinding Padding}"" Margin=""{TemplateBinding Margin}"" TextElement.Foreground=""{TemplateBinding Foreground}"">
                        <ContentPresenter x:Name=""cp"" ContentSource=""Header"" HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsSelected"" Value=""True"">
                            <Setter TargetName=""bd"" Property=""Background"" Value=""#0F172A""/>
                            <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""#334155""/>
                            <Setter Property=""Foreground"" Value=""#F8FAFC""/>
                            <Setter Property=""FontWeight"" Value=""Bold""/>
                        </Trigger>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""bd"" Property=""Background"" Value=""#1E293B""/>
                            <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""#06B6D4""/>
                            <Setter Property=""Foreground"" Value=""#06B6D4""/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType=""Button"">
        <Setter Property=""Background"" Value=""#334155""/>
        <Setter Property=""Foreground"" Value=""#F8FAFC""/>
        <Setter Property=""BorderBrush"" Value=""#475569""/>
        <Setter Property=""BorderThickness"" Value=""1""/>
        <Setter Property=""Padding"" Value=""10,6,10,6""/>
        <Setter Property=""FontSize"" Value=""12.5""/>
        <Setter Property=""FontWeight"" Value=""Medium""/>
        <Setter Property=""Cursor"" Value=""Hand""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""Button"">
                    <Border x:Name=""btnBorder"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""5"" Padding=""{TemplateBinding Padding}"" TextElement.Foreground=""{TemplateBinding Foreground}"">
                        <ContentPresenter x:Name=""btnContent"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" RecognizesAccessKey=""True""/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""btnBorder"" Property=""Background"" Value=""#06B6D4""/>
                            <Setter TargetName=""btnBorder"" Property=""BorderBrush"" Value=""#06B6D4""/>
                            <Setter Property=""Foreground"" Value=""#0F172A""/>
                            <Setter Property=""FontWeight"" Value=""Bold""/>
                        </Trigger>
                        <Trigger Property=""IsPressed"" Value=""True"">
                            <Setter TargetName=""btnBorder"" Property=""Background"" Value=""#0891B2""/>
                            <Setter TargetName=""btnBorder"" Property=""BorderBrush"" Value=""#0891B2""/>
                            <Setter Property=""Foreground"" Value=""#0F172A""/>
                        </Trigger>
                        <Trigger Property=""IsEnabled"" Value=""False"">
                            <Setter TargetName=""btnBorder"" Property=""Background"" Value=""#1E293B""/>
                            <Setter TargetName=""btnBorder"" Property=""BorderBrush"" Value=""#334155""/>
                            <Setter Property=""Foreground"" Value=""#64748B""/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style x:Key=""NavButtonStyle"" TargetType=""Button"">
        <Setter Property=""Background"" Value=""#1E293B""/>
        <Setter Property=""Foreground"" Value=""#94A3B8""/>
        <Setter Property=""FontSize"" Value=""14""/>
        <Setter Property=""FontWeight"" Value=""Medium""/>
        <Setter Property=""Height"" Value=""44""/>
        <Setter Property=""Margin"" Value=""0,0,0,6""/>
        <Setter Property=""Padding"" Value=""14,0,14,0""/>
        <Setter Property=""HorizontalContentAlignment"" Value=""Left""/>
        <Setter Property=""Cursor"" Value=""Hand""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""Button"">
                    <Border x:Name=""bd"" Background=""{TemplateBinding Background}"" CornerRadius=""6"" Padding=""{TemplateBinding Padding}"" TextElement.Foreground=""{TemplateBinding Foreground}"">
                        <ContentPresenter HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" VerticalAlignment=""Center""/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""bd"" Property=""Background"" Value=""#334155""/>
                            <Setter Property=""Foreground"" Value=""#06B6D4""/>
                        </Trigger>
                        <Trigger Property=""Tag"" Value=""Active"">
                            <Setter TargetName=""bd"" Property=""Background"" Value=""#06B6D4""/>
                            <Setter Property=""Foreground"" Value=""#0F172A""/>
                            <Setter Property=""FontWeight"" Value=""Bold""/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType=""ScrollBar"">
        <Setter Property=""Background"" Value=""#0F172A""/>
        <Setter Property=""Foreground"" Value=""#38BDF8""/>
        <Setter Property=""BorderBrush"" Value=""#334155""/>
        <Setter Property=""BorderThickness"" Value=""1""/>
        <Setter Property=""Stylus.IsPressAndHoldEnabled"" Value=""False""/>
        <Setter Property=""Stylus.IsFlicksEnabled"" Value=""False""/>
        <Setter Property=""Width"" Value=""16""/>
        <Setter Property=""Height"" Value=""Auto""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""ScrollBar"">
                    <Grid x:Name=""Bg"" Background=""#0F172A"" SnapsToDevicePixels=""True"">
                        <Track x:Name=""PART_Track"" Orientation=""{TemplateBinding Orientation}"" IsDirectionReversed=""True"">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton x:Name=""btnPageUp"" Command=""{x:Static ScrollBar.PageUpCommand}"" Background=""Transparent"" BorderThickness=""0"" Focusable=""False""/>
                            </Track.DecreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb x:Name=""Thumb"" MinHeight=""28"" MinWidth=""28"">
                                    <Thumb.Template>
                                        <ControlTemplate TargetType=""Thumb"">
                                            <Border x:Name=""thumbBorder"" Background=""#38BDF8"" CornerRadius=""4"" Margin=""2""/>
                                            <ControlTemplate.Triggers>
                                                <Trigger Property=""IsMouseOver"" Value=""True"">
                                                    <Setter TargetName=""thumbBorder"" Property=""Background"" Value=""#06B6D4""/>
                                                </Trigger>
                                                <Trigger Property=""IsDragging"" Value=""True"">
                                                    <Setter TargetName=""thumbBorder"" Property=""Background"" Value=""#22D3EE""/>
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton x:Name=""btnPageDown"" Command=""{x:Static ScrollBar.PageDownCommand}"" Background=""Transparent"" BorderThickness=""0"" Focusable=""False""/>
                            </Track.IncreaseRepeatButton>
                        </Track>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""Orientation"" Value=""Horizontal"">
                            <Setter Property=""Width"" Value=""Auto""/>
                            <Setter Property=""Height"" Value=""16""/>
                            <Setter TargetName=""PART_Track"" Property=""IsDirectionReversed"" Value=""False""/>
                            <Setter TargetName=""btnPageUp"" Property=""Command"" Value=""{x:Static ScrollBar.PageLeftCommand}""/>
                            <Setter TargetName=""btnPageDown"" Property=""Command"" Value=""{x:Static ScrollBar.PageRightCommand}""/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <ControlTemplate x:Key=""ComboBoxToggleButtonTemplate"" TargetType=""ToggleButton"">
        <Border x:Name=""border"" CornerRadius=""4"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"">
            <Path x:Name=""arrow"" HorizontalAlignment=""Right"" Margin=""0,0,10,0"" VerticalAlignment=""Center"" Data=""M 0 0 L 4 4 L 8 0 Z"" Fill=""#94A3B8""/>
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter TargetName=""border"" Property=""Background"" Value=""#334155""/>
                <Setter TargetName=""border"" Property=""BorderBrush"" Value=""#06B6D4""/>
                <Setter TargetName=""arrow"" Property=""Fill"" Value=""#F8FAFC""/>
            </Trigger>
            <Trigger Property=""IsChecked"" Value=""True"">
                <Setter TargetName=""border"" Property=""Background"" Value=""#0F172A""/>
                <Setter TargetName=""border"" Property=""BorderBrush"" Value=""#06B6D4""/>
                <Setter TargetName=""arrow"" Property=""Fill"" Value=""#06B6D4""/>
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>
    <ControlTemplate x:Key=""CustomComboBoxTemplate"" TargetType=""ComboBox"">
        <Grid SnapsToDevicePixels=""True"">
            <ToggleButton x:Name=""toggleButton"" Template=""{StaticResource ComboBoxToggleButtonTemplate}"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" Focusable=""False"" IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}""/>
            <ContentPresenter x:Name=""contentPresenter"" Content=""{TemplateBinding SelectionBoxItem}"" ContentTemplate=""{TemplateBinding SelectionBoxItemTemplate}"" ContentTemplateSelector=""{TemplateBinding ItemTemplateSelector}"" Margin=""10,6,30,6"" VerticalAlignment=""Center"" HorizontalAlignment=""Left"" IsHitTestVisible=""False"" TextElement.Foreground=""{TemplateBinding Foreground}""/>
            <Popup x:Name=""PART_Popup"" AllowsTransparency=""True"" IsOpen=""{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}}"" Placement=""Bottom"" PopupAnimation=""None"">
                <Border x:Name=""DropDownBorder"" Background=""#0F172A"" BorderBrush=""#06B6D4"" BorderThickness=""1"" CornerRadius=""4"" Margin=""0,2,0,0"" MinWidth=""{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"">
                    <ScrollViewer SnapsToDevicePixels=""True"">
                        <StackPanel IsItemsHost=""True"" KeyboardNavigation.DirectionalNavigation=""Contained""/>
                    </ScrollViewer>
                </Border>
            </Popup>
        </Grid>
    </ControlTemplate>
    <Style TargetType=""ComboBox"">
        <Setter Property=""Background"" Value=""#1E293B""/>
        <Setter Property=""Foreground"" Value=""#F8FAFC""/>
        <Setter Property=""BorderBrush"" Value=""#334155""/>
        <Setter Property=""BorderThickness"" Value=""1""/>
        <Setter Property=""Padding"" Value=""8,6,8,6""/>
        <Setter Property=""FontSize"" Value=""13""/>
        <Setter Property=""Template"" Value=""{StaticResource CustomComboBoxTemplate}""/>
    </Style>
    <Style TargetType=""ComboBoxItem"">
        <Setter Property=""Background"" Value=""#0F172A""/>
        <Setter Property=""Foreground"" Value=""#F8FAFC""/>
        <Setter Property=""Padding"" Value=""10,8,10,8""/>
        <Setter Property=""FontSize"" Value=""13""/>
        <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""ComboBoxItem"">
                    <Border x:Name=""itemBd"" Background=""{TemplateBinding Background}"" Padding=""{TemplateBinding Padding}"" CornerRadius=""3"" Margin=""1"">
                        <ContentPresenter HorizontalAlignment=""Left"" VerticalAlignment=""Center"" TextElement.Foreground=""{TemplateBinding Foreground}""/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsHighlighted"" Value=""True"">
                            <Setter TargetName=""itemBd"" Property=""Background"" Value=""#334155""/>
                            <Setter Property=""Foreground"" Value=""#06B6D4""/>
                        </Trigger>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""itemBd"" Property=""Background"" Value=""#334155""/>
                            <Setter Property=""Foreground"" Value=""#06B6D4""/>
                        </Trigger>
                        <Trigger Property=""IsSelected"" Value=""True"">
                            <Setter TargetName=""itemBd"" Property=""Background"" Value=""#06B6D4""/>
                            <Setter Property=""Foreground"" Value=""#0F172A""/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType=""PasswordBox"">
        <Setter Property=""Background"" Value=""#0F172A""/>
        <Setter Property=""Foreground"" Value=""#F8FAFC""/>
        <Setter Property=""BorderBrush"" Value=""#334155""/>
        <Setter Property=""BorderThickness"" Value=""1""/>
        <Setter Property=""Padding"" Value=""8,6,8,6""/>
        <Setter Property=""FontSize"" Value=""13""/>
        <Setter Property=""MinHeight"" Value=""32""/>
        <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
    </Style>
    <Style TargetType=""TextBox"">
        <Setter Property=""Background"" Value=""#0F172A""/>
        <Setter Property=""Foreground"" Value=""#F8FAFC""/>
        <Setter Property=""BorderBrush"" Value=""#334155""/>
        <Setter Property=""BorderThickness"" Value=""1""/>
        <Setter Property=""Padding"" Value=""6,4,6,4""/>
        <Setter Property=""FontSize"" Value=""13""/>
        <Setter Property=""MinHeight"" Value=""32""/>
    </Style>
    <Style TargetType=""ListView"">
        <Setter Property=""Background"" Value=""#0F172A""/>
        <Setter Property=""Foreground"" Value=""#F8FAFC""/>
        <Setter Property=""BorderBrush"" Value=""#334155""/>
        <Setter Property=""BorderThickness"" Value=""1""/>
    </Style>
    <Style TargetType=""GridViewColumnHeader"">
        <Setter Property=""Background"" Value=""#1E293B""/>
        <Setter Property=""Foreground"" Value=""#94A3B8""/>
        <Setter Property=""BorderBrush"" Value=""#334155""/>
        <Setter Property=""BorderThickness"" Value=""0,0,1,1""/>
        <Setter Property=""Padding"" Value=""10,8,10,8""/>
        <Setter Property=""FontSize"" Value=""12""/>
        <Setter Property=""FontWeight"" Value=""SemiBold""/>
        <Setter Property=""HorizontalContentAlignment"" Value=""Left""/>
        <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""GridViewColumnHeader"">
                    <Grid SnapsToDevicePixels=""True"">
                        <Border x:Name=""headerBorder"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" Padding=""{TemplateBinding Padding}"">
                            <ContentPresenter HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" VerticalAlignment=""{TemplateBinding VerticalContentAlignment}"" RecognizesAccessKey=""True""/>
                        </Border>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""headerBorder"" Property=""Background"" Value=""#334155""/>
                            <Setter Property=""Foreground"" Value=""#F8FAFC""/>
                        </Trigger>
                        <Trigger Property=""IsPressed"" Value=""True"">
                            <Setter TargetName=""headerBorder"" Property=""Background"" Value=""#0F172A""/>
                            <Setter Property=""Foreground"" Value=""#06B6D4""/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>
";

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml)))
                {
                    var resDict = (ResourceDictionary)XamlReader.Load(stream);
                    app.Resources.MergedDictionaries.Add(resDict);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Theme Xaml Load Error: " + ex.Message);
            }
        }

        private static Button CreateButtonInternal(string content, RoutedEventHandler onClick, SolidColorBrush bg, SolidColorBrush fg, FontWeight fontWeight, SolidColorBrush borderBrush = null, Thickness? borderThickness = null)
        {
            var btn = new Button
            {
                Content = content,
                Background = bg,
                Foreground = fg,
                FontWeight = fontWeight,
                BorderBrush = borderBrush ?? Brushes.Transparent,
                BorderThickness = borderThickness ?? new Thickness(0),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 12.5,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0)
            };
            if (onClick != null)
            {
                btn.Click += onClick;
            }
            return btn;
        }

        public static Button CreatePrimaryButton(string content, RoutedEventHandler onClick)
        {
            return CreateButtonInternal(content, onClick, BrushPrimaryCyan, Brushes.Black, FontWeights.SemiBold);
        }

        public static Button CreateSecondaryButton(string content, RoutedEventHandler onClick)
        {
            return CreateButtonInternal(content, onClick, BrushSurfaceHover, BrushTextMain, FontWeights.Medium, BrushBorder, new Thickness(1));
        }

        public static Button CreateDangerButton(string content, RoutedEventHandler onClick)
        {
            return CreateButtonInternal(content, onClick, BrushDangerRed, Brushes.White, FontWeights.SemiBold);
        }

        public static TextBox CreateStyledTextBox(string defaultText, bool isReadOnly)
        {
            return new TextBox
            {
                Text = defaultText ?? "",
                IsReadOnly = isReadOnly,
                MinHeight = 32,
                Background = BrushBgDark,
                Foreground = BrushTextMain,
                BorderBrush = BrushBorder,
                BorderThickness = new Thickness(1),
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        public static TextBox CreateStyledTextBox(string defaultText)
        {
            return CreateStyledTextBox(defaultText, false);
        }

        public static TextBox CreateStyledTextBox()
        {
            return CreateStyledTextBox("", false);
        }

        public static PasswordBox CreateStyledPasswordBox()
        {
            return new PasswordBox
            {
                MinHeight = 32,
                Height = 36,
                Background = BrushBgDark,
                Foreground = BrushTextMain,
                BorderBrush = BrushBorder,
                BorderThickness = new Thickness(1),
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 6, 8, 6),
                CaretBrush = BrushPrimaryCyan
            };
        }

        public static Border CreateBadge(string text, Color bg, Color fg)
        {
            return new Border
            {
                Background = new SolidColorBrush(bg),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(fg),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                }
            };
        }

        public static Border CreateCardPanel(UIElement content, double padding)
        {
            return new Border
            {
                Background = BrushSurfaceDark,
                BorderBrush = BrushBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(padding),
                Margin = new Thickness(6),
                Child = content
            };
        }

        public static Border CreateCardPanel(UIElement content)
        {
            return CreateCardPanel(content, 16);
        }

        public static void CopyTextToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            System.Windows.Clipboard.SetText(text);
            ShowInfoModal(null, "クリップボードにコピーしました！", "成功");
        }

        public static void SaveTextToFile(string text, string defaultName)
        {
            if (string.IsNullOrEmpty(text)) return;
            var sfd = new Microsoft.Win32.SaveFileDialog { FileName = defaultName };
            if (sfd.ShowDialog() == true)
            {
                File.WriteAllText(sfd.FileName, text);
                ShowInfoModal(null, "ファイルに保存しました！", "成功");
            }
        }

        public static bool ShowConfirmModal(Window owner, string message, string title = "削除の確認")
        {
            var dlg = new Window
            {
                Title = title,
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                Background = BrushBgDark,
                Foreground = BrushTextMain,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            ApplyDarkTitleBar(dlg);

            var mainStack = new StackPanel { Margin = new Thickness(22) };

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            var iconBadge = CreateBadge("⚠️ " + title, DangerRed, Colors.White);
            iconBadge.Margin = new Thickness(0, 0, 10, 0);
            headerPanel.Children.Add(iconBadge);

            mainStack.Children.Add(headerPanel);

            var txtMsg = new TextBlock
            {
                Text = message,
                FontSize = 13.5,
                Foreground = BrushTextMain,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 22)
            };
            mainStack.Children.Add(txtMsg);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var btnYes = CreateDangerButton("はい (Y)", (s, e) => { dlg.DialogResult = true; dlg.Close(); });
            btnYes.IsDefault = true;
            btnYes.MinWidth = 85;

            var btnNo = CreateSecondaryButton("いいえ (N)", (s, e) => { dlg.DialogResult = false; dlg.Close(); });
            btnNo.IsCancel = true;
            btnNo.MinWidth = 85;

            btnPanel.Children.Add(btnYes);
            btnPanel.Children.Add(btnNo);

            mainStack.Children.Add(btnPanel);
            dlg.Content = mainStack;

            return dlg.ShowDialog() == true;
        }

        public static void ShowInfoModal(Window owner, string message, string title = "通知")
        {
            var dlg = new Window
            {
                Title = title,
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                Background = BrushBgDark,
                Foreground = BrushTextMain,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            ApplyDarkTitleBar(dlg);

            var mainStack = new StackPanel { Margin = new Thickness(22) };

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            var iconBadge = CreateBadge("ℹ️ " + title, PrimaryCyan, Colors.Black);
            iconBadge.Margin = new Thickness(0, 0, 10, 0);
            headerPanel.Children.Add(iconBadge);

            mainStack.Children.Add(headerPanel);

            var txtMsg = new TextBlock
            {
                Text = message,
                FontSize = 13.5,
                Foreground = BrushTextMain,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 22)
            };
            mainStack.Children.Add(txtMsg);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = CreatePrimaryButton("OK", (s, e) => { dlg.DialogResult = true; dlg.Close(); });
            btnOk.IsDefault = true;
            btnOk.MinWidth = 85;

            btnPanel.Children.Add(btnOk);
            mainStack.Children.Add(btnPanel);
            dlg.Content = mainStack;

            dlg.ShowDialog();
        }
    }
}
