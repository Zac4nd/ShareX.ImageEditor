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

using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Core.Editor;
using SkiaSharp;

// Deliberately framework-free: a failing assertion exits nonzero. No desktop is needed.
var cases = new (string Name, Action Run)[]
{
    ("No image and invalid rectangles are no-ops", InvalidRegions),
    ("Reverse fractional edges round outward and clamp", PixelBounds),
    ("Copy excludes annotations and preserves source/history at every core zoom", CopyPixels),
    ("Extracted pixels and clipboard clones own independent bitmaps", IndependentCopies),
    ("Cut converts opaque input to alpha and touches only selected pixels", TransparentCut),
    ("Cut, paste, move and cancel share ordered undo/redo", UnifiedHistory),
    ("A new cut after undo discards the redo branch", RedoBranch)
};
foreach (var test in cases)
{
    test.Run();
    Console.WriteLine($"PASS: {test.Name}");
}
Console.WriteLine($"{cases.Length} raster regression cases passed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static SKColor Pixel(int x, int y) => new((byte)(x * 30), (byte)(y * 40), 180, 255);

static EditorCore CreateEditor()
{
    // Opaque source models JPEG input: clearing must still produce alpha-capable pixels.
    var source = new SKBitmap(6, 5, SKColorType.Bgra8888, SKAlphaType.Opaque);
    for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++) source.SetPixel(x, y, Pixel(x, y));
    var editor = new EditorCore();
    editor.LoadImage(source); // Core takes ownership.
    return editor;
}

static void InvalidRegions()
{
    using var empty = new EditorCore();
    Check(empty.CopyRasterRegion(new SKRect(0, 0, 2, 2)) == null, "Copy without source");
    Check(!empty.ClearRasterRegion(new SKRect(0, 0, 2, 2)), "Cut without source");
    using var editor = CreateEditor();
    foreach (var region in new[]
    {
        new SKRect(1, 1, 1, 3), new SKRect(2, 2, 4, 2),
        new SKRect(-5, -5, -1, -1), new SKRect(6, 0, 8, 4),
        new SKRect(float.NaN, 0, 2, 2), new SKRect(0, 0, float.PositiveInfinity, 2)
    })
    {
        Check(editor.CopyRasterRegion(region) == null, "Invalid region copied");
        Check(!editor.ClearRasterRegion(region), "Invalid region cut");
    }
    Check(!editor.CanUndo && !editor.CanRedo, "No-op created history");
    Check(editor.SourceImage!.GetPixel(0, 0) == Pixel(0, 0), "No-op mutated source");
}

static void PixelBounds()
{
    using var editor = CreateEditor();
    Check(editor.TryGetRasterBounds(new SKRect(4.2f, 3.1f, -2, 1.2f), out var bounds), "Reverse drag rejected");
    Check(bounds == new SKRectI(0, 1, 5, 4), $"Wrong rounded bounds: {bounds}");
    Check(editor.TryGetRasterBounds(new SKRect(-10, -10, 90, 90), out bounds), "Oversize region rejected");
    Check(bounds == new SKRectI(0, 0, 6, 5), "Image bounds not enforced");
    using var pixels = editor.CopyRasterRegion(new SKRect(4.2f, 3.1f, -2, 1.2f))!;
    Check(pixels.Width == 5 && pixels.Height == 3, "Incorrect extraction size");
    Check(pixels.GetPixel(0, 0) == Pixel(0, 1) && pixels.GetPixel(4, 2) == Pixel(4, 3), "Incorrect extraction coordinates");
}

static void CopyPixels()
{
    using var editor = CreateEditor();
    var step = new NumberAnnotation { Number = 1, StartPoint = new SKPoint(2, 1), EndPoint = new SKPoint(4, 3) };
    editor.AddAnnotation(step);
    editor.Undo();
    Check(editor.CanRedo, "Fixture must have redo history");
    foreach (float zoom in new[] { 0.25f, 1, 2.5f })
    {
        editor.Zoom = zoom;
        using var pixels = editor.CopyRasterRegion(new SKRect(1, 1, 3, 4))!;
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 2; x++) Check(pixels.GetPixel(x, y) == Pixel(x + 1, y + 1), "Copy changed with core zoom");
        Check(editor.CanRedo && !editor.CanUndo, "Copy modified history");
    }
    editor.Redo();
    using var background = editor.CopyRasterRegion(new SKRect(2, 1, 4, 3))!;
    Check(background.GetPixel(0, 0) == Pixel(2, 1), "Annotation flattened into copied background");
    Check(editor.Annotations.Single().Id == step.Id, "Copy removed annotation");
    Check(editor.SourceImage!.GetPixel(2, 1) == Pixel(2, 1), "Copy destroyed source pixels");
}

