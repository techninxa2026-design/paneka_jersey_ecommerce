using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Services;

/// <summary>
/// All 64 districts, grouped by the eight administrative divisions shown by
/// the Bangladesh National Portal (bangladesh.gov.bd/views/district-list).
/// Use one canonical English spelling in checkout and stored order addresses.
/// </summary>
public static class BangladeshDistricts
{
    public sealed record Division(string Name, IReadOnlyList<string> Districts);

    public static IReadOnlyList<Division> ByDivision { get; } =
    [
        new("Dhaka", [
            "Dhaka", "Faridpur", "Gazipur", "Gopalganj", "Kishoreganj",
            "Madaripur", "Manikganj", "Munshiganj", "Narayanganj",
            "Narsingdi", "Rajbari", "Shariatpur", "Tangail"
        ]),
        new("Khulna", [
            "Bagerhat", "Chuadanga", "Jashore", "Jhenaidah", "Khulna",
            "Kushtia", "Magura", "Meherpur", "Narail", "Satkhira"
        ]),
        new("Chattogram", [
            "Bandarban", "Brahmanbaria", "Chandpur", "Chattogram",
            "Cumilla", "Cox's Bazar", "Feni", "Khagrachhari",
            "Lakshmipur", "Noakhali", "Rangamati"
        ]),
        new("Rajshahi", [
            "Bogura", "Joypurhat", "Naogaon", "Natore",
            "Chapainawabganj", "Pabna", "Rajshahi", "Sirajganj"
        ]),
        new("Sylhet", ["Habiganj", "Moulvibazar", "Sunamganj", "Sylhet"]),
        new("Rangpur", [
            "Dinajpur", "Gaibandha", "Kurigram", "Lalmonirhat",
            "Nilphamari", "Panchagarh", "Rangpur", "Thakurgaon"
        ]),
        new("Mymensingh", ["Jamalpur", "Mymensingh", "Netrokona", "Sherpur"]),
        new("Barishal", [
            "Barguna", "Barishal", "Bhola", "Jhalokati",
            "Patuakhali", "Pirojpur"
        ])
    ];

    // Previously-entered English variants remain valid and are normalized.
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chittagong"] = "Chattogram",
            ["Comilla"] = "Cumilla",
            ["Jessore"] = "Jashore",
            ["Barisal"] = "Barishal",
            ["Bogra"] = "Bogura",
            ["Coxs Bazar"] = "Cox's Bazar",
            ["Cox's bazar"] = "Cox's Bazar",
            ["Chapai Nawabganj"] = "Chapainawabganj",
            ["Jhalkathi"] = "Jhalokati",
            ["Khagrachari"] = "Khagrachhari",
            ["Lakshmipur"] = "Lakshmipur",
            ["Moulvi Bazar"] = "Moulvibazar"
        };

    public static string? Normalize(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (Aliases.TryGetValue(text, out var replacement)) text = replacement;
        foreach (var division in ByDivision)
            foreach (var district in division.Districts)
                if (string.Equals(district, text, StringComparison.OrdinalIgnoreCase))
                    return district;
        return null;
    }

    public static bool IsValid(string? district) => Normalize(district) is not null;
    public static bool IsDhaka(string? district) => Normalize(district) == "Dhaka";
}

/// <summary>Validates the selected district on server, including forged POSTs.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class BangladeshDistrictAttribute : ValidationAttribute
{
    public BangladeshDistrictAttribute()
    {
        ErrorMessage = "Choose a valid Bangladesh district from the list.";
    }

    public override bool IsValid(object? value) =>
        value is string text && BangladeshDistricts.IsValid(text);
}
