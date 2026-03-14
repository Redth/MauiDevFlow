using Microsoft.Maui.Controls;
using MauiDevFlow.Agent.Core;
#if IOS || MACCATALYST
using UIKit;
#endif
#if MACOS
using AppKit;
#endif

namespace MauiDevFlow.Agent;

/// <summary>
/// Platform-specific visual tree walker that provides native view info
/// for Android, iOS, Mac Catalyst, Windows, and macOS AppKit.
/// </summary>
public class PlatformVisualTreeWalker : VisualTreeWalker
{
    protected override void PopulateNativeInfo(ElementInfo info, VisualElement ve)
    {
        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView == null) return;

            info.NativeType = platformView.GetType().FullName;

#if IOS || MACCATALYST
            if (platformView is UIKit.UIView uiView)
            {
                var props = new Dictionary<string, string?>();
                if (!string.IsNullOrEmpty(uiView.AccessibilityIdentifier))
                    props["accessibilityIdentifier"] = uiView.AccessibilityIdentifier;
                if (!string.IsNullOrEmpty(uiView.AccessibilityLabel))
                    props["accessibilityLabel"] = uiView.AccessibilityLabel;
                if (uiView is UIKit.UIControl uiControl)
                    props["isUIControl"] = "true";
                if (uiView is UIKit.UITextField textField)
                    props["isSecureTextEntry"] = textField.SecureTextEntry.ToString();
                if (props.Count > 0)
                    info.NativeProperties = props;

                // Effective rendered colors — resolved after theme/styles.
                // For background: walk up the superview chain because most text views
                // have UIColor.Clear as their own background; the real background is on
                // a parent container (UIView, UIScrollView, window background).
                if (uiView is UIKit.UILabel uiLabel)
                {
                    info.EffectiveTextColor = IosColorToHex(uiLabel.TextColor);
                    info.EffectiveBackgroundColor = IosColorToHex(uiView.BackgroundColor)
                        ?? IosEffectiveBackgroundColor(uiView.Superview);
                }
                else if (uiView is UIKit.UITextField uiTextField2)
                {
                    info.EffectiveTextColor = IosColorToHex(uiTextField2.TextColor);
                    info.EffectiveBackgroundColor = IosColorToHex(uiView.BackgroundColor)
                        ?? IosEffectiveBackgroundColor(uiView.Superview);
                }
                else if (uiView is UIKit.UITextView uiTextView)
                {
                    info.EffectiveTextColor = IosColorToHex(uiTextView.TextColor);
                    info.EffectiveBackgroundColor = IosColorToHex(uiView.BackgroundColor)
                        ?? IosEffectiveBackgroundColor(uiView.Superview);
                }
                else if (uiView is UIKit.UIButton uiButton)
                {
                    info.EffectiveTextColor = IosColorToHex(uiButton.TitleLabel?.TextColor);
                    info.EffectiveBackgroundColor = IosColorToHex(uiView.BackgroundColor)
                        ?? IosEffectiveBackgroundColor(uiView.Superview);
                }
            }
#elif ANDROID
            if (platformView is Android.Views.View androidView)
            {
                var props = new Dictionary<string, string?>();
                if (!string.IsNullOrEmpty(androidView.ContentDescription))
                    props["contentDescription"] = androidView.ContentDescription;
                if (androidView is Android.Widget.EditText editText)
                    props["inputType"] = editText.InputType.ToString();
                if (androidView.Clickable)
                    props["clickable"] = "true";
                if (props.Count > 0)
                    info.NativeProperties = props;

                // Effective rendered colors — after theme/style resolution
                if (androidView is Android.Widget.TextView textView)
                {
                    var argb = textView.CurrentTextColor;
                    info.EffectiveTextColor = $"#{(argb >> 24) & 0xFF:X2}{(argb >> 16) & 0xFF:X2}{(argb >> 8) & 0xFF:X2}{argb & 0xFF:X2}";
                    // Walk up parent chain for background — text views often have transparent bg
                    info.EffectiveBackgroundColor = AndroidEffectiveBackgroundColor(androidView);
                }
            }
#elif MACOS
            if (platformView is NSView nsView)
            {
                var props = new Dictionary<string, string?>();
                if (!string.IsNullOrEmpty(nsView.AccessibilityIdentifier))
                    props["accessibilityIdentifier"] = nsView.AccessibilityIdentifier;
                if (!string.IsNullOrEmpty(nsView.AccessibilityLabel))
                    props["accessibilityLabel"] = nsView.AccessibilityLabel;
                if (nsView is NSControl nsControl)
                {
                    props["isNSControl"] = "true";
                    props["isEnabled"] = nsControl.Enabled.ToString();
                }
                if (nsView is NSButton nsButton)
                    props["buttonTitle"] = nsButton.Title;
                if (nsView is NSTextField nsTextField)
                {
                    props["stringValue"] = nsTextField.StringValue;
                    props["isEditable"] = nsTextField.Editable.ToString();
                    info.EffectiveTextColor = MacColorToHex(nsTextField.TextColor);
                    info.EffectiveBackgroundColor = MacColorToHex(nsTextField.BackgroundColor);
                }
                props["isHidden"] = nsView.Hidden.ToString();
                props["alphaValue"] = nsView.AlphaValue.ToString("F2");
                if (props.Count > 0)
                    info.NativeProperties = props;
            }
#elif WINDOWS
            if (platformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
            {
                var props = new Dictionary<string, string?>();
                var automationId = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(frameworkElement);
                if (!string.IsNullOrEmpty(automationId))
                    props["automationId"] = automationId;
                var automationName = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(frameworkElement);
                if (!string.IsNullOrEmpty(automationName))
                    props["automationName"] = automationName;
                var helpText = Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(frameworkElement);
                if (!string.IsNullOrEmpty(helpText))
                    props["helpText"] = helpText;
                if (!string.IsNullOrEmpty(frameworkElement.Name))
                    props["name"] = frameworkElement.Name;
                if (frameworkElement.Visibility != Microsoft.UI.Xaml.Visibility.Visible)
                    props["visibility"] = "collapsed";
                if (!frameworkElement.IsHitTestVisible)
                    props["isHitTestVisible"] = "false";
                if (frameworkElement is Microsoft.UI.Xaml.Controls.Control control)
                {
                    if (!control.IsEnabled)
                        props["isEnabled"] = "false";
                    if (!control.IsTabStop)
                        props["isTabStop"] = "false";
                }
                if (frameworkElement is Microsoft.UI.Xaml.Controls.TextBox textBox)
                {
                    if (textBox.IsReadOnly)
                        props["isReadOnly"] = "true";
                }
                if (frameworkElement is Microsoft.UI.Xaml.Controls.PasswordBox)
                    props["isPassword"] = "true";
                if (props.Count > 0)
                    info.NativeProperties = props;

                // Effective rendered colors
                Windows.UI.Color? fg = null;
                Windows.UI.Color? bg = null;
                if (frameworkElement is Microsoft.UI.Xaml.Controls.TextBlock tb
                    && tb.Foreground is Microsoft.UI.Xaml.Media.SolidColorBrush tbBrush)
                    fg = tbBrush.Color;
                else if (frameworkElement is Microsoft.UI.Xaml.Controls.Control ctrl
                    && ctrl.Foreground is Microsoft.UI.Xaml.Media.SolidColorBrush ctrlBrush)
                    fg = ctrlBrush.Color;
                if (frameworkElement.Background is Microsoft.UI.Xaml.Media.SolidColorBrush bgBrush)
                    bg = bgBrush.Color;
                if (fg.HasValue)
                    info.EffectiveTextColor = $"#{fg.Value.A:X2}{fg.Value.R:X2}{fg.Value.G:X2}{fg.Value.B:X2}";
                if (bg.HasValue)
                    info.EffectiveBackgroundColor = $"#{bg.Value.A:X2}{bg.Value.R:X2}{bg.Value.G:X2}{bg.Value.B:X2}";
            }
#endif
        }
        catch
        {
            // Native info is best-effort; don't fail the tree walk
        }
    }

    protected override void PopulateAccessibilityInfo(ElementInfo info, VisualElement ve)
    {
        // Let base populate MAUI-level semantics first
        base.PopulateAccessibilityInfo(info, ve);

        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView == null) return;

            info.Accessibility ??= new AccessibilityInfo();
            var a11y = info.Accessibility;

