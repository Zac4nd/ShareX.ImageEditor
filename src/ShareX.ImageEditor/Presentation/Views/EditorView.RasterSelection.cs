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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Hosting;
using ShareX.ImageEditor.Presentation.Rendering;
using ShareX.ImageEditor.Presentation.ViewModels;
using SkiaSharp;

namespace ShareX.ImageEditor.Presentation.Views;

public partial class EditorView
{
    private SKRect? _rasterSelection;
    private Point _rasterStart;
    private bool _rasterDragging;
    private bool _rasterClipboardBusy;
    private Guid? _floatingRasterPaste;
    private static bool _clipboardIsRaster;
    private DispatcherTimer? _regionScrollTimer;
    private IPointer? _regionPointer;
    private Point _regionViewportPoint;
    private KeyModifiers _regionModifiers;

    private bool IsRegionDragging => _rasterDragging || _inputController.IsRegionInteractionActive;

    private bool TryBeginRasterSelection(PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel { ActiveTool: EditorTool.RasterSelect } ||
            _editorCore.SourceImage == null) return false;
        var canvas = this.FindControl<Canvas>("AnnotationCanvas");
        if (canvas == null) return false;
        var properties = e.GetCurrentPoint(canvas).Properties;
        if (properties.IsRightButtonPressed)
        {
            ResetRasterSelection();
            e.Handled = true;
            return true;
        }
        if (!properties.IsLeftButtonPressed) return false;
        ResetRasterSelection();
        _selectionController.ClearSelection();
        var point = e.GetPosition(canvas);
        _rasterStart = ClampRasterPoint(point);
        _rasterDragging = true;
        Focus();
        e.Pointer.Capture(canvas);
        UpdateRasterSelection(point);
        TrackRegionPointer(e);
        e.Handled = true;
        return true;
    }

    private Point ClampRasterPoint(Point point) => new(
        Math.Clamp(point.X, 0, _editorCore.SourceImage?.Width ?? 0),
        Math.Clamp(point.Y, 0, _editorCore.SourceImage?.Height ?? 0));

    private void UpdateRasterSelection(Point point)
    {
        point = ClampRasterPoint(point);
        var region = new SKRect((float)_rasterStart.X, (float)_rasterStart.Y, (float)point.X, (float)point.Y);
        if (_editorCore.TryGetRasterBounds(region, out var bounds))
            _rasterSelection = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        else
            _rasterSelection = null;
        var marquee = this.FindControl<global::Avalonia.Controls.Shapes.Rectangle>("RasterSelectionOverlay");
        if (marquee != null)
        {
            marquee.IsVisible = _rasterSelection.HasValue;
            if (_rasterSelection is { } rect)
            {
                Canvas.SetLeft(marquee, rect.Left + OverlayCanvasBleed);
                Canvas.SetTop(marquee, rect.Top + OverlayCanvasBleed);
                marquee.Width = rect.Width;
                marquee.Height = rect.Height;
            }
        }
        if (DataContext is MainViewModel vm) vm.HasRasterSelection = _rasterSelection.HasValue;
    }

    private void ResetRasterSelection()
    {
        _rasterDragging = false;
        _rasterSelection = null;
        var pointer = _regionPointer;
        StopRegionAutoScroll();
        _inputController.CancelRegionInteraction();
        pointer?.Capture(null);
        if (this.FindControl<global::Avalonia.Controls.Shapes.Rectangle>("RasterSelectionOverlay") is { } marquee)
            marquee.IsVisible = false;
        if (DataContext is MainViewModel vm) vm.HasRasterSelection = false;
    }

    private async Task CopyRasterSelectionAsync(bool cut)
    {
        if (_rasterClipboardBusy || _rasterSelection is not { } region) return;
        _rasterClipboardBusy = true;
        ++_pendingAutoCopyImageVersion;
        try
        {
            var source = _editorCore.SourceImage;
            using var pixels = _editorCore.CopyRasterRegion(region);
            if (pixels == null) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) throw new InvalidOperationException("System clipboard is unavailable.");
            // Follow the existing Avalonia bitmap clipboard ownership convention.
            var bitmap = BitmapConversionHelpers.ToAvaloniBitmap(pixels);
            var data = new DataTransfer();
            var item = new DataTransferItem();
            item.SetBitmap(bitmap);
            data.Add(item);
            await clipboard.SetDataAsync(data);
            if (_workspaceDisposed) return;

            var annotation = new ImageAnnotation
            {
                StartPoint = new SKPoint(region.Left, region.Top),
                EndPoint = new SKPoint(region.Right, region.Bottom)
            };
            annotation.SetImage(pixels.Copy() ?? throw new InvalidOperationException("Could not copy selected pixels."));
            ReplaceClipboardAnnotation(annotation);
            _clipboardIsRaster = true;
            _ = CheckClipboardStatus();
            // Clipboard operations yield: never cut a newly loaded or subsequently edited image.
            if (cut && ReferenceEquals(source, _editorCore.SourceImage) && _rasterSelection == region)
                _editorCore.ClearRasterRegion(region);
        }
        catch (Exception ex)
        {
            EditorServices.ReportWarning(nameof(EditorView), "Failed to copy background pixels to the clipboard.", ex);
            if (DataContext is MainViewModel vm) vm.ShowNotification("Could not copy pixels. The image was left unchanged.");
        }
        finally { _rasterClipboardBusy = false; }
    }

    // Match the native bitmap to the internal raster clipboard without assuming channel layout.
    // External clipboard content retains priority when it differs from our last raster copy.
    private static bool IsRasterClipboardBitmap(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        if (!_clipboardIsRaster || _clipboardAnnotation is not ImageAnnotation { ImageBitmap: { } expected } ||
            bitmap.PixelSize.Width != expected.Width || bitmap.PixelSize.Height != expected.Height) return false;
        using var actual = BitmapConversionHelpers.ToSKBitmap(bitmap);
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                if (actual.GetPixel(x, y) != expected.GetPixel(x, y)) return false;
        return true;
    }

    private bool HandleRasterKeyDown(KeyEventArgs e)
    {
        _regionModifiers = e.KeyModifiers;
        if (e.KeyModifiers == KeyModifiers.Control && _rasterSelection.HasValue && (e.Key is Key.C or Key.X))
        {
            _ = CopyRasterSelectionAsync(e.Key == Key.X);
            return true;
        }
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None &&
            _selectionController.SelectedShape?.Tag is ImageAnnotation image && image.Id == _floatingRasterPaste)
        {
            _floatingRasterPaste = null;
            _selectionController.ClearSelection();
            return true;
        }
        if ((e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift) &&
            (_rasterSelection.HasValue || IsRegionDragging || _inputController.HasActiveRegion) &&
            (e.Key is Key.Left or Key.Right or Key.Up or Key.Down))
        {
            double step = e.KeyModifiers == KeyModifiers.Shift ? 128 : 32;
            ScrollRegionViewport(new Vector(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0,
                e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0));
            return true;
        }
        return false;
    }

    private bool CancelRasterInteraction()
    {
        if (_rasterDragging || _rasterSelection.HasValue)
        {
            ResetRasterSelection();
            return true;
        }
        if (_selectionController.SelectedShape?.Tag is ImageAnnotation image && image.Id == _floatingRasterPaste &&
            _editorCore.Annotations.Contains(image))
        {
            _floatingRasterPaste = null;
            if (DataContext is MainViewModel vm) vm.DeleteSelectedCommand.Execute(null);
            return true;
        }
        return false;
    }

    private void TrackRegionPointer(PointerEventArgs e)
    {
        if (!IsRegionDragging || this.FindControl<ScrollViewer>("CanvasScrollViewer") is not { } scroll) return;
        _regionPointer = e.Pointer;
        _regionViewportPoint = e.GetPosition(scroll);
        _regionModifiers = e.KeyModifiers;
        if (_regionScrollTimer == null)
        {
            _regionScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _regionScrollTimer.Tick += OnRegionScrollTick;
        }
        _regionScrollTimer.Start();
    }

    private void StopRegionAutoScroll()
    {
        _regionScrollTimer?.Stop();
        _regionPointer = null;
    }

    private void OnRegionCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_regionPointer != e.Pointer) return;
        ResetRasterSelection();
    }

    private void OnRegionScrollTick(object? sender, EventArgs e)
    {
        if (!IsRegionDragging || _regionPointer?.Captured == null || _workspaceDisposed)
        {
            StopRegionAutoScroll();
            return;
        }
        if (this.FindControl<ScrollViewer>("CanvasScrollViewer") is not { } scroll) return;
        ScrollRegionViewport(new Vector(EdgeScrollDelta(_regionViewportPoint.X, scroll.Viewport.Width),
            EdgeScrollDelta(_regionViewportPoint.Y, scroll.Viewport.Height)));
    }

    private static double EdgeScrollDelta(double position, double extent)
    {
        const double edge = 32;
        if (position < edge) return -18 * Math.Clamp((edge - position) / edge, 0, 1);
        if (position > extent - edge) return 18 * Math.Clamp((position - extent + edge) / edge, 0, 1);
        return 0;
    }

    private void ScrollRegionViewport(Vector delta)
    {
        if (delta == default || this.FindControl<ScrollViewer>("CanvasScrollViewer") is not { } scroll) return;
        var oldOffset = scroll.Offset;
        scroll.Offset = new Vector(Math.Clamp(oldOffset.X + delta.X, 0, Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width)),
            Math.Clamp(oldOffset.Y + delta.Y, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
        if (scroll.Offset == oldOffset) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsRegionDragging || _workspaceDisposed ||
                this.FindControl<Canvas>("AnnotationCanvas") is not { } canvas) return;
            // Translate after layout: includes zoom, scroll, centering and smart-padding offsets.
            if (scroll.TranslatePoint(_regionViewportPoint, canvas) is not { } imagePoint) return;
            if (_rasterDragging) UpdateRasterSelection(imagePoint);
            else _inputController.UpdateRegionDrag(imagePoint, _regionModifiers);
        }, DispatcherPriority.Loaded);
    }
}
