namespace LeadHub.Core.Vpn;

public sealed record CountryInfo(string Code, string NameRu);

/// <summary>Определение страны узла по названию (EN/RU/коды). GeoIP по IP — отдельно, по запросу.</summary>
public static class CountryGuesser
{
    /// <summary>Паттерны в порядке приоритета: (regex по имени узла, страна).</summary>
    private static readonly (Regex Re, CountryInfo Country)[] Patterns =
    {
        (Re(@"герман|germany|german|deutschland|\bde\b|frankfurt|falkenstein|nuremberg|nürnberg|\bfra\b"), new("DE", "Германия")),
        (Re(@"нидерланд|netherlands|holland|\bnl\b|amsterdam|\bams\b"), new("NL", "Нидерланды")),
        (Re(@"финлянд|finland|\bfi\b|helsinki|\bhel\b"), new("FI", "Финляндия")),
        (Re(@"швец|sweden|\bse\b|stockholm"), new("SE", "Швеция")),
        (Re(@"норвег|norway|\bno\b|oslo"), new("NO", "Норвегия")),
        (Re(@"франц|france|\bfr\b|paris|strasbourg|gravelines|\bpar\b"), new("FR", "Франция")),
        (Re(@"великобритан|britain|england|united\s*kingdom|\buk\b|london|\blon\b"), new("GB", "Великобритания")),
        (Re(@"швейцар|switzerland|\bch\b|zurich|zürich"), new("CH", "Швейцария")),
        (Re(@"австри|austria|\bat\b|vienna"), new("AT", "Австрия")),
        (Re(@"польш|poland|\bpl\b|warsaw|varshava"), new("PL", "Польша")),
        (Re(@"чех|czech|\bcz\b|prague|praha"), new("CZ", "Чехия")),
        (Re(@"латв|latvia|\blv\b|riga"), new("LV", "Латвия")),
        (Re(@"эстон|estonia|\bee\b|tallinn"), new("EE", "Эстония")),
        (Re(@"литв|lithuania|\blt\b|vilnius"), new("LT", "Литва")),
        (Re(@"молдов|moldova|\bmd\b|chisinau"), new("MD", "Молдова")),
        (Re(@"россия|русск|russia|\bru\b|moscow|москва|msk|petersburg|питер|spb"), new("RU", "Россия")),
        (Re(@"казахстан|kazakhstan|\bkz\b|almaty|astana"), new("KZ", "Казахстан")),
        (Re(@"армен|armenia|\bam\b|yerevan|ереван"), new("AM", "Армения")),
        (Re(@"грузи|georgia|\bge\b|tbilisi"), new("GE", "Грузия")),
        (Re(@"турц|turkey|turkiye|\btr\b|istanbul|ankara"), new("TR", "Турция")),
        (Re(@"сша|usa|united\s*states|america|\bus\b|new\s*york|\bnyc\b|los\s*angeles|chicago|dallas|miami|seattle|ashburn|virginia"), new("US", "США")),
        (Re(@"канад|canada|\bca\b|toronto|montreal|vancouver|beauharnois"), new("CA", "Канада")),
        (Re(@"гонконг|hong\s*kong|\bhk\b"), new("HK", "Гонконг")),
        (Re(@"япони|japan|\bjp\b|tokyo|osaka"), new("JP", "Япония")),
        (Re(@"сингапур|singapore|\bsg\b"), new("SG", "Сингапур")),
        (Re(@"корея|korea|\bkr\b|seoul"), new("KR", "Южная Корея")),
        (Re(@"китай|china|\bcn\b|shanghai|beijing"), new("CN", "Китай")),
        (Re(@"индия|india|\bin\b|mumbai|bangalore|chennai"), new("IN", "Индия")),
        (Re(@"оаэ|эмират|uae|dubai|abu\s*dhabi"), new("AE", "ОАЭ")),
        (Re(@"израил|israel|\bil\b|tel\s*aviv"), new("IL", "Израиль")),
        (Re(@"испани|spain|\bes\b|madrid|barcelona"), new("ES", "Испания")),
        (Re(@"итали|italy|\bit\b|milan|milano|rome"), new("IT", "Италия")),
        (Re(@"бразил|brazil|\bbr\b|sao\s*paulo|são\s*paulo"), new("BR", "Бразилия")),
        (Re(@"австрали|australia|\bau\b|sydney"), new("AU", "Австралия")),
        (Re(@"филиппин|philippine|\bph\b|manila"), new("PH", "Филиппины")),
        (Re(@"вьетнам|vietnam|\bvn\b|hanoi"), new("VN", "Вьетнам")),
        (Re(@"тайланд|таиланд|thailand|\bth\b|bangkok"), new("TH", "Таиланд")),
        (Re(@"индонез|indonesia|\bid\b|jakarta"), new("ID", "Индонезия")),
        (Re(@"малайз|malaysia|\bmy\b|kuala"), new("MY", "Малайзия")),
        (Re(@"серб|serbia|\brs\b|belgrade"), new("RS", "Сербия")),
        (Re(@"румын|romania|\bro\b|bucharest"), new("RO", "Румыния")),
        (Re(@"болгар|bulgaria|\bbg\b|sofia"), new("BG", "Болгария")),
        (Re(@"украин|ukraine|\bua\b|kyiv|kiev"), new("UA", "Украина")),
        (Re(@"бельг|belgium|\bbe\b|brussels"), new("BE", "Бельгия")),
        (Re(@"ирланд|ireland|\bie\b|dublin"), new("IE", "Ирландия")),
    };