#if IOS || MACCATALYST
            if (platformView is UIKit.UIView uiView)
            {
                // Native accessibility label (resolved by UIKit, includes any overrides)
                var nativeLabel = uiView.AccessibilityLabel;
                if (!string.IsNullOrEmpty(nativeLabel))
                    a11y.Label = nativeLabel;

                var nativeHint = uiView.AccessibilityHint;
                if (!string.IsNullOrEmpty(nativeHint))
                    a11y.Hint = nativeHint;

                var nativeValue = uiView.AccessibilityValue;
                if (!string.IsNullOrEmpty(nativeValue))
                    a11y.Value = nativeValue;

                // UIAccessibilityTraits → readable trait list
                var traits = uiView.AccessibilityTraits;
                var traitNames = new List<string>();
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.Button)) traitNames.Add("Button");
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.Link)) traitNames.Add("Link");
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.Header)) { traitNames.Add("Header"); a11y.IsHeading = true; }
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.SearchField)) traitNames.Add("SearchField");
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.Image)) traitNames.Add("Image");
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.Selected)) traitNames.Add("Selected");
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.PlaysSound)) traitNames.Add("PlaysSound");
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.KeyboardKey)) traitNames.Add("KeyboardKey");
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.StaticText)) traitNames.Add("StaticText");
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.SummaryElement)) traitNames.Add("SummaryElement");
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.NotEnabled)) { traitNames.Add("NotEnabled"); a11y.IsEnabled = false; }
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.UpdatesFrequently)) traitNames.Add("UpdatesFrequently");
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.Adjustable)) traitNames.Add("Adjustable");
                if (traits.HasFlag(UIKit.UIAccessibilityTrait.AllowsDirectInteraction)) traitNames.Add("AllowsDirectInteraction");
                if (traitNames.Count > 0)
                    a11y.Traits = traitNames;

                // Map traits to role if not already set
                if (a11y.Role == null)
                {
                    if (traits.HasFlag(UIKit.UIAccessibilityTrait.Button)) a11y.Role = "Button";
                    else if (traits.HasFlag(UIKit.UIAccessibilityTrait.Link)) a11y.Role = "Link";
                    else if (traits.HasFlag(UIKit.UIAccessibilityTrait.SearchField)) a11y.Role = "SearchField";
                    else if (traits.HasFlag(UIKit.UIAccessibilityTrait.Image)) a11y.Role = "Image";
                    else if (traits.HasFlag(UIKit.UIAccessibilityTrait.Header)) a11y.Role = "Header";
                    else if (traits.HasFlag(UIKit.UIAccessibilityTrait.StaticText)) a11y.Role = "StaticText";
                    else if (traits.HasFlag(UIKit.UIAccessibilityTrait.Adjustable)) a11y.Role = "Adjustable";
                }

                // Native IsAccessibilityElement can upgrade but not downgrade
                // (UIKit returns false for views grouped into containers, but they're still relevant)
                if (uiView.IsAccessibilityElement)
                    a11y.IsAccessibilityElement = true;
                if (uiView.IsAccessibilityElement)
                    a11y.IsFocusable = true;

                // Accessibility container child count
                if (uiView is UIKit.IUIAccessibilityContainer container)
                {
                    var count = container.AccessibilityElementCount();
                    if (count > 0)
                        a11y.ChildCount = (int)count;
                }
            }
