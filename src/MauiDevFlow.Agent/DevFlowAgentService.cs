using Microsoft.Maui.Controls;
using MauiDevFlow.Agent.Core;
#if MACOS
using AppKit;
using Foundation;
#endif

namespace MauiDevFlow.Agent;

/// <summary>
/// Platform-specific agent service that provides native tap and screenshot
/// implementations for Android, iOS, Mac Catalyst, Windows, and macOS AppKit.
/// </summary>
public class PlatformAgentService : DevFlowAgentService
{
    public PlatformAgentService(AgentOptions? options = null) : base(options) { }

    protected override VisualTreeWalker CreateTreeWalker() => new PlatformVisualTreeWalker();

    protected override double GetWindowDisplayDensity(IWindow? window)
    {
        try
        {
#if IOS || MACCATALYST
            if (window?.Handler?.PlatformView is UIKit.UIWindow uiWindow)
                return uiWindow.Screen.Scale;
            return UIKit.UIScreen.MainScreen.Scale;
#elif ANDROID
            if (window?.Handler?.PlatformView is Android.App.Activity activity)
                return activity.Resources?.DisplayMetrics?.Density ?? 1.0;
            if (Android.App.Application.Context.Resources?.DisplayMetrics is Android.Util.DisplayMetrics dm)
                return dm.Density;
            return 1.0;
#elif WINDOWS
            if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window winuiWindow)
            {
                var xamlRoot = winuiWindow.Content?.XamlRoot;
                if (xamlRoot != null)
                    return xamlRoot.RasterizationScale;
            }
            return 1.0;
#elif MACOS
            if (window?.Handler?.PlatformView is AppKit.NSWindow nsWindow)
                return nsWindow.BackingScaleFactor;
            return AppKit.NSScreen.MainScreen?.BackingScaleFactor ?? 2.0;
#else
            return base.GetWindowDisplayDensity(window);
#endif
        }
        catch
        {
            return base.GetWindowDisplayDensity(window);
        }
    }

    protected override Task<bool> TryNativeScroll(VisualElement element, double deltaX, double deltaY)
    {
        try
        {
            // Walk up from the element to find a native scrollable view
            var target = element;
            while (target != null)
            {
                var platformView = target.Handler?.PlatformView;
                if (platformView != null)
                {
#if IOS || MACCATALYST
                    // Check: view itself → subviews → ancestors
                    var uiView = platformView as UIKit.UIView;
                    UIKit.UIScrollView? uiScrollView = uiView as UIKit.UIScrollView;
                    if (uiScrollView == null)
                        uiScrollView = FindNativeDescendant<UIKit.UIScrollView>(uiView);
                    if (uiScrollView == null)
                        uiScrollView = FindNativeAncestor<UIKit.UIScrollView>(uiView);
                    if (uiScrollView != null)
                    {
                        var offset = uiScrollView.ContentOffset;
                        var newX = Math.Max(0, Math.Min(offset.X + deltaX, uiScrollView.ContentSize.Width - uiScrollView.Bounds.Width));
                        var newY = Math.Max(0, Math.Min(offset.Y - deltaY, uiScrollView.ContentSize.Height - uiScrollView.Bounds.Height));
                        uiScrollView.SetContentOffset(new CoreGraphics.CGPoint(newX, newY), animated: true);
                        return Task.FromResult(true);
                    }
#elif ANDROID
                    // Check: view itself → descendants → ancestors
                    var androidView = platformView as Android.Views.View;
                    var recyclerView = androidView as AndroidX.RecyclerView.Widget.RecyclerView;
                    if (recyclerView == null)
                        recyclerView = FindNativeDescendantAndroid<AndroidX.RecyclerView.Widget.RecyclerView>(androidView);
                    if (recyclerView == null)
                        recyclerView = FindNativeAncestorAndroid<AndroidX.RecyclerView.Widget.RecyclerView>(androidView);
                    if (recyclerView != null)
                    {
                        recyclerView.ScrollBy((int)deltaX, (int)-deltaY);
                        return Task.FromResult(true);
                    }
                    var androidScrollView = androidView as Android.Widget.ScrollView;
                    if (androidScrollView == null)
                        androidScrollView = FindNativeDescendantAndroid<Android.Widget.ScrollView>(androidView);
                    if (androidScrollView == null)
                        androidScrollView = FindNativeAncestorAndroid<Android.Widget.ScrollView>(androidView);
                    if (androidScrollView != null)
                    {
                        androidScrollView.ScrollBy((int)deltaX, (int)-deltaY);
                        return Task.FromResult(true);
                    }
#elif WINDOWS
                    // Check: view itself → descendants → ancestors
                    var winView = platformView as Microsoft.UI.Xaml.DependencyObject;
                    var scrollViewer = winView as Microsoft.UI.Xaml.Controls.ScrollViewer;
                    if (scrollViewer == null)
                        scrollViewer = FindWinUIDescendant<Microsoft.UI.Xaml.Controls.ScrollViewer>(winView);
                    if (scrollViewer == null)
                        scrollViewer = FindWinUIScrollViewer(winView);
                    if (scrollViewer != null)
                    {
                        scrollViewer.ChangeView(
                            scrollViewer.HorizontalOffset + deltaX,
                            scrollViewer.VerticalOffset - deltaY,
                            null);
                        return Task.FromResult(true);
                    }
#endif
                }
                target = target.Parent as VisualElement;
            }
        }
        catch { }
        return Task.FromResult(false);
    }

