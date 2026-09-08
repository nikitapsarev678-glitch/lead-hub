using System.IO;
using System.Reflection;

namespace LeadHub.Core.Export;

/// <summary>
/// Экспорт лидов в автономный HTML click-copy хелпер — тот же интерфейс, что instagram-leads.html
/// скилла (вкладки, поиск, копирование текста, «Написал», выгрузка CSV/отметок, localStorage).
/// </summary>
public static class HtmlHelperExporter
{
    public static readonly (string Key, string Label)[] Sections =
    {
        ("telegram_resolved", "Telegram"),
        ("whatsapp_reserve", "WhatsApp — Telegram не найден"),
        ("needs_verification", "Нужна проверка"),
    };

    public static readonly string[] NotSentReasons =
    {
        "", "Не нужна ниши", "Одиночный мастер", "Слабый или маленький бизнес", "Сайт всё-таки есть",
        "Неактуальный профиль", "Уже писал раньше", "WhatsApp: номер не найден", "Telegram: не найден / приватность",
        "Telegram: нужен Premium / Stars", "Другой бизнес", "Контакт не подходит", "Другое",
    };

    private static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["published"] = "ссылка опубликована бизнесом",
        ["missing_public_link"] = "ссылка в профиле не найдена",
        ["published_profile_phone"] = "номер из профиля",
        ["registered"] = "подтверждён",
        ["resolved"] = "подтверждён",
        ["unchecked"] = "не проверен",
    };

    public static string Export(IReadOnlyList<Parser.LeadRecord> leads, string title, string outputPath)
    {
        var counts = Sections.ToDictionary(s => s.Key, s => leads.Count(l => SectionKey(l.Section) == s.Key));
        var css = ReadAsset("lead-table.css");
        var js = ReadAsset("lead-table.js");

        var tabs = string.Join("", Sections.Select(s =>
            $"""<button id="tab-{s.Key}" type="button" role="tab" aria-controls="table-panel" aria-selected="{(s.Key == "telegram_resolved").ToString().ToLowerInvariant()}" data-section="{s.Key}">{Esc(s.Label)} <span class="count">{counts[s.Key]}</span></button>"""));

        var index = 0;
        var rows = string.Join("\n", leads.Select(l => RenderRow(l, ++index)));

        var doc = new StringBuilder();
        doc.Append("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>").Append(Esc(title)).Append("</title><style>").Append(css).Append("</style></head>\n");
        doc.Append("<body><header><h1>").Append(Esc(title)).Append("</h1>\n");
        doc.Append("<p class=\"subtitle\">").Append(counts["telegram_resolved"]).Append(" готовых Telegram · ").Append(counts["whatsapp_reserve"]).Append(" в WhatsApp · ").Append(counts["needs_verification"]).Append(" требуют проверки</p>\n");
        doc.Append("<div class=\"controls\"><label class=\"search-label\"><span class=\"visually-hidden\">Поиск по бизнесу, городу или нише</span><input id=\"search\" type=\"search\" placeholder=\"Бизнес, город, ниша или телефон\"></label>\n");
        doc.Append("<label><input type=\"checkbox\" id=\"hide-done\"> Скрыть тех, кому написал</label><button id=\"export-csv\" type=\"button\">Скачать вкладку CSV</button>\n");
        doc.Append("<button id=\"export-feedback\" type=\"button\">Скачать отметки</button><label class=\"import\">Загрузить отметки<input id=\"import-feedback\" class=\"visually-hidden\" type=\"file\" accept=\".json,application/json\"></label></div>\n");
        doc.Append("<div id=\"notice\" role=\"status\" aria-live=\"polite\"></div></header><main><div class=\"tabs\" role=\"tablist\" aria-label=\"Таблицы лидов\">").Append(tabs).Append("</div>\n");
        doc.Append("<div class=\"summary-line\"><span>«Написал» отмечается вручную после отправки.</span><span id=\"visible-count\"></span></div>\n");
        doc.Append("<div id=\"table-panel\" role=\"tabpanel\" aria-labelledby=\"tab-telegram_resolved\"><div class=\"table-wrap\"><table><thead><tr><th>№</th><th>Бизнес и проверка</th><th>Контакты</th><th>Текст сообщения</th><th>Отметки</th></tr></thead><tbody>").Append(rows).Append("</tbody></table><div id=\"empty\" class=\"empty\" hidden></div></div></div></main>\n");
        doc.Append("<footer>Сгенерировано LeadHub. WhatsApp — ссылка, опубликованная бизнесом; Telegram — из профиля. «Написал» отмечается вручную после отправки.</footer>\n");
        doc.Append("<script>").Append(js).Append("</script></body></html>");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, doc.ToString(), new System.Text.UTF8Encoding(false));
        return outputPath;
    }

    public static string SectionKey(Parser.LeadSection section) => section switch
    {
        Parser.LeadSection.TelegramResolved => "telegram_resolved",
        Parser.LeadSection.WhatsappReserve => "whatsapp_reserve",
        _ => "needs_verification",
    };

    private static string RenderRow(Parser.LeadRecord lead, int index)
    {
        var handle = lead.Handle.ToLowerInvariant();
        var leadId = "instagram:" + handle;
        var section = SectionKey(lead.Section);

        // Номер у кнопки и текстом — одно и то же число (правило скилла)
        var waNumber = Regex.Match(lead.WhatsappUrl ?? "", @"wa\.me/(\d{8,15})").Groups[1].Value;
        var phone = lead.Phone ?? "";
        var wa = lead.WhatsappUrl ?? "";
        if (waNumber.Length > 0) phone = waNumber;
        else if (wa.Length == 0 && phone.Length > 0) wa = $"https://wa.me/{phone}";
        var tg = lead.TelegramUrl ?? "";

        var links = Anchor(lead.InstagramUrl, "Открыть Instagram", "instagram");
        if (tg.Length > 0) links += Anchor(tg, tg.Contains("/+") ? "Открыть Telegram по номеру" : "Открыть Telegram", "telegram");
        if (wa.Length > 0) links += Anchor(wa, "Открыть WhatsApp", "whatsapp");

        var message = string.IsNullOrEmpty(lead.OutreachText) ? Parser.MessageGenerator.Generate(lead) : lead.OutreachText;
        var status = lead.Section switch
        {
            Parser.LeadSection.TelegramResolved => "Telegram",
            Parser.LeadSection.WhatsappReserve => "WhatsApp — ссылка бизнеса",
            _ => "Нужна проверка",
        };
        var notes = new List<string>
        {
            "WhatsApp: " + (StatusLabels.GetValueOrDefault(lead.WhatsappStatus, lead.WhatsappStatus)),
            "Telegram: " + (StatusLabels.GetValueOrDefault(lead.TelegramStatus, lead.TelegramStatus)),
        };
        if (lead.Section == Parser.LeadSection.WhatsappReserve)
            notes.Add("Прямой Telegram по опубликованным данным не найден. Аккаунт может существовать.");

        var business = string.IsNullOrWhiteSpace(lead.FullName) ? handle : lead.FullName;
        var attrs = new Dictionary<string, string>
        {
            ["lead-id"] = leadId, ["handle"] = handle, ["section"] = section, ["business"] = business,
            ["city"] = lead.City ?? "", ["niche"] = lead.Niche ?? "", ["phone"] = phone,
            ["telegram"] = tg, ["whatsapp"] = wa,
            ["wa-status"] = StatusLabels.GetValueOrDefault(lead.WhatsappStatus, lead.WhatsappStatus),
            ["wa-checked"] = "", ["wa-published"] = lead.WhatsappStatus == "published" ? "profile_link" : "",
            ["tg-checked"] = lead.TelegramStatus == "published" ? "profile_link" : "",
            ["search"] = $"{handle} {business} {lead.City} {lead.Niche} {phone}".ToLowerInvariant(),
        };
        var attr = string.Join(" ", attrs.Select(kv => $"data-{kv.Key}=\"{Esc(kv.Value)}\""));
        var options = string.Join("", NotSentReasons.Select(r => $"<option value=\"{Esc(r)}\">{Esc(r.Length == 0 ? "Не указано" : r)}</option>"));
        var messageText = message;
        var doneChecked = lead.Sent ? " checked" : "";

        return $$"""
      <tr {{attr}}>
      <td class="number">{{index}}</td><td class="business-cell"><div class="business">{{Esc(business)}}</div>
      <div class="meta">@{{Esc(handle)}} · {{Esc(lead.City)}} · {{Esc(lead.Niche)}}</div>
      <span class="status {{(lead.Section == Parser.LeadSection.NeedsVerification ? "pending" : "")}}">{{status}}</span>
      <div class="check-note">{{string.Join("<br>", notes.Select(Esc))}}</div></td>
      <td class="links">{{links}}<div class="phone">{{(phone.Length > 0 ? $"<a href=\"https://wa.me/{phone}\" target=\"_blank\" rel=\"noopener noreferrer\">+{phone}</a>" : "Номер не раскрыт")}}</div></td>
      <td class="message-cell"><label class="visually-hidden" for="message-{{index}}">Текст для @{{Esc(handle)}}</label><textarea class="message" id="message-{{index}}">{{Esc(messageText)}}</textarea></td>
      <td class="actions"><button type="button" class="copy">Скопировать текст</button><label class="done-label"><input type="checkbox" class="done"{{doneChecked}}> Написал</label>
      <label class="feedback-label" for="reason-{{index}}">Почему не написал</label><select class="reason" id="reason-{{index}}">{{options}}</select>
      <label class="visually-hidden" for="note-{{index}}">Комментарий для @{{Esc(handle)}}</label><textarea class="reason-note" id="note-{{index}}" placeholder="Комментарий для следующего поиска">{{Esc(lead.Comment)}}</textarea></td></tr>
""";
    }

    private static string Anchor(string url, string label, string css)
    {
        if (string.IsNullOrEmpty(url)) return "";
        return $"""<a class="button {css}" href="{Esc(url)}" target="_blank" rel="noopener noreferrer">{Esc(label)} ↗</a>""";
    }

    private static string Esc(string? value) =>
        value?.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;") ?? "";

    private static string ReadAsset(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"LeadHub.Core.Export.Assets.{name}")
            ?? throw new InvalidOperationException($"Встроенный ресурс не найден: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
