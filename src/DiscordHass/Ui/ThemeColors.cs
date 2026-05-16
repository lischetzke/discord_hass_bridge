using System;
using System.Drawing;
using Microsoft.Win32;

namespace DiscordHass.Ui;

/// <summary>
/// Centralised colour palette that auto-selects light or dark variants based on the user's
/// Windows app-theme preference. Read once at process start (or on first access — same thing for
/// a tray app); WinForms can't live-switch theming without restart, so changing your Windows
/// theme while the app is running has no effect until next launch.
///
/// Known limitations:
///  - WinForms <see cref="System.Windows.Forms.TabControl"/> headers and standard
///    <see cref="System.Windows.Forms.Button"/> chrome are drawn by the OS visual-styles engine
///    and don't honour <c>BackColor</c>. Accept that those small surfaces stay in system colours;
///    body content (panels, labels, hint text, tile backgrounds) is what we recolour.
/// </summary>
internal static class ThemeColors
{
    private static readonly bool s_isDark = ReadIsDarkOnce();

    /// <summary>True when Windows is in dark mode (apps theme = dark). False if light or unreadable.</summary>
    public static bool IsDarkMode => s_isDark;

    // Light palette mirrors the existing app look (white surfaces, near-black text).
    private static readonly Color LightBackground   = Color.FromArgb(0xF5, 0xF5, 0xF7);
    private static readonly Color LightSurface      = Color.White;
    private static readonly Color LightSurfaceMuted = Color.FromArgb(0xEC, 0xEC, 0xF0);
    private static readonly Color LightOnSurface    = Color.FromArgb(0x1A, 0x1A, 0x1A);
    private static readonly Color LightOnSurfaceDim = Color.FromArgb(0x66, 0x66, 0x70);
    private static readonly Color LightAccent       = Color.FromArgb(0x58, 0x65, 0xF2); // Discord blurple
    private static readonly Color LightStatusOk     = Color.FromArgb(0x2E, 0x8B, 0x57); // sea green
    private static readonly Color LightStatusWarn   = Color.FromArgb(0xE0, 0x88, 0x1A); // amber
    private static readonly Color LightStatusError  = Color.FromArgb(0xB2, 0x22, 0x22); // firebrick
    private static readonly Color LightDivider      = Color.FromArgb(0xDD, 0xDD, 0xE0);

    // Dark palette: brighter status hues so they read against a dark surface.
    private static readonly Color DarkBackground    = Color.FromArgb(0x1E, 0x1F, 0x22);
    private static readonly Color DarkSurface       = Color.FromArgb(0x2B, 0x2D, 0x31);
    private static readonly Color DarkSurfaceMuted  = Color.FromArgb(0x24, 0x25, 0x29);
    private static readonly Color DarkOnSurface     = Color.FromArgb(0xE4, 0xE6, 0xEB);
    private static readonly Color DarkOnSurfaceDim  = Color.FromArgb(0x9B, 0x9E, 0xA8);
    private static readonly Color DarkAccent        = Color.FromArgb(0x7C, 0x86, 0xF7);
    private static readonly Color DarkStatusOk      = Color.FromArgb(0x55, 0xC4, 0x7A);
    private static readonly Color DarkStatusWarn    = Color.FromArgb(0xF2, 0xB1, 0x4F);
    private static readonly Color DarkStatusError   = Color.FromArgb(0xED, 0x5F, 0x5F);
    private static readonly Color DarkDivider       = Color.FromArgb(0x3A, 0x3C, 0x42);

    public static Color Background   => s_isDark ? DarkBackground   : LightBackground;
    public static Color Surface      => s_isDark ? DarkSurface      : LightSurface;
    public static Color SurfaceMuted => s_isDark ? DarkSurfaceMuted : LightSurfaceMuted;
    public static Color OnSurface    => s_isDark ? DarkOnSurface    : LightOnSurface;
    public static Color OnSurfaceDim => s_isDark ? DarkOnSurfaceDim : LightOnSurfaceDim;
    public static Color Accent       => s_isDark ? DarkAccent       : LightAccent;
    public static Color StatusOk     => s_isDark ? DarkStatusOk     : LightStatusOk;
    public static Color StatusWarn   => s_isDark ? DarkStatusWarn   : LightStatusWarn;
    public static Color StatusError  => s_isDark ? DarkStatusError  : LightStatusError;
    public static Color Divider      => s_isDark ? DarkDivider      : LightDivider;

    /// <summary>
    /// Returns a colour suitable for a status pill background based on a connection phase-like
    /// classification.
    /// </summary>
    public static Color StatusBackgroundForPhase(StatusPhase phase) => phase switch
    {
        StatusPhase.Ok    => StatusOk,
        StatusPhase.Warn  => StatusWarn,
        StatusPhase.Error => StatusError,
        _                 => SurfaceMuted,
    };

    /// <summary>White-on-coloured text reads better on every status pill regardless of theme.</summary>
    public static Color StatusForeground => Color.White;

    private static bool ReadIsDarkOnce()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? raw = key?.GetValue("AppsUseLightTheme");
            if (raw is int i) return i == 0;
        }
        catch
        {
            // Not catastrophic — fall through to light.
        }
        return false;
    }
}

internal enum StatusPhase
{
    Idle,
    Ok,
    Warn,
    Error,
}