#if IOS || MACCATALYST
    private static T? FindNativeAncestor<T>(UIKit.UIView? view) where T : UIKit.UIView
    {
        var current = view;
        while (current != null)
        {
            if (current is T match) return match;
            current = current.Superview;
        }
        return null;
    }

    private static T? FindNativeDescendant<T>(UIKit.UIView? view) where T : UIKit.UIView
    {
        if (view == null) return null;
        if (view is T match) return match;
        foreach (var subview in view.Subviews)
        {
            var found = FindNativeDescendant<T>(subview);
            if (found != null) return found;
        }
        return null;
    }
#elif ANDROID
    private static T? FindNativeAncestorAndroid<T>(Android.Views.View? view) where T : Android.Views.View
    {
        var current = view;
        while (current != null)
        {
            if (current is T match) return match;
            current = current.Parent as Android.Views.View;
        }
        return null;
    }

    private static T? FindNativeDescendantAndroid<T>(Android.Views.View? view) where T : Android.Views.View
    {
        if (view == null) return null;
        if (view is T match) return match;
        if (view is Android.Views.ViewGroup vg)
        {
            for (var i = 0; i < vg.ChildCount; i++)
            {
                var found = FindNativeDescendantAndroid<T>(vg.GetChildAt(i));
                if (found != null) return found;
            }
        }
        return null;
    }
#elif WINDOWS
    private static Microsoft.UI.Xaml.Controls.ScrollViewer? FindWinUIScrollViewer(Microsoft.UI.Xaml.DependencyObject? obj)
    {
        if (obj == null) return null;
        if (obj is Microsoft.UI.Xaml.Controls.ScrollViewer sv) return sv;
        // Walk up the visual tree
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(obj);
        while (parent != null)
        {
            if (parent is Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer)
                return scrollViewer;
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        }
        // Also search children (CollectionView wraps a ScrollViewer internally)
        return FindWinUIDescendant<Microsoft.UI.Xaml.Controls.ScrollViewer>(obj);
    }

    private static T? FindWinUIDescendant<T>(Microsoft.UI.Xaml.DependencyObject? parent) where T : Microsoft.UI.Xaml.DependencyObject
    {
        if (parent == null) return null;
        if (parent is T match) return match;
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var descendant = FindWinUIDescendant<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }
#endif

    protected override bool TryNativeTap(VisualElement ve)
    {
        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView == null) return false;

#if IOS || MACCATALYST
            if (platformView is UIKit.UIControl control)
            {
                control.SendActionForControlEvents(UIKit.UIControlEvent.TouchUpInside);
                return true;
            }
#elif ANDROID
            if (platformView is Android.Views.View androidView && androidView.Clickable)
            {
                androidView.PerformClick();
                return true;
            }
#elif MACOS
            if (platformView is NSButton button)
            {
                button.PerformClick(null);
                return true;
            }
            if (platformView is NSControl nsControl && nsControl.Action != null)
            {
                nsControl.SendAction(nsControl.Action, nsControl.Target);
                return true;
            }
#endif
        }
        catch { }
        return false;
    }

