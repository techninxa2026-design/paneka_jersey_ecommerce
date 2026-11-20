using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jersey_ecommerce.Services;

/// <summary>
/// Creates predictable, human-readable URL slugs only from server-validated names.
/// Browser-posted slug fields are never used as an authority.
/// Existing slugs should be retained when an entity's name has not changed.
/// </summary>
public static class CatalogSlug
{
    private static readonly Regex Separator = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string FromName(string? name, string fallback = "item", int maxLength = 120)
    {
        if (maxLength is < 8 or > 220)
            throw new ArgumentOutOfRangeException(nameof(maxLength));

        var source = (name ?? "").Normalize(NormalizationForm.FormD);
        var letters = new string(source.Where(c =>
            CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());

        var seed = Separator.Replace(letters.ToLowerInvariant(), "-").Trim('-');
        if (seed.Length == 0)
            seed = Separator.Replace(fallback.ToLowerInvariant(), "-").Trim('-');
        if (seed.Length == 0) seed = "item";
        return seed.Length > maxLength ? seed[..maxLength].TrimEnd('-') : seed;
    }

    public static async Task<string> UniqueAsync(
        string? name,
        string fallback,
        int maxLength,
        Func<string, Task<bool>> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        var seed = FromName(name, fallback, maxLength);
        if (!await exists(seed)) return seed;
        for (var i = 2; i < int.MaxValue; i++)
        {
            var suffix = "-" + i.ToString(CultureInfo.InvariantCulture);
            var maxSeedLength = maxLength - suffix.Length;
            if (maxSeedLength <= 0) throw new InvalidOperationException("Slug suffix exhausted.");
            var candidate = seed[..Math.Min(seed.Length, maxSeedLength)].TrimEnd('-') + suffix;
            if (!await exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("Unable to allocate a unique URL slug.");
    }
}
