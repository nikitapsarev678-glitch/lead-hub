namespace LeadHub.Core.Parser;

/// <summary>
/// Генератор персонального текста первого сообщения. Порт default_message из build_lead_site.py:
/// детерминированный выбор варианта по сумме кодов символов handle.
/// </summary>
public static class MessageGenerator
{
    private static readonly string[] Greetings = { "Здравствуйте!", "Добрый день!", "Приветствую вас!)", "Здравствуйте!)", "Добрый день!)" };
    private static readonly string[] Intros =
    {
        "Нашёл через Instagram вашу страничку «{handle}»",
        "Наткнулся в Instagram на ваш профиль «{handle}»",
        "Нашёл вас в Instagram — «{handle}»",
        "Ваш профиль «{handle}» попался мне в Instagram",
        "Искал в Instagram и наткнулся на вашу страничку «{handle}»",
    };
    private static readonly string[] Contexts = { " по запросу «{niche}»", " — {niche}", " ({niche})" };
    private static readonly string[] CityForms = { " ({city})", ", {city}" };
    private static readonly string[] Questions =
    {
        "Подскажите, пожалуйста, у вас есть отдельный сайт?",
        "Пытался найти ваш сайт в интернете, но так и не нашёл) Подскажите, он у вас есть или всё только в Instagram?",
        "Скажите, пожалуйста, отдельный сайт у вас есть или ведёте всё через Instagram?",
        "Подскажите, у вас есть свой сайт, или я просто плохо искал?)",
        "Единственный ваш сайт — этот Instagram, или есть отдельный?)",
        "Хотел посмотреть подробнее, но кроме Instagram ничего не нашёл. Сайт у вас есть?)",
    };
    private static readonly string[] Smiles = { "", "", ")", ")", ")" };

    public static string Generate(string handle, string niche, string city)
    {
        var seed = handle.Sum(c => c);
        niche = (niche ?? "").Trim().TrimEnd('.');
        city = (city ?? "").Trim();

        var greeting = Greetings[Mod(seed, Greetings.Length)];
        var intro = Intros[Mod(seed / 7, Intros.Length)].Replace("{handle}", handle);
        if (niche.Length > 0 && !niche.Contains(city, StringComparison.OrdinalIgnoreCase) && (city.Length == 0 || !niche.Contains(city, StringComparison.OrdinalIgnoreCase)))
            intro += Contexts[Mod(seed / 11, Contexts.Length)].Replace("{niche}", niche);
        if (city.Length > 0 && !niche.Contains(city, StringComparison.OrdinalIgnoreCase))
            intro += CityForms[Mod(seed / 13, CityForms.Length)].Replace("{city}", city);
        var question = Questions[Mod(seed / 17, Questions.Length)];
        var smile = question.EndsWith(')') ? "" : Smiles[Mod(seed / 19, Smiles.Length)];
        return $"{greeting} {intro}. {question}{smile}";
    }

    public static string Generate(LeadRecord lead) => Generate(lead.Handle, lead.Niche, lead.City);

    private static int Mod(int value, int m) => ((value % m) + m) % m;
}
