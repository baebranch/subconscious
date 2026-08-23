using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Maui;
using System.Runtime.Versioning;

namespace Subconscious.Mobile;

[Activity(Theme = "@style/Subconscious.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private MobileAppearancePreferences? _appearancePreferences;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var platformApplication = IPlatformApplication.Current;
        _appearancePreferences = platformApplication?.Services.GetService(typeof(MobileAppearancePreferences)) as MobileAppearancePreferences;
        if (_appearancePreferences is not null)
        {
            _appearancePreferences.AppearanceChanged += OnAppearanceChanged;
        }

        ApplySystemBarAppearance();
        // The native splash theme owns the initial bar. Reapply after the first decor pass so
        // the persisted MobileSurfaceColor replaces the fallback as soon as MAUI is visible.
        Window?.DecorView?.Post(ApplySystemBarAppearance);
    }

    protected override void OnDestroy()
    {
        if (_appearancePreferences is not null)
        {
            _appearancePreferences.AppearanceChanged -= OnAppearanceChanged;
        }

        base.OnDestroy();
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) => RunOnUiThread(ApplySystemBarAppearance);

    private void ApplySystemBarAppearance()
    {
        var application = Microsoft.Maui.Controls.Application.Current;
        if (Window is null ||
            application?.Resources.TryGetValue("MobileSurfaceColor", out var resource) != true ||
            resource is not Microsoft.Maui.Graphics.Color surface)
        {
            return;
        }

        var nativeSurface = Android.Graphics.Color.Argb(
            ToChannel(surface.Alpha),
            ToChannel(surface.Red),
            ToChannel(surface.Green),
            ToChannel(surface.Blue));

        // Android 15 enforces edge-to-edge and ignores status/navigation bar colors. The decor
        // and full-screen content roots are therefore the backgrounds visible through transparent
        // system bars, while older Android releases still receive explicit system-bar colors.
        // Android 15 exposes the complete AppCompat root hierarchy through transparent system
        // bars. Color every wrapper from android:id/content through DecorView so no inherited
        // colorPrimary layer remains visible in the status-bar inset.
        ApplyWindowBackground(nativeSurface);
        // Derive icon contrast from the same dynamic surface that colors the bars. This keeps
        // native chrome synchronized even while MAUI is transitioning between light and dark.
        var useDarkIcons = IsLightSurface(surface);
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            ApplyModernSystemBarIconAppearance(useDarkIcons);
        }
        else
        {
            ApplyLegacySystemBarAppearance(useDarkIcons);
        }

        // Android 15 ignores runtime bar colors; Subconscious.SplashTheme keeps those
        // platform layers transparent so these dynamic root backgrounds show through.
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            ApplyPreAndroid15SystemBarColors(nativeSurface);
        }
    }

    [SupportedOSPlatform("android30.0")]
    private void ApplyModernSystemBarIconAppearance(bool useDarkIcons)
    {
        var controller = Window?.InsetsController;
        if (controller is null) return;

        var mask = (int)(WindowInsetsControllerAppearance.LightStatusBars |
            WindowInsetsControllerAppearance.LightNavigationBars);
        controller.SetSystemBarsAppearance(useDarkIcons ? mask : 0, mask);
    }

    [SupportedOSPlatform("android21.0")]
    [UnsupportedOSPlatform("android30.0")]
    private void ApplyLegacySystemBarAppearance(bool useDarkIcons)
    {
        if (Window?.DecorView is not { } decorView) return;

        var systemUi = decorView.SystemUiFlags;
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            systemUi = useDarkIcons
                ? systemUi | SystemUiFlags.LightStatusBar
                : systemUi & ~SystemUiFlags.LightStatusBar;
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            systemUi = useDarkIcons
                ? systemUi | SystemUiFlags.LightNavigationBar
                : systemUi & ~SystemUiFlags.LightNavigationBar;
        }
        decorView.SystemUiFlags = systemUi;
    }

    [SupportedOSPlatform("android21.0")]
    [UnsupportedOSPlatform("android35.0")]
    private void ApplyPreAndroid15SystemBarColors(Android.Graphics.Color surface)
    {
        Window?.SetStatusBarColor(surface);
        Window?.SetNavigationBarColor(surface);
    }

    private void ApplyWindowBackground(Android.Graphics.Color surface)
    {
        var decorView = Window?.DecorView;
        Android.Views.View? current = Window?.FindViewById(Android.Resource.Id.Content);
        while (current is not null)
        {
            current.SetBackgroundColor(surface);
            if (ReferenceEquals(current, decorView)) break;
            current = current.Parent as Android.Views.View;
        }

        decorView?.SetBackgroundColor(surface);
        if (decorView is not null && OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            ApplyAndroid15StatusBarBackground(decorView, surface);
        }
    }

    [SupportedOSPlatform("android35.0")]
    private void ApplyAndroid15StatusBarBackground(Android.Views.View decorView, Android.Graphics.Color surface)
    {
        const string backgroundTag = "subconscious-status-bar-background";
        var tag = new Java.Lang.String(backgroundTag);
        var background = decorView.FindViewWithTag(tag);
        if (background is null && decorView is ViewGroup root)
        {
            background = new Android.Views.View(this)
            {
                Tag = tag,
                Clickable = false,
                Focusable = false,
                ImportantForAccessibility = ImportantForAccessibility.No,
                TranslationZ = 1000f,
            };
            root.AddView(background);
        }

        var statusBars = decorView.RootWindowInsets?.GetInsets(WindowInsets.Type.StatusBars());
        if (background is null || statusBars is null) return;

        background.SetBackgroundColor(surface);
        background.LayoutParameters = new Android.Widget.FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            statusBars.Top,
            GravityFlags.Top);
        background.RequestLayout();
    }

    private static bool IsLightSurface(Microsoft.Maui.Graphics.Color color) =>
        color.Red * 0.299f + color.Green * 0.587f + color.Blue * 0.114f > 0.5f;

    private static int ToChannel(float value) => (int)Math.Round(Math.Clamp(value, 0f, 1f) * 255);
}