#elif ANDROID
            if (platformView is Android.Views.View androidView)
            {
                var nodeInfo = androidView.CreateAccessibilityNodeInfo();
                if (nodeInfo != null)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(nodeInfo.ContentDescription?.ToString()))
                            a11y.Label = nodeInfo.ContentDescription!.ToString();

                        if (!string.IsNullOrEmpty(nodeInfo.Text?.ToString()))
                        {
                            // If no label, text serves as announced text
                            if (string.IsNullOrEmpty(a11y.Label))
                                a11y.Label = nodeInfo.Text!.ToString();
                            a11y.Value = nodeInfo.Text!.ToString();
                        }

                        if (!string.IsNullOrEmpty(nodeInfo.HintText?.ToString()))
                            a11y.Hint = nodeInfo.HintText!.ToString();

                        // Map Android className to role
                        var className = nodeInfo.ClassName?.ToString() ?? "";
                        a11y.Role ??= className switch
                        {
                            var c when c.Contains("Button") => "Button",
                            var c when c.Contains("EditText") => "TextField",
                            var c when c.Contains("TextView") => "StaticText",
                            var c when c.Contains("ImageView") => "Image",
                            var c when c.Contains("CheckBox") => "CheckBox",
                            var c when c.Contains("RadioButton") => "RadioButton",
                            var c when c.Contains("Switch") || c.Contains("ToggleButton") => "Switch",
                            var c when c.Contains("SeekBar") || c.Contains("ProgressBar") => "Slider",
                            var c when c.Contains("Spinner") => "ComboBox",
                            var c when c.Contains("ScrollView") || c.Contains("RecyclerView") => "ScrollView",
                            _ => a11y.Role
                        };

                        // Traits from Android node info
                        var traits = new List<string>();
                        if (nodeInfo.Clickable) traits.Add("Clickable");
                        if (nodeInfo.LongClickable) traits.Add("LongClickable");
                        if (nodeInfo.Checkable) traits.Add("Checkable");
                        if (nodeInfo.Checked) traits.Add("Checked");
                        if (nodeInfo.Selected) traits.Add("Selected");
                        if (nodeInfo.Scrollable) traits.Add("Scrollable");
                        if (nodeInfo.Focusable) traits.Add("Focusable");
                        if (nodeInfo.Editable) traits.Add("Editable");
                        if (nodeInfo.Heading) { traits.Add("Heading"); a11y.IsHeading = true; }
                        if (traits.Count > 0)
                            a11y.Traits = traits;

                        a11y.IsEnabled = nodeInfo.Enabled;
                        a11y.IsFocusable = nodeInfo.Focusable || nodeInfo.AccessibilityFocused;
                        a11y.IsFocused = nodeInfo.AccessibilityFocused || nodeInfo.Focused;
                        a11y.IsAccessibilityElement = nodeInfo.VisibleToUser &&
                            (nodeInfo.Clickable || nodeInfo.Focusable || !string.IsNullOrEmpty(nodeInfo.ContentDescription?.ToString())
                             || !string.IsNullOrEmpty(nodeInfo.Text?.ToString()));

                        // Live region
                        if ((int)nodeInfo.LiveRegion != 0)
                            a11y.LiveRegion = nodeInfo.LiveRegion.ToString();

                        a11y.ChildCount = nodeInfo.ChildCount;
                    }
                    finally
                    {
                        nodeInfo.Recycle();
                    }
                }
            }
#elif WINDOWS
            if (platformView is Microsoft.UI.Xaml.FrameworkElement fe)
            {
                var name = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(fe);
                if (!string.IsNullOrEmpty(name))
                    a11y.Label = name;

                var helpText = Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(fe);
                if (!string.IsNullOrEmpty(helpText))
                    a11y.Hint = helpText;

                var headingLevel = Microsoft.UI.Xaml.Automation.AutomationProperties.GetHeadingLevel(fe);
                if (headingLevel != Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.None)
                {
                    a11y.IsHeading = true;
                    a11y.Traits ??= new List<string>();
                    a11y.Traits.Add($"Heading:{headingLevel}");
                }

                var liveRegion = Microsoft.UI.Xaml.Automation.AutomationProperties.GetLiveSetting(fe);
                if (liveRegion != Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Off)
                    a11y.LiveRegion = liveRegion.ToString();

                var isRequired = Microsoft.UI.Xaml.Automation.AutomationProperties.GetIsRequiredForForm(fe);
                if (isRequired)
                {
                    a11y.Traits ??= new List<string>();
                    a11y.Traits.Add("RequiredForForm");
                }

                var accessKey = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAccessKey(fe);
                if (!string.IsNullOrEmpty(accessKey))
                {
                    a11y.Traits ??= new List<string>();
                    a11y.Traits.Add($"AccessKey:{accessKey}");
                }

                if (fe is Microsoft.UI.Xaml.Controls.Control control)
                {
                    a11y.IsEnabled = control.IsEnabled;
                    a11y.IsFocusable = control.IsTabStop;
                    a11y.IsFocused = control.FocusState != Microsoft.UI.Xaml.FocusState.Unfocused;
                }

                // Try to get the AutomationPeer for role
                try
                {
                    var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(fe);
                    if (peer != null)
                    {
                        var automationRole = peer.GetAutomationControlType();
                        a11y.Role ??= automationRole.ToString();
                        a11y.IsAccessibilityElement = !peer.IsOffscreen();

                        // Get announced name from peer if label not set
                        if (string.IsNullOrEmpty(a11y.Label))
                        {
                            var peerName = peer.GetName();
                            if (!string.IsNullOrEmpty(peerName))
                                a11y.Label = peerName;
                        }
                    }
                }
                catch { /* AutomationPeer may not be available for all elements */ }
            }
#elif MACOS
            if (platformView is AppKit.NSView nsView)
            {
                var nativeLabel = nsView.AccessibilityLabel;
                if (!string.IsNullOrEmpty(nativeLabel))
                    a11y.Label = nativeLabel;

                var nativeTitle = nsView.AccessibilityTitle;
                if (!string.IsNullOrEmpty(nativeTitle) && string.IsNullOrEmpty(a11y.Label))
                    a11y.Label = nativeTitle;

                var nativeHelp = nsView.AccessibilityHelp;
                if (!string.IsNullOrEmpty(nativeHelp))
                    a11y.Hint = nativeHelp;

                var nativeValue = nsView.AccessibilityValue?.ToString();
                if (!string.IsNullOrEmpty(nativeValue))
                    a11y.Value = nativeValue;

                var nativeRole = nsView.AccessibilityRole;
                if (nativeRole != null)
                    a11y.Role = nativeRole.ToString();

                a11y.IsAccessibilityElement = nsView.AccessibilityElement;
                a11y.IsFocusable = nsView.CanBecomeKeyView;
                a11y.IsFocused = nsView.Window?.FirstResponder == nsView;

                var children = nsView.AccessibilityChildren;
                if (children != null)
                    a11y.ChildCount = children.Length;
            }
#endif
        }
        catch
        {
            // Accessibility info is best-effort
        }
    }

    protected override BoundsInfo? ResolveSyntheticBounds(object marker)
    {
        try
        {
#if IOS || MACCATALYST
            return ResolveBoundsApple(marker);
#elif ANDROID
            return ResolveBoundsAndroid(marker);
#elif WINDOWS
            return ResolveBoundsWindows(marker);
#else
            return null;
#endif
        }
        catch { return null; }
    }

    protected override void PopulateSyntheticNativeInfo(ElementInfo info, object marker)
    {
        try
        {
#if IOS || MACCATALYST
            PopulateNativeInfoApple(info, marker);
#elif ANDROID
            PopulateNativeInfoAndroid(info, marker);
#elif WINDOWS
            PopulateNativeInfoWindows(info, marker);
#endif
        }
        catch { }
    }

    protected override BoundsInfo? ResolveWindowBounds(VisualElement ve)
    {
        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView == null) return null;

#if IOS || MACCATALYST
            if (platformView is UIKit.UIView uiView && uiView.Window != null)
            {
                var windowRect = uiView.ConvertRectToView(uiView.Bounds, uiView.Window.RootViewController?.View ?? uiView.Window);
                return new BoundsInfo
                {
                    X = windowRect.X,
                    Y = windowRect.Y,
                    Width = windowRect.Width,
                    Height = windowRect.Height
                };
            }
