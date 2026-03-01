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
}
