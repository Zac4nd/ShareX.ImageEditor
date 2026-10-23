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

using SkiaSharp;

namespace ShareX.ImageEditor.Core.Editor;

public partial class EditorCore
{
    /// <summary>Normalize image-space edges to whole pixels and clip to the source.</summary>
    public bool TryGetRasterBounds(SKRect region, out SKRectI bounds)
    {
        bounds = default;
        if (SourceImage == null || !float.IsFinite(region.Left) || !float.IsFinite(region.Top) ||
            !float.IsFinite(region.Right) || !float.IsFinite(region.Bottom)) return false;

        float left = Math.Clamp(Math.Min(region.Left, region.Right), 0, SourceImage.Width);
        float top = Math.Clamp(Math.Min(region.Top, region.Bottom), 0, SourceImage.Height);
        float right = Math.Clamp(Math.Max(region.Left, region.Right), 0, SourceImage.Width);
        float bottom = Math.Clamp(Math.Max(region.Top, region.Bottom), 0, SourceImage.Height);
        if (right <= left || bottom <= top) return false;

        bounds = new SKRectI((int)Math.Floor(left), (int)Math.Floor(top),
            (int)Math.Ceiling(right), (int)Math.Ceiling(bottom));
        return true;
    }

    /// <summary>Copies background pixels only. The caller owns the independent bitmap.</summary>
    public SKBitmap? CopyRasterRegion(SKRect region)
    {
        if (!TryGetRasterBounds(region, out var bounds)) return null;
        var copy = new SKBitmap(bounds.Width, bounds.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            using var canvas = new SKCanvas(copy);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(SourceImage!, -bounds.Left, -bounds.Top);
            return copy;
        }
        catch { copy.Dispose(); throw; }
    }

    /// <summary>Clears background pixels to transparency, preserving annotations and history.</summary>
    public bool ClearRasterRegion(SKRect region)
    {
        if (!TryGetRasterBounds(region, out var bounds)) return false;
        bool changed = ApplyImageOperation(source =>
        {
            // Always use an alpha-capable destination, including for opaque/JPEG input.
            var result = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            try
            {
                using var canvas = new SKCanvas(result);
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(source, 0, 0);
                using var clear = new SKPaint { BlendMode = SKBlendMode.Clear };
                canvas.DrawRect(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), clear);
                return result;
            }
            catch { result.Dispose(); throw; }
        });
        // Rebuild bitmap-backed effect visuals against the newly cleared source.
        if (changed) AnnotationsRestored?.Invoke();
        return changed;
    }
}