#elif ANDROID
            if (platformView is Android.Views.View androidView)
            {
                var location = new int[2];
                androidView.GetLocationInWindow(location);
                var density = androidView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
                return new BoundsInfo
                {
                    X = location[0] / density,
                    Y = location[1] / density,
                    Width = androidView.Width / density,
                    Height = androidView.Height / density
                };
            }
#elif WINDOWS
            if (platformView is Microsoft.UI.Xaml.UIElement uiElement)
            {
                var transform = uiElement.TransformToVisual(null);
                var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                if (uiElement is Microsoft.UI.Xaml.FrameworkElement fe)
                {
                    return new BoundsInfo
                    {
                        X = point.X,
                        Y = point.Y,
                        Width = fe.ActualWidth,
                        Height = fe.ActualHeight
                    };
                }
            }
#elif MACOS
            if (platformView is AppKit.NSView nsView && nsView.Window?.ContentView != null)
            {
                var windowRect = nsView.ConvertRectToView(nsView.Bounds, nsView.Window.ContentView);
                // NSView uses bottom-left origin; convert to top-left
                var contentHeight = nsView.Window.ContentView.Bounds.Height;
                return new BoundsInfo
                {
                    X = windowRect.X,
                    Y = contentHeight - windowRect.Y - windowRect.Height,
                    Width = windowRect.Width,
                    Height = windowRect.Height
                };
            }
#endif
            return null;
        }
        catch { return null; }
    }

#if IOS || MACCATALYST
    private BoundsInfo? ResolveBoundsApple(object marker)
    {
        Shell? shell = marker switch
        {
            FlyoutButtonMarker m => m.Shell,
            ShellFlyoutItemMarker m => m.Shell,
            ShellTabMarker m => m.Shell,
            NavBarTitleMarker => Shell.Current,
            SearchHandlerMarker => Shell.Current,
            ToolbarItem => Shell.Current,
            _ => null
        };

        if (shell?.Handler?.PlatformView is not UIView shellView)
            return null;

        // Find UINavigationBar for nav bar elements
        if (marker is NavBarTitleMarker or FlyoutButtonMarker or SearchHandlerMarker or ToolbarItem)
        {
            var navBar = FindSubview<UINavigationBar>(shellView);
            if (navBar != null)
            {
                if (marker is ToolbarItem ti)
                {
                    // Find the button matching this toolbar item in the nav bar
                    var button = FindToolbarButton(navBar, ti, shellView);
                    if (button != null) return button;
                }

                var frame = navBar.ConvertRectToView(navBar.Bounds, shellView);
                if (marker is FlyoutButtonMarker)
                {
                    // Flyout button is in the left area of the nav bar
                    return new BoundsInfo
                    {
                        X = frame.X,
                        Y = frame.Y,
                        Width = 44,
                        Height = frame.Height
                    };
                }
                return new BoundsInfo
                {
                    X = frame.X,
                    Y = frame.Y,
                    Width = frame.Width,
                    Height = frame.Height
                };
            }
        }

        // Find UITabBar for tab elements
        if (marker is ShellTabMarker)
        {
            var tabBar = FindSubview<UITabBar>(shellView);
            if (tabBar != null)
            {
                var frame = tabBar.ConvertRectToView(tabBar.Bounds, shellView);
                return new BoundsInfo
                {
                    X = frame.X,
                    Y = frame.Y,
                    Width = frame.Width,
                    Height = frame.Height
                };
            }
        }

        return null;
    }

    private static BoundsInfo? FindToolbarButton(UINavigationBar navBar, ToolbarItem ti, UIView rootView)
    {
        // Search for any interactive view in the nav bar matching the toolbar item
        var match = FindMatchingView(navBar, ti);
        if (match != null)
        {
            var frame = match.ConvertRectToView(match.Bounds, rootView);
            return new BoundsInfo
            {
                X = frame.X,
                Y = frame.Y,
                Width = frame.Width,
                Height = frame.Height
            };
        }
        return null;
    }

    private static UIView? FindMatchingView(UIView root, ToolbarItem ti)
    {
        // Check this view's accessibility label/identifier against the toolbar item
        var accessLabel = root.AccessibilityLabel;
        var accessId = root.AccessibilityIdentifier;
        var title = (root as UIButton)?.CurrentTitle;

        if ((!string.IsNullOrEmpty(ti.Text) && (title == ti.Text || accessLabel == ti.Text))
            || (!string.IsNullOrEmpty(ti.AutomationId) && accessId == ti.AutomationId))
        {
            // Prefer interactive leaf views — only match if clickable or if no subviews
            if (root.UserInteractionEnabled && root.Bounds.Width > 0 && root.Bounds.Height > 0)
                return root;
        }

        // Recurse into subviews, preferring deeper (more specific) matches
        foreach (var sub in root.Subviews)
        {
            var found = FindMatchingView(sub, ti);
            if (found != null) return found;
        }

        return null;
    }

    private static T? FindSubview<T>(UIView root) where T : UIView
    {
        if (root is T match) return match;
        foreach (var sub in root.Subviews)
        {
            var found = FindSubview<T>(sub);
            if (found != null) return found;
        }
        return null;
    }

    private void PopulateNativeInfoApple(ElementInfo info, object marker)
    {
        Shell? shell = marker switch
        {
            FlyoutButtonMarker m => m.Shell,
            ShellFlyoutItemMarker m => m.Shell,
            ShellTabMarker m => m.Shell,
            NavBarTitleMarker => Shell.Current,
            _ => null
        };

        if (shell?.Handler?.PlatformView is UIView shellView)
        {
            if (marker is NavBarTitleMarker or FlyoutButtonMarker)
            {
                var navBar = FindSubview<UINavigationBar>(shellView);
                if (navBar != null) info.NativeType = navBar.GetType().FullName;
            }
            else if (marker is ShellTabMarker)
            {
                var tabBar = FindSubview<UITabBar>(shellView);
                if (tabBar != null) info.NativeType = tabBar.GetType().FullName;
            }
        }
    }
#endif