static void IndependentCopies()
{
    using var editor = CreateEditor();
    using var pixels = editor.CopyRasterRegion(new SKRect(1, 1, 3, 3))!;
    using var annotation = new ImageAnnotation();
    annotation.SetImage(pixels.Copy());
    using var clone = (ImageAnnotation)annotation.Clone();
    pixels.SetPixel(0, 0, SKColors.Red);
    annotation.ImageBitmap!.SetPixel(0, 0, SKColors.Blue);
    Check(editor.SourceImage!.GetPixel(1, 1) == Pixel(1, 1), "Copied bitmap aliases source");
    Check(clone.ImageBitmap!.GetPixel(0, 0) == Pixel(1, 1), "Pasted clone aliases clipboard");
    annotation.Dispose();
    Check(clone.ImageBitmap.GetPixel(1, 1) == Pixel(2, 2), "Clipboard disposal invalidated paste");
    using var alpha = new SKBitmap(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul);
    alpha.Erase(new SKColor(255, 0, 0, 128));
    using var alphaEditor = new EditorCore();
    alphaEditor.LoadImage(alpha.Copy());
    using var alphaCopy = alphaEditor.CopyRasterRegion(new SKRect(0, 0, 2, 2))!;
    Check(alphaCopy.GetPixel(1, 1) == new SKColor(255, 0, 0, 128), "Copy lost partial alpha");
}

static void TransparentCut()
{
    using var editor = CreateEditor();
    var arrow = new ArrowAnnotation { StartPoint = new SKPoint(1, 1), EndPoint = new SKPoint(4, 3) };
    editor.AddAnnotation(arrow);
    int visualRefreshes = 0;
    editor.AnnotationsRestored += () => visualRefreshes++;
    Check(editor.ClearRasterRegion(new SKRect(1, 1, 4, 3)), "Cut failed");
    Check(visualRefreshes == 1, "Cut did not request bitmap-backed annotation visual refresh");
    Check(editor.SourceImage!.AlphaType != SKAlphaType.Opaque, "Opaque source cannot represent cut");
    for (int y = 0; y < 5; y++)
        for (int x = 0; x < 6; x++)
            Check(x >= 1 && x < 4 && y >= 1 && y < 3
                ? editor.SourceImage.GetPixel(x, y).Alpha == 0
                : editor.SourceImage.GetPixel(x, y) == Pixel(x, y), $"Unexpected pixel at {x},{y}");
    Check(editor.Annotations.Single().Id == arrow.Id, "Cut removed annotations");
    editor.Undo();
    Check(editor.SourceImage.GetPixel(2, 2) == Pixel(2, 2), "Undo cut did not restore source");
    editor.Redo();
    Check(editor.SourceImage.GetPixel(2, 2).Alpha == 0, "Redo cut did not restore transparency");
}

static void UnifiedHistory()
{
    using var editor = CreateEditor();
    editor.ActiveTool = EditorTool.RasterSelect;
    editor.AddAnnotation(new NumberAnnotation { Number = 1 });
    using var pixels = editor.CopyRasterRegion(new SKRect(1, 1, 3, 3))!;
    editor.ClearRasterRegion(new SKRect(1, 1, 3, 3));
    var paste = new ImageAnnotation { StartPoint = new SKPoint(1, 1), EndPoint = new SKPoint(3, 3) };
    paste.SetImage(pixels.Copy());
    editor.ActiveTool = EditorTool.Select;
    editor.AddAnnotation(paste);
    editor.RecordAnnotationChange();
    paste.StartPoint = new SKPoint(3, 2);
    paste.EndPoint = new SKPoint(5, 4);
    editor.Undo();
    Check(editor.Annotations.OfType<ImageAnnotation>().Single().StartPoint == new SKPoint(1, 1), "Undo move");
    editor.Redo();
    Check(editor.Annotations.OfType<ImageAnnotation>().Single().StartPoint == new SKPoint(3, 2), "Redo move");
    // Escape follows the normal annotation deletion path; the preceding cut remains separate.
    editor.RemoveAnnotation(editor.Annotations.OfType<ImageAnnotation>().Single());
    Check(editor.Annotations.Count == 1 && editor.SourceImage!.GetPixel(1, 1).Alpha == 0, "Cancel reverted cut or removed other annotations");
    editor.Undo(); // Undo cancel.
    Check(editor.Annotations.OfType<ImageAnnotation>().Single().ImageBitmap!.GetPixel(0, 0) == Pixel(1, 1), "Undo cancel lost pixels");
    editor.Undo(); // Undo move.
    editor.Undo(); // Undo paste.
    Check(editor.Annotations.Count == 1 && editor.SourceImage!.GetPixel(1, 1).Alpha == 0, "Undo paste reverted cut");
    editor.Undo(); // Undo cut.
    Check(editor.SourceImage!.GetPixel(1, 1) == Pixel(1, 1), "Undo cut");
    editor.Redo();
    editor.Redo();
    Check(editor.Annotations.Count == 2 && editor.SourceImage.GetPixel(1, 1).Alpha == 0, "Redo cut/paste");
}

static void RedoBranch()
{
    using var editor = CreateEditor();
    editor.ClearRasterRegion(new SKRect(1, 1, 3, 3));
    editor.Undo();
    Check(editor.CanRedo, "Missing redo after cut undo");
    editor.ClearRasterRegion(new SKRect(4, 3, 6, 5));
    Check(!editor.CanRedo && editor.SourceImage!.GetPixel(1, 1) == Pixel(1, 1), "New cut did not replace redo branch");
}
