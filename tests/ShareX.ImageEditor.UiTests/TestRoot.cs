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
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Styling;

// Avalonia 12.1 does not expose a standalone layout root publicly. Keep the
// presentation-source adapter isolated here; it supplies layout services only.
// Font loading, theme lookup, controls, bindings and Skia rendering are real.
class TestCursorFactory : ICursorFactory
{
    public ICursorImpl GetCursor(StandardCursorType type) => new TestCursor();
    public ICursorImpl CreateCursor(Bitmap bitmap, PixelPoint hotSpot) => new TestCursor();
}
class TestCursor : ICursorImpl { public void Dispose() {} }


class TestRoot : Decorator, ILogicalRoot, IStyleHost
{
    // Match TopLevel: application styles/resources are above the root in lookup.
    IStyleHost IStyleHost.StylingParent => Application.Current!;
    public Size ClientSize { get; set; } = new Size(1000, 700);
    public TestRoot()
    {
        var types = typeof(Visual).Assembly.GetTypes();
        var layoutType = types.Single(t => t.FullName == "Avalonia.Layout.ILayoutRoot");
        var rendererType = types.Single(t => t.FullName == "Avalonia.Rendering.IRenderer");
        object? manager = null;
        var layout = Proxy.Make(layoutType, m => m.Name switch { "get_LayoutManager" => manager, "get_RootVisual" => this, "get_LayoutScaling" => 1d, "get_ClientSize" => ClientSize, _ => null });
        manager = Activator.CreateInstance(types.Single(t => t.FullName == "Avalonia.Layout.LayoutManager"), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new object[] {layout}, null);
        var source = (IPresentationSource)Proxy.Make(typeof(IPresentationSource), m => m.Name switch {
            "get_LayoutRoot" => layout,
            "get_RootVisual" => this,
            "get_RenderScaling" => 1d,
            "get_ClientSize" => ClientSize,
            "get_Renderer" => Proxy.Make(rendererType, _ => null),
            _ => null
        });
        typeof(Visual).GetMethod("SetPresentationSourceForRootVisual", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(this, new object?[] {source});
    }
}
public class Proxy : System.Reflection.DispatchProxy
{
    public Func<System.Reflection.MethodInfo, object?> Handler = _ => null;
    public static object Make(Type type, Func<System.Reflection.MethodInfo, object?> handler)
    {
        var proxy = (Proxy)Create(type, typeof(Proxy)); proxy.Handler = handler; return proxy;
    }
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        var value = Handler(method!);
        return value ?? (method!.ReturnType != typeof(void) && method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null);
    }
}