#if ANDROID
    private BoundsInfo? ResolveBoundsAndroid(object marker)
    {
        Shell? shell = marker switch
        {
            FlyoutButtonMarker m => m.Shell,
            ShellFlyoutItemMarker m => m.Shell,
            ShellTabMarker m => m.Shell,
            NavBarTitleMarker => Shell.Current,
            ToolbarItem => Shell.Current,
            _ => null
        };

        if (shell?.Handler?.PlatformView is not Android.Views.View shellView)
            return null;

        var density = shellView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;

        if (marker is NavBarTitleMarker or FlyoutButtonMarker or ToolbarItem)
        {
            var toolbar = FindAndroidView<AndroidX.AppCompat.Widget.Toolbar>(shellView);
            if (toolbar != null)
            {
                // For ToolbarItem, try to find the specific action view
                if (marker is ToolbarItem ti)
                {
                    var actionView = FindAndroidToolbarButton(toolbar, ti);
                    if (actionView != null)
                    {
                        var loc = new int[2];
                        actionView.GetLocationInWindow(loc);
                        return new BoundsInfo
                        {
                            X = loc[0] / density,
                            Y = loc[1] / density,
                            Width = actionView.Width / density,
                            Height = actionView.Height / density
                        };
                    }
                }

                // For FlyoutButton, find the navigation ImageButton
                if (marker is FlyoutButtonMarker)
                {
                    var navButton = FindAndroidNavigationButton(toolbar);
                    if (navButton != null)
                    {
                        var loc = new int[2];
                        navButton.GetLocationInWindow(loc);
                        return new BoundsInfo
                        {
                            X = loc[0] / density,
                            Y = loc[1] / density,
                            Width = navButton.Width / density,
                            Height = navButton.Height / density
                        };
                    }
                }

                var location = new int[2];
                toolbar.GetLocationOnScreen(location);
                var shellLocation = new int[2];
                shellView.GetLocationOnScreen(shellLocation);

                return new BoundsInfo
                {
                    X = (location[0] - shellLocation[0]) / density,
                    Y = (location[1] - shellLocation[1]) / density,
                    Width = toolbar.Width / density,
                    Height = toolbar.Height / density
                };
            }
        }

        if (marker is ShellTabMarker)
        {
            var bottomNav = FindAndroidView<Google.Android.Material.BottomNavigation.BottomNavigationView>(shellView);
            if (bottomNav != null)
            {
                var location = new int[2];
                bottomNav.GetLocationOnScreen(location);
                var shellLocation = new int[2];
                shellView.GetLocationOnScreen(shellLocation);

                return new BoundsInfo
                {
                    X = (location[0] - shellLocation[0]) / density,
                    Y = (location[1] - shellLocation[1]) / density,
                    Width = bottomNav.Width / density,
                    Height = bottomNav.Height / density
                };
            }
        }

        return null;
    }

    private static T? FindAndroidView<T>(Android.Views.View root) where T : Android.Views.View
    {
        if (root is T match) return match;
        if (root is Android.Views.ViewGroup vg)
        {
            for (int i = 0; i < vg.ChildCount; i++)
            {
                var child = vg.GetChildAt(i);
                if (child != null)
                {
                    var found = FindAndroidView<T>(child);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }

    private static Android.Views.View? FindAndroidToolbarButton(AndroidX.AppCompat.Widget.Toolbar toolbar, ToolbarItem ti)
    {
        // Search toolbar's descendants recursively — action buttons are nested
        // inside ActionMenuView/LinearLayoutCompat, not direct children.
        // ContentDescription may be set to AutomationId or Text, so check both.
        return FindToolbarButtonRecursive(toolbar, ti);

        static Android.Views.View? FindToolbarButtonRecursive(Android.Views.ViewGroup parent, ToolbarItem ti)
        {
            for (int i = 0; i < parent.ChildCount; i++)
            {
                var child = parent.GetChildAt(i);
                if (child == null) continue;

                var desc = child.ContentDescription;
                if (!string.IsNullOrEmpty(desc))
                {
                    if (desc == ti.Text || desc == ti.AutomationId)
                        return child;
                }
                if (child is Android.Widget.TextView tv && tv.Text == ti.Text)
                    return child;

                if (child is Android.Views.ViewGroup vg)
                {
                    var found = FindToolbarButtonRecursive(vg, ti);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }

    private static Android.Views.View? FindAndroidNavigationButton(AndroidX.AppCompat.Widget.Toolbar toolbar)
    {
        // The navigation/hamburger button is an ImageButton direct child of the toolbar
        for (int i = 0; i < toolbar.ChildCount; i++)
        {
            var child = toolbar.GetChildAt(i);
            if (child is Android.Widget.ImageButton)
                return child;
        }
        return null;
    }

    private void PopulateNativeInfoAndroid(ElementInfo info, object marker)
    {
        Shell? shell = marker switch
        {
            FlyoutButtonMarker m => m.Shell,
            ShellFlyoutItemMarker m => m.Shell,
            ShellTabMarker m => m.Shell,
            NavBarTitleMarker => Shell.Current,
            _ => null
        };

        if (shell?.Handler?.PlatformView is Android.Views.View shellView)
        {
            if (marker is NavBarTitleMarker or FlyoutButtonMarker)
            {
                var toolbar = FindAndroidView<AndroidX.AppCompat.Widget.Toolbar>(shellView);
                if (toolbar != null) info.NativeType = toolbar.GetType().FullName ?? toolbar.Class?.Name;
            }
            else if (marker is ShellTabMarker)
            {
                var bottomNav = FindAndroidView<Google.Android.Material.BottomNavigation.BottomNavigationView>(shellView);
                if (bottomNav != null) info.NativeType = bottomNav.GetType().FullName ?? bottomNav.Class?.Name;
            }
        }
    }
#endif

    protected override string? EnsurePlatformStableId(object platformObj)
    {
        try
        {
#if IOS || MACCATALYST
            if (platformObj is UIKit.UIView uiView)
            {
                if (string.IsNullOrEmpty(uiView.AccessibilityIdentifier))
                    uiView.AccessibilityIdentifier = Guid.NewGuid().ToString();
                return uiView.AccessibilityIdentifier;
            }
#elif ANDROID
            if (platformObj is Android.Views.View androidView)
            {
                var existing = androidView.ContentDescription;
                if (string.IsNullOrEmpty(existing))
                {
                    existing = Guid.NewGuid().ToString();
                    androidView.ContentDescription = existing;
                }
                return existing;
            }
#elif WINDOWS
            if (platformObj is Microsoft.UI.Xaml.UIElement uiElement)
            {
                var existing = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(uiElement);
                if (string.IsNullOrEmpty(existing))
                {
                    existing = Guid.NewGuid().ToString();
                    uiElement.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AutomationIdProperty, existing);
                }
                return existing;
            }
#elif MACOS
            if (platformObj is AppKit.NSView nsView)
            {
                if (string.IsNullOrEmpty(nsView.AccessibilityIdentifier))
                    nsView.AccessibilityIdentifier = Guid.NewGuid().ToString();
                return nsView.AccessibilityIdentifier;
            }
#endif
        }
        catch { }
        return null;
    }

#if WINDOWS
    private BoundsInfo? ResolveBoundsWindows(object marker)
    {
        // Windows NavigationView doesn't expose easily queryable sub-parts
        // for nav bar / tab regions. Return null for now — can be enhanced later.
        return null;
    }

    private void PopulateNativeInfoWindows(ElementInfo info, object marker)
    {
        Shell? shell = marker switch
        {
            FlyoutButtonMarker m => m.Shell,
            ShellFlyoutItemMarker m => m.Shell,
            ShellTabMarker m => m.Shell,
            NavBarTitleMarker => Shell.Current,
            _ => null
        };

        if (shell?.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement fe)
        {
            info.NativeType = fe.GetType().FullName;
        }
    }
#endif

    // -----------------------------------------------------------------------
    // Native Accessibility Tree Walk
    // Returns elements in the exact order the platform screen reader visits them.
    // -----------------------------------------------------------------------

    public override List<NativeScreenReaderEntry> GetNativeA11yTree(Application app, int windowIndex)
    {
        var results = new List<NativeScreenReaderEntry>();
        try
        {
            var nativeToId = BuildNativeViewToIdMap(app, windowIndex);
            var order = 0;

#if IOS || MACCATALYST
            var window = windowIndex >= 0 && windowIndex < app.Windows.Count
                ? app.Windows[windowIndex] : app.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is UIKit.UIWindow uiWindow
                && uiWindow.RootViewController?.View is UIKit.UIView rootView)
            {
                var visited = new HashSet<IntPtr>();
                WalkIosA11yTree(rootView, uiWindow, nativeToId, results, ref order, visited, 0);
            }
#elif ANDROID
            var window = windowIndex >= 0 && windowIndex < app.Windows.Count
                ? app.Windows[windowIndex] : app.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is Android.App.Activity activity
                && activity.Window?.DecorView is Android.Views.ViewGroup decorView)
            {
                var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
                var visited = new HashSet<int>();
                WalkAndroidA11yTree(decorView, nativeToId, results, ref order, density, visited, 0);
            }
#elif WINDOWS
            var window = windowIndex >= 0 && windowIndex < app.Windows.Count
                ? app.Windows[windowIndex] : app.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window winWindow
                && winWindow.Content is Microsoft.UI.Xaml.UIElement rootContent)
            {
                var visited = new HashSet<int>();
                WalkWindowsA11yTree(rootContent, nativeToId, results, ref order, visited, 0);
            }
#elif MACOS
            var nsWindow = AppKit.NSApplication.SharedApplication.KeyWindow
                ?? (app.Windows.ElementAtOrDefault(windowIndex)?.Handler?.PlatformView as AppKit.NSWindow);
            if (nsWindow?.ContentView is AppKit.NSView contentView)
            {
                var visited = new HashSet<IntPtr>();
                WalkMacA11yTree(contentView, nsWindow.ContentView!, nativeToId, results, ref order, visited, 0);
            }
#endif
        }
        catch { }
        return results;
    }

#if IOS || MACCATALYST
    private const int MaxA11yDepth   = 60;
    private const int MaxA11yResults = 800;

    private static void WalkIosA11yTree(
        UIKit.UIView view,
        UIKit.UIWindow window,
        Dictionary<object, string> nativeToId,
        List<NativeScreenReaderEntry> results,
        ref int order,
        HashSet<IntPtr> visited,
        int depth)
    {
        if (results.Count >= MaxA11yResults || depth > MaxA11yDepth) return;

        // Cycle guard — some containers return themselves or ancestors
        if (!visited.Add(view.Handle)) return;

        // Hidden from accessibility — skip entirely
        if (view.AccessibilityElementsHidden || view.Hidden || view.Alpha < 0.01f)
            return;

        // Views whose accessibility trees involve IPC / expensive async work that
        // can deadlock the main thread — skip them as opaque nodes.
        var typeName = view.GetType().Name;
        if (view is WebKit.WKWebView
            || typeName.Contains("MKMapView", StringComparison.Ordinal)
            || typeName.Contains("GLKView",   StringComparison.Ordinal)
            || typeName.Contains("MTKView",   StringComparison.Ordinal)
            || typeName.Contains("SCNView",   StringComparison.Ordinal))
            return;

        // This view is an accessibility leaf — VoiceOver stops here and announces it
        if (view.IsAccessibilityElement)
        {
            var rootView = window.RootViewController?.View ?? window;
            var rect = view.ConvertRectToView(view.Bounds, rootView);
            var traits = view.AccessibilityTraits;
            results.Add(new NativeScreenReaderEntry
            {
                Order = order++,
                Label    = view.AccessibilityLabel ?? HarvestIosLabel(view),
                Hint     = view.AccessibilityHint,
                Value    = view.AccessibilityValue?.ToString(),
                Role     = GetIosRole(traits),
                Traits   = GetIosTraits(traits),
                IsHeading = traits.HasFlag(UIKit.UIAccessibilityTrait.Header),
                WindowBounds = new BoundsInfo { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height },
                ElementId  = nativeToId.TryGetValue(view, out var eid) ? eid : null,
                NativeType = typeName,
            });
            return; // Don't recurse — VoiceOver treats children as part of this element
        }

        // Explicit ordering via IUIAccessibilityContainer overrides visual subview order.
        // IMPORTANT: every UIView conforms to UIAccessibilityContainer. Views that do NOT
        // explicitly override it return NSNotFound (== nint.MaxValue on 64-bit) from
        // AccessibilityElementCount(). Without a guard, `count > 0` passes for MaxValue and
        // the loop runs forever while GetAccessibilityElementAt keeps returning null.
        // We treat counts > MaxA11yResults as "not explicitly set" (NSNotFound sentinel).
        if (view is UIKit.IUIAccessibilityContainer container)
        {
            nint count;
            try { count = container.AccessibilityElementCount(); }
            catch { count = -1; }

            if (count > 0 && count <= MaxA11yResults)
            {
                for (nint i = 0; i < count && results.Count < MaxA11yResults; i++)
                {
                    Foundation.NSObject? child;
                    try { child = container.GetAccessibilityElementAt(i); }
                    catch { continue; }
                    if (child is UIKit.UIView childView)
                        WalkIosA11yTree(childView, window, nativeToId, results, ref order, visited, depth + 1);
                }
                return;
            }
        }

        // Walk subviews — VoiceOver visits them in subview order (which matches layout order)
        foreach (var subview in view.Subviews)
            WalkIosA11yTree(subview, window, nativeToId, results, ref order, visited, depth + 1);
    }

    /// <summary>
    /// When AccessibilityLabel is null (e.g. UITabBarButton synthesises its label
    /// dynamically via ObjC override), recursively scan subviews for any text content.
    /// Depth-limited to avoid expensive traversal.
    /// </summary>
    private static string? HarvestIosLabel(UIKit.UIView view, int maxDepth = 3)
    {
        if (maxDepth <= 0) return null;
        foreach (var sub in view.Subviews)
        {
            var text = sub switch
            {
                UIKit.UILabel lbl when !string.IsNullOrEmpty(lbl.Text) => lbl.Text,
                UIKit.UIButton btn when !string.IsNullOrEmpty(btn.CurrentTitle) => btn.CurrentTitle,
                _ => null,
            };
            if (text != null) return text;
            text = HarvestIosLabel(sub, maxDepth - 1);
            if (text != null) return text;
        }
        return null;
    }

    private static string? GetIosRole(UIKit.UIAccessibilityTrait traits)
    {
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.Button))     return "Button";
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.Link))       return "Link";
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.Header))     return "Header";
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.SearchField))return "SearchField";
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.Image))      return "Image";
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.Adjustable)) return "Slider";
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.KeyboardKey))return "Key";
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.StaticText)) return "Text";
        return null;
    }

    private static List<string>? GetIosTraits(UIKit.UIAccessibilityTrait traits)
    {
        var list = new List<string>();
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.Button))            list.Add("Button");
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.Link))              list.Add("Link");
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.Header))            list.Add("Header");
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.SearchField))       list.Add("SearchField");
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.Image))             list.Add("Image");
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.Selected))          list.Add("Selected");
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.NotEnabled))        list.Add("Disabled");
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.Adjustable))        list.Add("Adjustable");
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.UpdatesFrequently)) list.Add("LiveRegion");
        if (traits.HasFlag(UIKit.UIAccessibilityTrait.StaticText))        list.Add("StaticText");
        return list.Count > 0 ? list : null;
    }
