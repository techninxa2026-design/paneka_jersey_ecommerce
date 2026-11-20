namespace Jersey_ecommerce.Services.Checkout;

/// <summary>
/// Bangladesh's 64 districts grouped by their 8 administrative divisions.
/// Source: Bangladesh National Portal, bangladesh.gov.bd/views/district-list.
/// Use one server-side catalog for the checkout selector and order validation.
/// </summary>
public static class BangladeshDistricts
{
    public static IReadOnlyDictionary<string, string[]> ByDivision { get; } =
        new Dictionary<string, string[]>
        {
            ["Dhaka"] =
            [
                "Dhaka", "Faridpur", "Gazipur", "Gopalganj", "Kishoreganj",
                "Madaripur", "Manikganj", "Munshiganj", "Narayanganj",
                "Narsingdi", "Rajbari", "Shariatpur", "Tangail"
            ],
            ["Chattogram"] =
            [
                "Bandarban", "Brahmanbaria", "Chandpur", "Chattogram",
                "Cumilla", "Cox's Bazar", "Feni", "Khagrachhari",
                "Lakshmipur", "Noakhali", "Rangamati"
            ],
            ["Rajshahi"] =
            [
                "Bogura", "Chapainawabganj", "Joypurhat", "Naogaon",
                "Natore", "Pabna", "Rajshahi", "Sirajganj"
            ],
            ["Khulna"] =
            [
                "Bagerhat", "Chuadanga", "Jashore", "Jhenaidah", "Khulna",
                "Kushtia", "Magura", "Meherpur", "Narail", "Satkhira"
            ],
            ["Barishal"] =
            [
                "Barguna", "Barishal", "Bhola", "Jhalokathi",
                "Patuakhali", "Pirojpur"
            ],
            ["Sylhet"] =
            [
                "Habiganj", "Moulvibazar", "Sunamganj", "Sylhet"
            ],
            ["Rangpur"] =
            [
                "Dinajpur", "Gaibandha", "Kurigram", "Lalmonirhat",
                "Nilphamari", "Panchagarh", "Rangpur", "Thakurgaon"
            ],
            ["Mymensingh"] =
            [
                "Jamalpur", "Mymensingh", "Netrokona", "Sherpur"
            ]
        };

    private static readonly IReadOnlyDictionary<string, string> Names =
        ByDivision.Values.SelectMany(x => x).ToDictionary(x => x, x => x,
            StringComparer.OrdinalIgnoreCase);

    // Normalize spellings historically used by address books and delivery
    // couriers, but do not allow arbitrary out-of-country place names.
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chittagong"] = "Chattogram",
            ["Comilla"] = "Cumilla",
            ["Jessore"] = "Jashore",
            ["Bogra"] = "Bogura",
            ["Barisal"] = "Barishal",
            ["Jhalokati"] = "Jhalokathi",
            ["Khagrachari"] = "Khagrachhari",
            ["Maulvibazar"] = "Moulvibazar",
            ["Netrakona"] = "Netrokona",
            ["Nawabganj"] = "Chapainawabganj"
        };

    public static int Count => Names.Count;

    public static bool TryNormalize(string? district, out string canonical)
    {
        var candidate = district?.Trim();
        if (candidate is not null && Names.TryGetValue(candidate, out var found))
        {
            canonical = found;
            return true;
        }
        if (candidate is not null && Aliases.TryGetValue(candidate, out var alias))
        {
            canonical = alias;
            return true;
        }
        canonical = "";
        return false;
    }

    public static string RegionFor(string canonicalDistrict) =>
        string.Equals(canonicalDistrict, "Dhaka", StringComparison.Ordinal)
            ? "Dhaka" : "OutsideDhaka";
}
