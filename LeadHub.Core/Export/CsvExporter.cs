using System.IO;

namespace LeadHub.Core.Export;

/// <summary>
/// CSV-экспорт лидов — контракт скилла instagram-whatsapp-lead-parser:
/// заголовок Instagram;Бизнес;Город;Ниша;...;Написал;Комментарий, разделитель «;»,
/// BOM + CRLF, защита от формул Excel.
/// </summary>
public static class CsvExporter
{
    public static readonly string[] Header =
    {
        "Instagram", "Бизнес", "Город", "Ниша", "Телефон", "Telegram", "WhatsApp",
        "Статус WhatsApp", "WhatsApp-ссылка найдена", "Telegram проверен",
        "Причина", "Текст", "Написал", "Комментарий",
    };

    public static string Export(IReadOnlyList<Parser.LeadRecord> leads, string outputPath, string? sectionFilter = null)
    {
        var rows = leads;
        if (sectionFilter != null)
            rows = leads.Where(l => HtmlHelperExporter.SectionKey(l.Section) == sectionFilter).ToList();

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", Header.Select(Cell)));
        foreach (var lead in rows)
        {
            var phone = lead.Phone;
            var waMatch = Regex.Match(lead.WhatsappUrl ?? "", @"wa\.me/(\d{8,15})");
            if (waMatch.Success) phone = waMatch.Groups[1].Value;

            sb.AppendLine(string.Join(";", new[]
            {
                Cell(lead.Handle),
                Cell(string.IsNullOrWhiteSpace(lead.FullName) ? lead.Handle : lead.FullName),
                Cell(lead.City),
                Cell(lead.Niche),
                Cell(phone.Length > 0 ? "+" + phone : ""),
                Cell(lead.TelegramUrl),
                Cell(lead.WhatsappUrl),
                Cell(WaStatusLabel(lead.WhatsappStatus)),
                Cell(lead.WhatsappStatus is "published" or "published_profile_phone" ? "да" : ""),
                Cell(lead.TelegramStatus == "published" ? "да" : ""),
                Cell(lead.NotSentReason),
                Cell(lead.OutreachText.Length > 0 ? lead.OutreachText : Parser.MessageGenerator.Generate(lead)),
                Cell(lead.Sent ? "Да" : ""),
                Cell(lead.Comment),
            }));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, "\ufeff" + sb.ToString().Replace("\n", "\r\n"), new System.Text.UTF8Encoding(false));
        return outputPath;
    }

    private static string WaStatusLabel(string status) => status switch
    {
        "published" => "ссылка опубликована бизнесом",
        "published_profile_phone" => "номер из профиля",
        "missing_public_link" => "ссылка в профиле не найдена",
        _ => "не проверен",
    };

    private static string Cell(string? value)
    {
        var text = value ?? "";
        if (Regex.IsMatch(text, @"^[\s]*[=+@-]")) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