#endif

#if ANDROID
    private const int MaxA11yDepth   = 60;
    private const int MaxA11yResults = 800;

    private static void WalkAndroidA11yTree(
        Android.Views.View view,
        Dictionary<object, string> nativeToId,
        List<NativeScreenReaderEntry> results,
        ref int order,
        float density,
        HashSet<int> visited,
        int depth)
    {
        if (results.Count >= MaxA11yResults || depth > MaxA11yDepth) return;

        // Cycle guard using object identity
        if (!visited.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(view))) return;

        if (!view.IsShown) return;

        // Skip views whose a11y trees live in another process
        if (view is Android.Webkit.WebView
            || view.GetType().Name.Contains("MapView", StringComparison.Ordinal))
            return;

        // NoHideDescendants hides this view AND its entire subtree from accessibility
        if (view.ImportantForAccessibility == Android.Views.ImportantForAccessibility.NoHideDescendants)
            return;

        // Per-view AccessibilityNodeInfo: replicate TalkBack's focusability predicate.
        //
        // Key rules:
        //  1. Do NOT filter by VisibleToUser — TalkBack navigates to off-screen elements
        //     in scrollable containers (it scrolls the container to bring them into view).
        //     view.IsShown above already excludes truly GONE/INVISIBLE views.
        //  2. Do NOT use Focusable (keyboard focus) — TalkBack has its own focus concept.
        //     TalkBack focuses an element when it is Clickable, LongClickable, Checkable,
        //     or has an announced label/text.
        var nodeInfo = view.CreateAccessibilityNodeInfo();
        if (nodeInfo != null)
        {
            try
            {
                var hasLabel = !string.IsNullOrEmpty(nodeInfo.ContentDescription?.ToString())
                            || !string.IsNullOrEmpty(nodeInfo.Text?.ToString());
                var isA11yFocusable = nodeInfo.Clickable
                    || nodeInfo.LongClickable
                    || nodeInfo.Checkable
                    || hasLabel;

                if (isA11yFocusable)
                {
                    var location = new int[2];
                    view.GetLocationInWindow(location);
                    bool isHeading = false;
                    try { isHeading = nodeInfo.Heading; } catch { }

                    results.Add(new NativeScreenReaderEntry
                    {
                        Order     = order++,
                        Label     = nodeInfo.ContentDescription?.ToString() ?? nodeInfo.Text?.ToString(),
                        Role      = GetAndroidRoleFromNode(nodeInfo),
                        IsHeading = isHeading,
                        WindowBounds = new BoundsInfo
                        {
                            X      = location[0] / density,
                            Y      = location[1] / density,
                            Width  = view.Width   / density,
                            Height = view.Height  / density,
                        },
                        ElementId  = nativeToId.TryGetValue(view, out var eid) ? eid : null,
                        NativeType = view.GetType().Name,
                    });
                }
            }
            finally
            {
                nodeInfo.Recycle();
            }
        }

        if (view is not Android.Views.ViewGroup viewGroup) return;

        // Sort children top-to-bottom, left-to-right — TalkBack reading order
        var children = Enumerable.Range(0, viewGroup.ChildCount)
            .Select(i => viewGroup.GetChildAt(i))
            .Where(c => c != null)
            .ToList();
        children.Sort((a, b) =>
        {
            var aLoc = new int[2]; a!.GetLocationInWindow(aLoc);
            var bLoc = new int[2]; b!.GetLocationInWindow(bLoc);
            var yDiff = aLoc[1] - bLoc[1];
            return yDiff != 0 ? yDiff : (aLoc[0] - bLoc[0]);
        });

        foreach (var child in children)
            WalkAndroidA11yTree(child!, nativeToId, results, ref order, density, visited, depth + 1);
    }

    private static string? AndroidEffectiveBackgroundColor(Android.Views.View? view, int maxDepth = 20)
    {
        var current = view;
        var depth = 0;
        while (current != null && depth < maxDepth)
        {
            if (current.Background is Android.Graphics.Drawables.ColorDrawable cd)
            {
                var argb = cd.Color.ToArgb();
                var a = (argb >> 24) & 0xFF;
                if (a > 10) // non-transparent
                    return $"#{a:X2}{(argb >> 16) & 0xFF:X2}{(argb >> 8) & 0xFF:X2}{argb & 0xFF:X2}";
            }
            current = current.Parent as Android.Views.View;
            depth++;
        }
        return null;
    }

    private static string? GetAndroidRoleFromNode(Android.Views.Accessibility.AccessibilityNodeInfo node)
    {
        var cn = node.ClassName?.ToString() ?? string.Empty;
        // More specific class names must come before their base classes
        if (cn.EndsWith("CheckBox",    StringComparison.Ordinal)) return "Checkbox";
        if (cn.EndsWith("Switch",      StringComparison.Ordinal)) return "Switch";
        if (cn.EndsWith("RadioButton", StringComparison.Ordinal)) return "RadioButton";
        if (cn.EndsWith("SeekBar",     StringComparison.Ordinal)) return "Slider";
        if (cn.EndsWith("EditText",    StringComparison.Ordinal)) return "TextField";
        if (cn.EndsWith("ImageButton", StringComparison.Ordinal)) return "Button";
        if (cn.EndsWith("Button",      StringComparison.Ordinal)) return "Button";
        if (cn.EndsWith("RecyclerView",StringComparison.Ordinal)) return "List";
        if (node.Clickable)                                        return "Button";
        return null;
    }