    public static CountryInfo? Guess(string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName)) return null;
        var lower = nodeName.ToLowerInvariant();
        foreach (var (re, country) in Patterns)
            if (re.IsMatch(lower)) return country;
        return null;
    }

    private static Regex Re(string pattern) => new(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

/// <summary>GeoIP-определение страны по IP сервера через ip-api.com (бесплатно, без ключа).</summary>
public sealed class GeoIpClient(HttpClient? http = null)
{
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

    public async Task<CountryInfo?> LookupAsync(string ip)
    {
        try
        {
            var json = await _http.GetStringAsync($"http://ip-api.com/json/{ip}?fields=status,countryCode,country");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var st) && st.GetString() == "success" &&
                root.TryGetProperty("countryCode", out var cc) && cc.GetString() is { Length: 2 } code)
            {
                var name = root.TryGetProperty("country", out var cn) ? cn.GetString() ?? code : code;
                return new CountryInfo(code.ToUpperInvariant(), Translate(code.ToUpperInvariant(), name));
            }
        }
        catch { /* сеть недоступна — не критично */ }
        return null;
    }

    private static string Translate(string code, string fallback) => code switch
    {
        "DE" => "Германия", "NL" => "Нидерланды", "FI" => "Финляндия", "SE" => "Швеция", "NO" => "Норвегия",
        "FR" => "Франция", "GB" => "Великобритания", "CH" => "Швейцария", "AT" => "Австрия", "PL" => "Польша",
        "CZ" => "Чехия", "LV" => "Латвия", "EE" => "Эстония", "LT" => "Литва", "MD" => "Молдова",
        "RU" => "Россия", "KZ" => "Казахстан", "AM" => "Армения", "GE" => "Грузия", "TR" => "Турция",
        "US" => "США", "CA" => "Канада", "HK" => "Гонконг", "JP" => "Япония", "SG" => "Сингапур",
        "KR" => "Южная Корея", "CN" => "Китай", "IN" => "Индия", "AE" => "ОАЭ", "IL" => "Израиль",
        "ES" => "Испания", "IT" => "Италия", "BR" => "Бразилия", "AU" => "Австралия", "PH" => "Филиппины",
        "VN" => "Вьетнам", "TH" => "Таиланд", "ID" => "Индонезия", "MY" => "Малайзия", "RS" => "Сербия",
        "RO" => "Румыния", "BG" => "Болгария", "UA" => "Украина", "BE" => "Бельгия", "IE" => "Ирландия",
        _ => fallback,
    };
}