#if MACOS
    protected override async Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement)
    {
        try
        {
            // Get the window - try KeyWindow first, then find any visible window via MAUI
            var window = NSApplication.SharedApplication.KeyWindow;
            if (window == null)
            {
                var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
                if (mauiWindow?.Handler?.PlatformView is NSWindow nsWindow)
                    window = nsWindow;
            }

            // If a modal sheet is attached, capture it instead of the main window
            if (window?.AttachedSheet is NSWindow sheet)
                window = sheet;

            // Use CGWindowListCreateImage for composited capture including layer-backed controls
            if (window != null)
            {
                var pngBytes = CaptureWindowViaCG(window);
                if (pngBytes != null)
                    return pngBytes;
            }

            // Fallback: DataWithPdfInsideRect (misses layer-backed controls like NSButton, NSSlider)
            var contentView = window?.ContentView;
            if (contentView != null)
            {
                var bounds = contentView.Bounds;
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    var pdfData = contentView.DataWithPdfInsideRect(bounds);
                    if (pdfData != null)
                    {
                        var image = new NSImage(pdfData);
                        var tiffData = image.AsTiff();
                        if (tiffData != null)
                        {
                            var bitmapRep = new NSBitmapImageRep(tiffData);
                            var pngData = bitmapRep.RepresentationUsingTypeProperties(
                                NSBitmapImageFileType.Png, new NSDictionary());
                            return pngData?.ToArray();
                        }
                    }
                }
            }
        }
        catch { }

        return await base.CaptureScreenshotAsync(rootElement);
    }

    protected override Task<byte[]?> CaptureElementScreenshotAsync(VisualElement element)
    {
        try
        {
            if (element.Handler?.PlatformView is NSView nsView)
            {
                var pngBytes = CaptureNSView(nsView);
                if (pngBytes != null)
                    return Task.FromResult<byte[]?>(pngBytes);
            }
        }
        catch { }

        return base.CaptureElementScreenshotAsync(element);
    }

    private static byte[]? CaptureNSView(NSView view)
    {
        var bounds = view.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        var scale = view.Window?.BackingScaleFactor ?? 2.0;
        var pixelWidth = (int)(bounds.Width * scale);
        var pixelHeight = (int)(bounds.Height * scale);

        var rep = new NSBitmapImageRep(
            IntPtr.Zero,
            pixelWidth,
            pixelHeight,
            8,       // bits per sample
            4,       // samples per pixel (RGBA)
            true,    // has alpha
            false,   // is planar
            NSColorSpace.DeviceRGB,
            0,       // bytes per row (auto)
            0);      // bits per pixel (auto)

        if (rep == null)
            return null;

        rep.Size = new CoreGraphics.CGSize(bounds.Width, bounds.Height);

        NSGraphicsContext.GlobalSaveGraphicsState();
        try
        {
            var context = NSGraphicsContext.FromBitmap(rep);
            if (context == null)
                return null;

            NSGraphicsContext.CurrentContext = context;
            view.CacheDisplay(bounds, rep);
        }
        finally
        {
            NSGraphicsContext.GlobalRestoreGraphicsState();
        }

        var pngData = rep.RepresentationUsingTypeProperties(
            NSBitmapImageFileType.Png, new NSDictionary());
        return pngData?.ToArray();
    }

    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    static extern IntPtr CGWindowListCreateImage(
        CoreGraphics.CGRect screenBounds,
        uint listOption,
        uint windowID,
        uint imageOption);

    private static byte[]? CaptureWindowViaCG(NSWindow window)
    {
        try
        {
            // kCGWindowListOptionIncludingWindow = 0x08, kCGWindowImageBoundsIgnoreFraming = 0x01
            var cgImagePtr = CGWindowListCreateImage(
                CoreGraphics.CGRect.Null, 0x08, (uint)window.WindowNumber, 0x01);

            if (cgImagePtr == IntPtr.Zero)
                return null;

            var cgImage = ObjCRuntime.Runtime.GetINativeObject<CoreGraphics.CGImage>(
                cgImagePtr, owns: true);
            if (cgImage == null)
                return null;

            var bitmapRep = new NSBitmapImageRep(cgImage);
            var pngData = bitmapRep.RepresentationUsingTypeProperties(
                NSBitmapImageFileType.Png, new NSDictionary());
            return pngData?.ToArray();
        }
        catch
        {
            return null;
        }
    }