#endif

#if WINDOWS
    private const int MaxA11yDepth   = 60;
    private const int MaxA11yResults = 800;

    private static void WalkWindowsA11yTree(
        Microsoft.UI.Xaml.UIElement element,
        Dictionary<object, string> nativeToId,
        List<NativeScreenReaderEntry> results,
        ref int order,
        HashSet<int> visited,
        int depth)
    {
        if (results.Count >= MaxA11yResults || depth > MaxA11yDepth) return;
        if (!visited.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(element))) return;

        if (element.Visibility != Microsoft.UI.Xaml.Visibility.Visible) return;

        // Skip WebView2 — its a11y tree is backed by the browser process
        if (element.GetType().Name.Contains("WebView", StringComparison.Ordinal)
            || element.GetType().Name.Contains("MapControl", StringComparison.Ordinal))
            return;

        // Try to get AutomationPeer — if it reports content, treat as leaf
        if (element is Microsoft.UI.Xaml.FrameworkElement fe)
        {
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(fe);
            if (peer != null && peer.IsContentElementCore())
            {
                var name = peer.GetName();
                var controlType = peer.GetAutomationControlType();
                if (!string.IsNullOrEmpty(name) || controlType != Microsoft.UI.Xaml.Automation.Peers.AutomationControlType.Custom)
                {
                    var transform = fe.TransformToVisual(null);
                    var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                    results.Add(new NativeScreenReaderEntry
                    {
                        Order  = order++,
                        Label  = name,
                        Role   = controlType.ToString(),
                        WindowBounds = new BoundsInfo { X = pt.X, Y = pt.Y, Width = fe.ActualWidth, Height = fe.ActualHeight },
                        ElementId  = nativeToId.TryGetValue(element, out var eid) ? eid : null,
                        NativeType = element.GetType().Name,
                    });
                    return;
                }
            }
        }

        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < count; i++)
        {
            if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i) is Microsoft.UI.Xaml.UIElement child)
                WalkWindowsA11yTree(child, nativeToId, results, ref order, visited, depth + 1);
        }
    }
