using System.IO;
using System.IO.Packaging;
using System.Security;
using System.Windows;
using UProjectHub.Core.Settings;
using AppThemeMode = UProjectHub.Core.Settings.ThemeMode;

namespace UProjectHub.App.Services;

public sealed class ThemeService
{
    private const string DictionarySourceMarker = "ThemeService.Source";
    private static readonly Uri LightThemeSource = CreateSource("Light.xaml");
    private static readonly Uri DarkThemeSource = CreateSource("Dark.xaml");
    private static readonly Uri NormalDensitySource = CreateSource("NormalDensity.xaml");
    private static readonly Uri CompactDensitySource = CreateSource("CompactDensity.xaml");

    private readonly ResourceDictionary _resources;
    private readonly Func<AppThemeMode> _systemThemeResolver;
    private readonly Func<Uri, ResourceDictionary> _dictionaryFactory;

    public ThemeService(
        ResourceDictionary resources,
        Func<AppThemeMode> systemThemeResolver,
        Func<Uri, ResourceDictionary>? dictionaryFactory = null)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _systemThemeResolver = systemThemeResolver
            ?? throw new ArgumentNullException(nameof(systemThemeResolver));
        _dictionaryFactory = dictionaryFactory ?? CreateDictionary;
    }

    public AppThemeMode EffectiveTheme { get; private set; } = AppThemeMode.Light;

    public RowDensity ActiveDensity { get; private set; } = RowDensity.Normal;

    public void ApplyTheme(AppThemeMode themeMode)
    {
        var effectiveTheme = themeMode == AppThemeMode.System
            ? ResolveSystemTheme()
            : themeMode;
        if (effectiveTheme is not AppThemeMode.Light and not AppThemeMode.Dark)
        {
            effectiveTheme = AppThemeMode.Light;
        }

        ReplaceOwnedDictionary(
            IsThemeDictionary,
            effectiveTheme == AppThemeMode.Dark ? DarkThemeSource : LightThemeSource);
        EffectiveTheme = effectiveTheme;
    }

    public void ApplyDensity(RowDensity density)
    {
        ReplaceOwnedDictionary(
            IsDensityDictionary,
            density == RowDensity.Compact ? CompactDensitySource : NormalDensitySource);
        ActiveDensity = density;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ApplyTheme(settings.ThemeMode);
        ApplyDensity(settings.RowDensity);
    }

    private AppThemeMode ResolveSystemTheme()
    {
        try
        {
            return _systemThemeResolver();
        }
        catch (IOException)
        {
            return AppThemeMode.Light;
        }
        catch (UnauthorizedAccessException)
        {
            return AppThemeMode.Light;
        }
        catch (SecurityException)
        {
            return AppThemeMode.Light;
        }
    }

    private void ReplaceOwnedDictionary(
        Func<ResourceDictionary, bool> isOwned,
        Uri source)
    {
        for (var index = _resources.MergedDictionaries.Count - 1; index >= 0; index--)
        {
            if (isOwned(_resources.MergedDictionaries[index]))
            {
                _resources.MergedDictionaries.RemoveAt(index);
            }
        }

        var dictionary = _dictionaryFactory(source)
            ?? throw new InvalidOperationException("The resource dictionary factory returned null.");
        dictionary[DictionarySourceMarker] = source.OriginalString;
        _resources.MergedDictionaries.Add(dictionary);
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        return HasSource(dictionary, LightThemeSource)
            || HasSource(dictionary, DarkThemeSource);
    }

    private static bool IsDensityDictionary(ResourceDictionary dictionary)
    {
        return HasSource(dictionary, NormalDensitySource)
            || HasSource(dictionary, CompactDensitySource);
    }

    private static bool HasSource(ResourceDictionary dictionary, Uri source)
    {
        if (dictionary[DictionarySourceMarker] is string marker
            && string.Equals(marker, source.OriginalString, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            dictionary.Source?.OriginalString,
            source.OriginalString,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ResourceDictionary CreateDictionary(Uri source)
    {
        return new ResourceDictionary { Source = source };
    }

    private static Uri CreateSource(string fileName)
    {
        _ = PackUriHelper.UriSchemePack;
        return new Uri(
            $"pack://application:,,,/UProjectHub.App;component/Themes/{fileName}",
            UriKind.Absolute);
    }
}