#elif WINDOWS
    protected override async Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement)
    {
        // MAUI's VisualDiagnostics doesn't capture WebView2 GPU-rendered content on Windows.
        // When a WebView2 is present, use CoreWebView2.CapturePreviewAsync instead.
        try
        {
            var wv2 = FindPlatformWebView2(rootElement);
            if (wv2?.CoreWebView2 != null)
            {
                using var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await wv2.CoreWebView2.CapturePreviewAsync(
                    Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, ras);
                var reader = new Windows.Storage.Streams.DataReader(ras.GetInputStreamAt(0));
                await reader.LoadAsync((uint)ras.Size);
                var bytes = new byte[ras.Size];
                reader.ReadBytes(bytes);
                return bytes;
            }
        }
        catch { }

        return await base.CaptureScreenshotAsync(rootElement);
    }

    private static Microsoft.UI.Xaml.Controls.WebView2? FindPlatformWebView2(Element element)
    {
        if (element is View view && view.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 wv2)
            return wv2;
        // Shell doesn't expose pages via Content/Children — use CurrentPage
        if (element is Shell shell && shell.CurrentPage != null)
        {
            var found = FindPlatformWebView2(shell.CurrentPage);
            if (found != null) return found;
        }
        if (element is ContentPage page && page.Content != null)
        {
            var found = FindPlatformWebView2(page.Content);
            if (found != null) return found;
        }
        if (element is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is Element childElement)
                {
                    var found = FindPlatformWebView2(childElement);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }
#endif

    // -----------------------------------------------------------------------
    // Native Highlight Overlay
    // Drawn directly on the native window layer — pixel-perfect because it uses
    // the same coordinate origin as ResolveWindowBounds.
    // -----------------------------------------------------------------------

#if IOS || MACCATALYST
    private UIKit.UIView? _highlightView;

    protected override void ShowNativeHighlight(BoundsInfo bounds, string color)
    {
        ClearNativeHighlight();
        var window = UIKit.UIApplication.SharedApplication.KeyWindow
            ?? Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as UIKit.UIWindow;
        var root = window?.RootViewController?.View;
        if (root == null) return;

        var parsed = ParseHexColor(color);
        var highlight = new UIKit.UIView(new CoreGraphics.CGRect(bounds.X, bounds.Y, bounds.Width, bounds.Height))
        {
            BackgroundColor = UIKit.UIColor.Clear,
            UserInteractionEnabled = false,
            AccessibilityElementsHidden = true,
        };
        highlight.Layer.BorderColor = new CoreGraphics.CGColor(
            (nfloat)((parsed.r) / 255.0),
            (nfloat)((parsed.g) / 255.0),
            (nfloat)((parsed.b) / 255.0),
            (nfloat)((parsed.a) / 255.0));
        highlight.Layer.BorderWidth = 3f;
        // Subtle fill
        highlight.BackgroundColor = UIKit.UIColor.FromRGBA(
            (byte)parsed.r, (byte)parsed.g, (byte)parsed.b, (byte)Math.Min(40, parsed.a));

        root.AddSubview(highlight);
        _highlightView = highlight;
    }

    protected override void ClearNativeHighlight()
    {
        _highlightView?.RemoveFromSuperview();
        _highlightView = null;
    }
#elif ANDROID
    private Android.Views.View? _highlightView;

    protected override void ShowNativeHighlight(BoundsInfo bounds, string color)
    {
        ClearNativeHighlight();
        var activity = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Android.App.Activity;
        if (activity?.Window?.DecorView is not Android.Views.ViewGroup decorView) return;

        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        var parsed = ParseHexColor(color);
        var borderColor = Android.Graphics.Color.Argb(parsed.a, parsed.r, parsed.g, parsed.b);
        var fillColor  = Android.Graphics.Color.Argb(Math.Min(40, parsed.a), parsed.r, parsed.g, parsed.b);

        var drawable = new Android.Graphics.Drawables.GradientDrawable();
        drawable.SetShape(Android.Graphics.Drawables.ShapeType.Rectangle);
        drawable.SetColor(fillColor);
        drawable.SetStroke((int)(3 * density), borderColor);

        var view = new Android.Views.View(activity) { Background = drawable };
        view.Focusable = false;
        view.ImportantForAccessibility = Android.Views.ImportantForAccessibility.No;

        var lp = new Android.Widget.FrameLayout.LayoutParams(
            (int)(bounds.Width * density),
            (int)(bounds.Height * density))
        {
            LeftMargin = (int)(bounds.X * density),
            TopMargin  = (int)(bounds.Y * density),
        };
        decorView.AddView(view, lp);
        _highlightView = view;
    }

    protected override void ClearNativeHighlight()
    {
        if (_highlightView?.Parent is Android.Views.ViewGroup parent)
            parent.RemoveView(_highlightView);
        _highlightView = null;
    }
#elif WINDOWS
    private Microsoft.UI.Xaml.Shapes.Rectangle? _highlightView;

    protected override void ShowNativeHighlight(BoundsInfo bounds, string color)
    {
        ClearNativeHighlight();
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window?.Content is not Microsoft.UI.Xaml.FrameworkElement root) return;

        // Find or create a Canvas overlay at root level
        var canvas = FindOrCreateHighlightCanvas(root);
        if (canvas == null) return;

        var parsed = ParseHexColor(color);
        var rect = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width  = bounds.Width,
            Height = bounds.Height,
            Fill   = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb((byte)Math.Min(40, parsed.a), (byte)parsed.r, (byte)parsed.g, (byte)parsed.b)),
            Stroke = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb((byte)parsed.a, (byte)parsed.r, (byte)parsed.g, (byte)parsed.b)),
            StrokeThickness = 3,
            IsHitTestVisible = false,
        };
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(rect, bounds.X);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(rect, bounds.Y);
        canvas.Children.Add(rect);
        _highlightView = rect;
    }

    protected override void ClearNativeHighlight()
    {
        if (_highlightView?.Parent is Microsoft.UI.Xaml.Controls.Canvas canvas)
            canvas.Children.Remove(_highlightView);
        _highlightView = null;
    }

    private static Microsoft.UI.Xaml.Controls.Canvas? FindOrCreateHighlightCanvas(Microsoft.UI.Xaml.FrameworkElement root)
    {
        // Look for existing highlight canvas in the visual tree root
        if (root is Microsoft.UI.Xaml.Controls.Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Microsoft.UI.Xaml.Controls.Canvas c && c.Name == "__devflow_highlight")
                    return c;
            }
            var canvas = new Microsoft.UI.Xaml.Controls.Canvas
            {
                Name = "__devflow_highlight",
                IsHitTestVisible = false,
                Width  = root.ActualWidth,
                Height = root.ActualHeight,
            };
            panel.Children.Add(canvas);
            return canvas;
        }
        return null;
    }