#endif

#if MACOS
    private const int MaxA11yDepth   = 60;
    private const int MaxA11yResults = 800;

    private static void WalkMacA11yTree(
        AppKit.NSView view,
        AppKit.NSView rootView,
        Dictionary<object, string> nativeToId,
        List<NativeScreenReaderEntry> results,
        ref int order,
        HashSet<IntPtr> visited,
        int depth)
    {
        if (results.Count >= MaxA11yResults || depth > MaxA11yDepth) return;
        if (!visited.Add(view.Handle)) return;

        if (view.Hidden) return;

        // WKWebView / MapKit — AccessibilityChildren involves IPC, skip
        if (view is WebKit.WKWebView
            || view.GetType().Name.Contains("MKMapView", StringComparison.Ordinal))
            return;

        var a11yChildren = view.AccessibilityChildren;
        bool hasChildren = a11yChildren != null && a11yChildren.Length > 0;
        var label = view.AccessibilityLabel ?? view.AccessibilityTitle;

        // Treat as leaf when no accessible children and has meaningful content
        if (!hasChildren && !string.IsNullOrEmpty(label))
        {
            var windowRect = view.ConvertRectToView(view.Bounds, rootView);
            var contentHeight = rootView.Bounds.Height;
            results.Add(new NativeScreenReaderEntry
            {
                Order  = order++,
                Label  = label,
                Hint   = view.AccessibilityHelp,
                Value  = view.AccessibilityValue?.ToString(),
                Role   = view.AccessibilityRole,
                WindowBounds = new BoundsInfo
                {
                    X = windowRect.X,
                    Y = contentHeight - windowRect.Y - windowRect.Height, // flip Y (NSView is bottom-left)
                    Width  = windowRect.Width,
                    Height = windowRect.Height,
                },
                ElementId  = nativeToId.TryGetValue(view, out var eid) ? eid : null,
                NativeType = view.GetType().Name,
            });
            return;
        }

        if (hasChildren)
        {
            foreach (var child in a11yChildren!)
            {
                if (child is AppKit.NSView childView)
                    WalkMacA11yTree(childView, rootView, nativeToId, results, ref order, visited, depth + 1);
            }
        }
        else
        {
            foreach (var subview in view.Subviews)
                WalkMacA11yTree(subview, rootView, nativeToId, results, ref order, visited, depth + 1);
        }
    }
#endif

    // -----------------------------------------------------------------------
    // Color helpers
    // -----------------------------------------------------------------------

#if IOS || MACCATALYST
    /// <summary>
    /// Walks up the superview chain to find the first non-transparent background color.
    /// Most UILabels/UIButtons have UIColor.Clear; the visible background is on a parent.
    /// </summary>
    private static string? IosEffectiveBackgroundColor(UIKit.UIView? view, int maxDepth = 20)
    {
        var current = view;
        var depth = 0;
        while (current != null && depth < maxDepth)
        {
            var hex = IosColorToHex(current.BackgroundColor);
            if (hex != null) return hex;
            current = current.Superview;
            depth++;
        }
        return null;
    }

    private static string? IosColorToHex(UIKit.UIColor? color)
    {
        if (color == null) return null;
        color.GetRGBA(out var r, out var g, out var b, out var a);
        if (a < 0.01f) return null; // fully transparent — not useful for contrast
        return $"#{(int)(a * 255):X2}{(int)(r * 255):X2}{(int)(g * 255):X2}{(int)(b * 255):X2}";
    }
#endif

#if MACOS
    private static string? MacColorToHex(AppKit.NSColor? color)
    {
        if (color == null) return null;
        try
        {
            var rgb = color.UsingColorSpace(AppKit.NSColorSpace.DeviceRGB);
            if (rgb == null) return null;
            rgb.GetRgba(out var r, out var g, out var b, out var a);
            if (a < 0.01f) return null;
            return $"#{(int)(a * 255):X2}{(int)(r * 255):X2}{(int)(g * 255):X2}{(int)(b * 255):X2}";
        }
        catch { return null; }
    }
#endif
}
