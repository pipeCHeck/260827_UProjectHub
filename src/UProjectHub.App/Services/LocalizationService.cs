using System.IO.Packaging;
using System.Windows;
using UProjectHub.Core.Settings;

namespace UProjectHub.App.Services;

public sealed class LocalizationService
{
    private const string DictionarySourceMarker = "LocalizationService.Source";
    private static readonly Uri EnglishSource = CreateSource("Strings.en.xaml");
    private static readonly Uri KoreanSource = CreateSource("Strings.ko.xaml");

    private readonly ResourceDictionary _resources;
    private readonly Func<Uri, ResourceDictionary> _dictionaryFactory;

    public LocalizationService(
        ResourceDictionary resources,
        Func<Uri, ResourceDictionary>? dictionaryFactory = null)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _dictionaryFactory = dictionaryFactory ?? CreateDictionary;
    }

    public event EventHandler? LanguageChanged;

    public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.English;

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ApplyLanguage(settings.Language);
    }

    public void ApplyLanguage(AppLanguage language)
    {
        var effective = Enum.IsDefined(language) ? language : AppLanguage.English;
        var source = effective == AppLanguage.Korean ? KoreanSource : EnglishSource;

        for (var index = _resources.MergedDictionaries.Count - 1; index >= 0; index--)
        {
            if (IsLocalizationDictionary(_resources.MergedDictionaries[index]))
            {
                _resources.MergedDictionaries.RemoveAt(index);
            }
        }

        var dictionary = _dictionaryFactory(source)
            ?? throw new InvalidOperationException("The localization dictionary factory returned null.");
        dictionary[DictionarySourceMarker] = source.OriginalString;
        _resources.MergedDictionaries.Add(dictionary);

        var changed = CurrentLanguage != effective;
        CurrentLanguage = effective;
        if (changed)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _resources[key] as string ?? key;
    }

    private static bool IsLocalizationDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary[DictionarySourceMarker] as string
            ?? dictionary.Source?.OriginalString;
        return source is not null
            && (source.EndsWith("Localization/Strings.en.xaml", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith("Localization/Strings.ko.xaml", StringComparison.OrdinalIgnoreCase));
    }

    private static ResourceDictionary CreateDictionary(Uri source) =>
        new() { Source = source };

    private static Uri CreateSource(string fileName)
    {
        _ = PackUriHelper.UriSchemePack;
        return new Uri(
            $"pack://application:,,,/UProjectHub.App;component/Localization/{fileName}",
            UriKind.Absolute);
    }
}
