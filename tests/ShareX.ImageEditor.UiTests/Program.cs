#region License Information (GPL v3)

/*
    ShareX.ImageEditor - The UI-agnostic Editor library for ShareX
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShareX.ImageEditor.Hosting;
using ShareX.ImageEditor.Presentation.Theming;
using ShareX.ImageEditor.Presentation.ViewModels;
using ShareX.ImageEditor.Presentation.Views;
using SkiaSharp;

// Real Skia fonts and production Application.Initialize; no desktop server is needed.
// Run the two applications in separate processes so their resources cannot mask one another.
AppBuilder builder = args.Contains("--library-host")
    ? AppBuilder.Configure<AvaloniaApp>()
    : AppBuilder.Configure<ShareX.ImageEditor.App.App>();
builder.WithInterFont()
    .UseStandardRuntimePlatformSubsystem()
    .UseHarfBuzz()
    .UseWindowingSubsystem(() => AvaloniaLocator.CurrentMutable.Bind<ICursorFactory>().ToConstant(new TestCursorFactory()), "Test")
    .UseSkia()
    .SetupWithoutStarting();

var cases = new (string Name, Action Run)[]
{
    ("Embedded Lucide asset, metadata and glyphs resolve without system installation", EmbeddedFont),
    ("Production application initializes shared font and theme resources", ApplicationResources),
    ("Toolbar icons and viewport survive image load and layout changes", EditorLayout)
};
foreach (var test in cases)
{
    test.Run();
    Console.WriteLine($"PASS: {test.Name}");
}
Console.WriteLine($"{cases.Length} UI regression cases passed ({(args.Contains("--library-host") ? "library host" : "standalone")}).");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static IEnumerable<(string Name, string Text)> Icons() => typeof(EditorIcons)
    .GetFields(BindingFlags.Public | BindingFlags.Static)
    .Where(f => f.IsLiteral && f.FieldType == typeof(string))
    .Select(f => (f.Name, (string)f.GetRawConstantValue()!));

static void EmbeddedFont()
{
    var uri = new Uri("avares://ShareX.ImageEditor/Assets/lucide.ttf");
    Check(AssetLoader.Exists(uri), "Lucide was not embedded in the library assembly");
    using var stream = AssetLoader.Open(uri);
    using var skiaTypeface = SKTypeface.FromStream(stream);
    Check(skiaTypeface?.FamilyName == "lucide", "Embedded font family metadata changed");
    Check(FontManager.Current.TryGetGlyphTypeface(new Typeface("avares://ShareX.ImageEditor/Assets#lucide"), out var glyphs),
        "Avalonia FontManager could not resolve the embedded font");
    Check(glyphs!.FamilyName == "lucide", "FontManager fell back to another family");
    foreach (var icon in Icons())
        foreach (var rune in icon.Text.EnumerateRunes())
            Check(glyphs.CharacterToGlyphMap.TryGetGlyph(rune.Value, out var glyph) && glyph != 0,
                $"Missing {icon.Name} glyph U+{rune.Value:X4}");
}

static void ApplicationResources()
{
    var app = Application.Current!;
    foreach (var theme in new[] { ThemeManager.ShareXDark, ThemeManager.ShareXLight })
    {
        Check(app.TryGetResource("ShareX.FontFamily.Icon", theme, out var font) && font is FontFamily,
            "Application did not load ShareX.FontFamily.Icon");
        Check(FontManager.Current.TryGetGlyphTypeface(new Typeface((FontFamily)font!), out var glyphs)
            && glyphs.FamilyName == "lucide", "Application icon font does not resolve to lucide");
        foreach (string key in new[] { "ShareX.Brush.Background.Main", "ShareX.Brush.Background.Toolbar",
            "ShareX.Brush.Background.Popup", "ShareX.Brush.Text", "ShareX.Brush.Border" })
            Check(app.TryGetResource(key, theme, out var value) && value is IBrush, $"Missing {key} in {theme}");
    }
}

static void EditorLayout()
{
    using var vm = new MainViewModel { ShowStartScreen = false, ShowFileMenu = true, ShowTaskButtons = true };
    var view = new EditorView { DataContext = vm };
    var root = new TestRoot();
    root.Child = view;
    ((ILogical)root).NotifyAttachedToLogicalTree(new LogicalTreeAttachmentEventArgs(root, root, root));
    try
    {
        Settle("empty", 1000, 700);
        // A large opaque image forces both scroll axes, unlike an empty-editor layout probe.
        var image = new SKBitmap(3000, 2000);
        image.Erase(SKColors.CornflowerBlue);
        vm.UpdatePreview(image); // Ownership transferred.
        foreach (double zoom in new[] { 0.25, 1, 2.5 })
        {
            vm.Zoom = zoom;
            Settle($"image zoom {zoom}", 1000, 700);
            var scroll = view.FindControl<ScrollViewer>("CanvasScrollViewer")!;
            scroll.Offset = new Vector(500, 300);
            Settle($"scrolled zoom {zoom}", 1000, 700);
        }
        Settle("narrow", 320, 240);
        Settle("restored", 1400, 900);
        vm.ShowToolbars = false;
        Settle("hidden", 1000, 700);
        vm.ShowToolbars = true;
        Settle("shown", 1000, 700);
        view.UseBuiltInToolbars = false;
        Settle("external toolbars", 1000, 700);
        view.UseBuiltInToolbars = true;
        Settle("recreated", 1000, 700);
        foreach (var theme in new[] { ThemeManager.ShareXLight, ThemeManager.ShareXDark })
        {
            ThemeManager.SetTheme(theme);
            Settle($"theme {theme}", 1000, 700);
        }
    }
    finally
    {
        view.DisposeWorkspace();
        root.Child = null;
    }

    void Settle(string name, double width, double height)
    {
        root.ClientSize = new Size(width, height);
        for (int i = 0; i < 5; i++)
        {
            root.Measure(root.ClientSize);
            root.Arrange(new Rect(root.ClientSize));
            Dispatcher.UIThread.RunJobs();
        }
        var top = view.FindControl<ContentControl>("BuiltInToolbarsHost")!;
        var bottom = view.FindControl<ContentControl>("BottomToolbarHost")!;
        var scroll = view.FindControl<ScrollViewer>("CanvasScrollViewer")!;
        double viewportY = scroll.TranslatePoint(default, view)!.Value.Y;
        double topBottom = top.TranslatePoint(default, view)!.Value.Y + top.Bounds.Height;
        double bottomY = bottom.TranslatePoint(default, view)!.Value.Y;
        bool barsVisible = view.UseBuiltInToolbars && vm.ShowToolbars;
        Check(scroll.Bounds.Width > 0 && scroll.Bounds.Height > 0, $"{name}: empty viewport");
        Check(viewportY >= topBottom && viewportY + scroll.Bounds.Height <= bottomY,
            $"{name}: viewport overlaps toolbar rows");
        Check(((Control)scroll.Parent!).ClipToBounds, $"{name}: image/overlay host can paint over toolbar rows");
        Check(((Control)scroll.Parent!.Parent!).ClipToBounds, $"{name}: viewport row does not contain overflowing descendants");
        Check(barsVisible ? top.Bounds.Height > 0 && bottom.Bounds.Height > 0
            : top.Bounds.Height == 0 && bottom.Bounds.Height == 0, $"{name}: incorrect toolbar visibility");
        var icons = view.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Text?.Any(c => c >= '\uE000' && c <= '\uF8FF') == true).ToArray();
        if (barsVisible) Check(icons.Length >= 10, $"{name}: toolbar icons were not created");
        foreach (var icon in icons)
        {
            Check(FontManager.Current.TryGetGlyphTypeface(new Typeface(icon.FontFamily, icon.FontStyle, icon.FontWeight), out var glyphs)
                && glyphs.FamilyName == "lucide", $"{name}: icon {icon.Text} uses {icon.FontFamily}, not lucide");
            foreach (var rune in icon.Text!.EnumerateRunes())
                Check(glyphs!.CharacterToGlyphMap.TryGetGlyph(rune.Value, out var glyph) && glyph != 0,
                    $"{name}: visible icon has missing glyph U+{rune.Value:X4}");
        }
        if (barsVisible)
        {
            var toolbar = view.GetVisualDescendants().OfType<EditorBuiltInToolbars>().Single();
            Check(ReferenceEquals(toolbar.AnnotationToolbar.DataContext, vm.ToolbarAdapter), $"{name}: toolbar adapter binding lost");
            foreach (var button in toolbar.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("toolbar-button") && b.Content is string))
            {
                var text = button.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == (string)button.Content!);
                Check(text.Bounds.Width >= text.FontSize && text.Bounds.Height >= text.FontSize,
                    $"{name}: compact button clips its {text.FontSize}px icon to {text.Bounds.Size}");
            }
            Check(bottom.GetVisualDescendants().OfType<Button>().Any(b => ReferenceEquals(b.Command, vm.SaveCommand)),
                $"{name}: bottom Save binding lost");
        }
        // Use the real renderer/fonts too; the test platform does not mock glyph metrics.
        using var frame = new RenderTargetBitmap(new PixelSize((int)width, (int)height));
        frame.Render(view);
        if (Environment.GetEnvironmentVariable("SHAREX_UI_TEST_OUTPUT_DIR") is { Length: > 0 } outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            frame.Save(Path.Combine(outputDirectory, name.Replace(' ', '-') + ".png"), PngBitmapEncoderOptions.Default);
        }
        Console.WriteLine($"  {name}: rows {top.Bounds.Height:F1}/{scroll.Bounds.Height:F1}/{bottom.Bounds.Height:F1}; {icons.Length} icons");
    }
}