#elif MACOS
    private AppKit.NSView? _highlightView;

    protected override void ShowNativeHighlight(BoundsInfo bounds, string color)
    {
        ClearNativeHighlight();
        var nsWindow = AppKit.NSApplication.SharedApplication.KeyWindow
            ?? (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as AppKit.NSWindow);
        if (nsWindow?.ContentView == null) return;

        var parsed = ParseHexColor(color);
        var highlight = new AppKit.NSView(new CoreGraphics.CGRect(
            bounds.X,
            nsWindow.ContentView.Bounds.Height - bounds.Y - bounds.Height,  // flip Y
            bounds.Width,
            bounds.Height))
        {
            WantsLayer = true,
        };
        highlight.Layer!.BorderColor = new CoreGraphics.CGColor(
            (nfloat)(parsed.r / 255.0), (nfloat)(parsed.g / 255.0),
            (nfloat)(parsed.b / 255.0), (nfloat)(parsed.a / 255.0));
        highlight.Layer.BorderWidth = 3f;
        highlight.Layer.BackgroundColor = new CoreGraphics.CGColor(
            (nfloat)(parsed.r / 255.0), (nfloat)(parsed.g / 255.0),
            (nfloat)(parsed.b / 255.0), (nfloat)(Math.Min(40, parsed.a) / 255.0));

        nsWindow.ContentView.AddSubview(highlight);
        _highlightView = highlight;
    }

    protected override void ClearNativeHighlight()
    {
        _highlightView?.RemoveFromSuperview();
        _highlightView = null;
    }
#endif

    /// <summary>Parses #AARRGGBB or #RRGGBB hex color string into components.</summary>
    private static (int a, int r, int g, int b) ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            hex = "FF" + hex;
        if (hex.Length != 8)
            return (255, 0x10, 0x7C, 0x10); // fallback green
        return (
            Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16),
            Convert.ToInt32(hex[6..8], 16));
    }
}
