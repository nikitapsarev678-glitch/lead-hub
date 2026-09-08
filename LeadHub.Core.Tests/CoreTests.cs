using System.IO;
using LeadHub.Core.Export;
using LeadHub.Core.Parser;
using LeadHub.Core.Rotation;
using LeadHub.Core.Store;
using LeadHub.Core.Vpn;
using Xunit;

namespace LeadHub.Core.Tests;

public class VlessLinkTests
{
    private const string RealityLink =
        "vless://8f2a1b3c-4d5e-6f70-8a9b-0c1d2e3f4a5b@5.181.10.20:8443?encryption=none&flow=xtls-rprx-vision&security=reality&sni=app.example.win&fp=chrome&pbk=AbCdEfPublicKey123&type=tcp#%F0%9F%87%A9%F0%9F%87%AA%20Germany%201";

    [Fact]
    public void Parse_RealityLink_MapsAllFields()
    {
        var node = VlessLink.Parse(RealityLink);
        Assert.Equal("5.181.10.20", node.Server);
        Assert.Equal(8443, node.Port);
        Assert.Equal("8f2a1b3c-4d5e-6f70-8a9b-0c1d2e3f4a5b", node.Uuid);
        Assert.Equal("xtls-rprx-vision", node.Flow);
        Assert.Equal("reality", node.Security);
        Assert.Equal("app.example.win", node.Sni);
        Assert.Equal("AbCdEfPublicKey123", node.PublicKey);
        Assert.Equal("tcp", node.Transport);
        Assert.Contains("Germany 1", node.Name);
    }

    [Fact]
    public void Parse_WsTransport_MapsPathAndHost()
    {
        var link = "vless://11111111-2222-3333-4444-555555555555@sub.example.win:443?encryption=none&security=tls&sni=sub.example.win&type=ws&path=%2Ftransport%2Fab12&host=sub.example.win#WS%20%D0%A3%D0%B7%D0%B5%D0%BB";
        var node = VlessLink.Parse(link);
        Assert.Equal("ws", node.Transport);
        Assert.Equal("/transport/ab12", node.Path);
        Assert.Equal("sub.example.win", node.HostHeader);
        Assert.Equal("WS Узел", node.Name);
    }

    [Fact]
    public void ToLink_Roundtrip_KeepsEssentials()
    {
        var node = VlessLink.Parse(RealityLink);
        var link = VlessLink.ToLink(node);
        var reparsed = VlessLink.Parse(link);
        Assert.Equal(node.Server, reparsed.Server);
        Assert.Equal(node.Port, reparsed.Port);
        Assert.Equal(node.Uuid, reparsed.Uuid);
        Assert.Equal(node.Flow, reparsed.Flow);
        Assert.Equal(node.Security, reparsed.Security);
        Assert.Equal(node.PublicKey, reparsed.PublicKey);
    }

    [Fact]
    public void ExtractLinks_FindsSeveral_InMixedText()
    {
        var text = $"какой-то текст\n{RealityLink}\nи ещё vless://99999999-1111-2222-3333-444444444444@1.2.3.4:443?security=tls#XZ";
        var links = VlessLink.ExtractLinks(text);
        Assert.Equal(2, links.Count);
    }

    [Fact]
    public void Parse_BadUuid_ThrowsFormat()
    {
        Assert.Throws<FormatException>(() => VlessLink.Parse("vless://not-a-uuid@1.2.3.4:443?security=tls#x"));
    }
}

public class SubscriptionDecoderTests
{
    [Fact]
    public void Decode_PlainLinksList_Dedups()
    {
        var l1 = "vless://11111111-2222-3333-4444-555555555555@1.2.3.4:443?security=tls#A";
        var result = SubscriptionDecoder.Decode($"{l1}\n{l1}\nvless://99999999-1111-2222-3333-444444444444@5.6.7.8:443?security=tls#B");
        Assert.Equal(2, result.Nodes.Count);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Decode_Base64Blob_Decodes()
    {
        var l1 = "vless://11111111-2222-3333-4444-555555555555@1.2.3.4:443?security=tls#A";
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(l1 + "\n"));
        var result = SubscriptionDecoder.Decode(b64);
        Assert.Single(result.Nodes);
        Assert.Equal("A", result.Nodes[0].Name);
    }

    [Fact]
    public void Decode_Garbage_ReportsError()
    {
        var result = SubscriptionDecoder.Decode("просто текст без ссылок");
        Assert.Empty(result.Nodes);
        Assert.NotEmpty(result.Errors);
    }
}

public class CountryGuesserTests
{
    [Theory]
    [InlineData("🇩🇪 Germany 1 VLESS TCP", "DE")]
    [InlineData("PHILIPPINES 1 VLESS TCP", "PH")]
    [InlineData("Нидерланды · Amsterdam-2", "NL")]
    [InlineData("Москва - MSK-Premium", "RU")]
    [InlineData("some-unknown-node", null)]
    public void Guess_MatchesExpectedCountry(string name, string? expected)
    {
        var info = CountryGuesser.Guess(name);
        Assert.Equal(expected, info?.Code);
    }
}

public class SingBoxConfigTests
{
    [Fact]
    public void Generate_ContainsSlotsAndNodes()
    {
        var nodes = new List<VpnNode>
        {
            new() { Id = 1, Server = "1.2.3.4", Port = 443, Uuid = "11111111-2222-3333-4444-555555555555", Security = "reality", Flow = "xtls-rprx-vision", PublicKey = "pbk123", Transport = "tcp" },
            new() { Id = 2, Server = "5.6.7.8", Port = 8443, Uuid = "99999999-1111-2222-3333-444444444444", Security = "tls", Transport = "ws", Path = "/ws", HostHeader = "cdn.example.com" },
        };
        var slots = new List<VpnSlot> { new() { Index = 1, Port = 10801 }, new() { Index = 2, Port = 10802, AssignedNodeTag = "node-2" } };
        var json = SingBoxConfigGen.Generate(nodes, slots);

        Assert.Contains("\"in-slot-1\"", json);
        Assert.Contains("\"port\": 10801", json);
        Assert.Contains("\"node-1\"", json);
        Assert.Contains("\"reality\"", json);
        Assert.Contains("\"type\": \"ws\"", json);
        Assert.Contains("127.0.0.1:9090", json);
        Assert.Contains("\"outbound\": \"slot-2\"", json);
        Assert.Contains("\"default\": \"node-2\"", json);
    }
}

public class QualificationTests
{
    private static readonly long Now = DateTimeOffset.Parse("2026-09-08T12:00:00Z").ToUnixTimeSeconds();
    private static readonly long FreshPostTs = DateTimeOffset.Parse("2026-09-01T12:00:00Z").ToUnixTimeSeconds();

    private static Candidate Candidate(string city = "Казань") => new()
    { Handle = "kuhni_kazan", Niche = "кухни на заказ под ключ", NicheGroup = "Производство", SourceCity = city };

    private static IgProfile Profile(Action<IgProfile>? tune = null)
    {
        var p = new IgProfile
        {
            Username = "kuhni_kazan",
            FullName = "Кухни Казань | Производство",
            Biography = "Кухни на заказ под ключ. Казань. WhatsApp по ссылке",
            FollowerCount = 3000,
            MediaCount = 120,
            BioLinks = { "https://wa.me/79001234567" },
            LatestPosts = { new IgPostItem { Code = "ABC123", TakenAt = FreshPostTs, CaptionText = "Новые кухни из массива" } },
        };
        tune?.Invoke(p);
        return p;
    }

    [Fact]
    public void Qualify_CleanBusiness_WithWhatsApp_Passes()
    {
        var q = new Qualifier(new ParseOptions());
        var result = q.Qualify(Candidate(), Profile(), Now);
        Assert.True(result.QualifiedPreSite);
        Assert.Equal("Казань", result.Location.City);
        Assert.StartsWith("https://wa.me/79001234567", result.Messenger.Whatsapp);
    }

    [Fact]
    public void Qualify_KazakhstanMarker_Rejects()
    {
        var q = new Qualifier(new ParseOptions());
        var profile = Profile(p => p.Biography = "Кухни на заказ. Алматы, Казахстан");
        var result = q.Qualify(Candidate(), profile, Now);
        Assert.Contains("kazakhstan_marker", result.Reasons);
        Assert.False(result.QualifiedPreSite);
    }

    [Fact]
    public void Qualify_SiteInBio_Rejects()
    {
        var q = new Qualifier(new ParseOptions());
        var profile = Profile(p => p.BioLinks.Add("https://kuhni-kazan.ru"));
        var result = q.Qualify(Candidate(), profile, Now);
        Assert.Contains("site_or_landing_in_profile", result.Reasons);
    }

    [Fact]
    public void Qualify_TooFewPostsAndSmallAudience_Rejects()
    {
        var q = new Qualifier(new ParseOptions());
        var profile = Profile(p => { p.MediaCount = 3; p.FollowerCount = 10; });
        var result = q.Qualify(Candidate(), profile, Now);
        Assert.Contains("too_few_posts", result.Reasons);
        Assert.Contains("very_small_audience", result.Reasons);
    }

    [Fact]
    public void Qualify_StaleProfile_NoStory_Rejects()
    {
        var q = new Qualifier(new ParseOptions());
        var staleTs = DateTimeOffset.Parse("2026-01-01T00:00:00Z").ToUnixTimeSeconds();
        var profile = Profile(p => p.LatestPosts = [new() { TakenAt = staleTs }]);
        var result = q.Qualify(Candidate(), profile, Now);
        Assert.Contains("no_recent_post_45d_or_active_story", result.Reasons);
    }

    [Fact]
    public void Qualify_ActiveStory_SavesStaleProfile()
    {
        var q = new Qualifier(new ParseOptions());
        var staleTs = DateTimeOffset.Parse("2026-01-01T00:00:00Z").ToUnixTimeSeconds();
        var storyTs = Now - 3600;
        var profile = Profile(p =>
        {
            p.LatestPosts = [new() { TakenAt = staleTs }];
            p.LatestReelMedia = storyTs;
        });
        var result = q.Qualify(Candidate(), profile, Now);
        Assert.True(result.ActiveStory);
        Assert.DoesNotContain("no_recent_post_45d_or_active_story", result.Reasons);
    }

    [Fact]
    public void Qualify_PersonalTrainer_Rejects()
    {
        var q = new Qualifier(new ParseOptions());
        var profile = Profile(p => { p.FullName = "Фитнес-тренер Маша"; p.Biography = "Личный блог о тренировках"; });
        var result = q.Qualify(Candidate(), profile, Now);
        Assert.Contains("personal_or_trainer_profile", result.Reasons);
    }

    [Fact]
    public void Qualify_ForeignPhoneCode_Rejects()
    {
        var q = new Qualifier(new ParseOptions());
        // телефон из полей API не в российском формате — узбекский код
        var profile = Profile(p => { p.Biography = "Кухни на заказ. Казань. WhatsApp"; p.BioLinks = new List<string>(); p.ContactPhoneNumber = "+998901234567"; });
        var result = q.Qualify(Candidate(), profile, Now);
        Assert.Contains("foreign_phone_code", result.Reasons);
    }

    [Fact]
    public void Qualify_PhoneInBiographyText_IsExtracted()
    {
        var q = new Qualifier(new ParseOptions());
        var profile = Profile(p => p.Biography = "Кухни на заказ, Казань. Заказы: 8 900 123-45-67");
        var result = q.Qualify(Candidate(), profile, Now);
        Assert.Equal("79001234567", result.PublicPhone);
    }
}

public class ContactExtractorTests
{
    [Theory]
    [InlineData("+7 900 123-45-67", "79001234567")]
    [InlineData("89001234567", "79001234567")]
    [InlineData("9001234567", "79001234567")]
    [InlineData("https://wa.me/79001234567", "79001234567")]
    [InlineData("https://wa.me/77771234567", "")]       // не российский формат
    [InlineData("почта@пример.ру", "")]
    public void NormalizePhone_Variants(string input, string expected)
    {
        Assert.Equal(expected, ContactExtractor.NormalizePhone(input));
    }

    [Fact]
    public void FindMessenger_TelegramInBioText()
    {
        var profile = new IgProfile
        {
            Biography = "По вопросам: telegram @kuhni_sales",
            LatestPosts = { new IgPostItem { CaptionText = "" } },
        };
        var m = ContactExtractor.FindMessenger(profile);
        Assert.Equal("https://t.me/kuhni_sales", m.Telegram);
    }

    [Fact]
    public void DetectBioSite_TaplinkLikeDomain_IsSite()
    {
        var profile = new IgProfile { BioLinks = { "https://kuhni.taplink.ru" }, Biography = "" };
        var messenger = ContactExtractor.FindMessenger(profile);
        var site = ContactExtractor.DetectBioSite(profile, messenger);
        Assert.True(site.Found);
    }

    [Fact]
    public void DetectBioSite_OnlySocials_NotASite()
    {
        var profile = new IgProfile { BioLinks = { "https://t.me/kuhni", "https://vk.com/kuhni" }, Biography = "" };
        var messenger = ContactExtractor.FindMessenger(profile);
        var site = ContactExtractor.DetectBioSite(profile, messenger);
        Assert.False(site.Found);
    }
}

public class BuildLeadSectionTests
{
    private static QualificationResult Result(string wa, string tg) => new()
    {
        QualifiedPreSite = true,
        Messenger = new MessengerInfo(tg, wa, "profile_link", "profile_link", "", Array.Empty<string>()),
        BioSite = new BioSiteInfo(false, "", ""),
        PublicPhone = "",
        Location = new LocationInfo(true, "Казань", "known_city_in_profile", ""),
    };

    [Fact]
    public void WhatsAppFirst_WhatsAppLead_MainSection()
    {
        var preset = new ParsePreset { ContactMode = ContactMode.WhatsAppFirst };
        var lead = ParseEngine.BuildLead(1, new Candidate { Handle = "a", Niche = "n" }, new IgProfile(), Result("https://wa.me/79001234567", ""), preset);
        Assert.NotNull(lead);
        Assert.Equal(LeadSection.WhatsappReserve, lead!.Section);
        Assert.Equal("published", lead.WhatsappStatus);
    }

    [Fact]
    public void TelegramOnly_NoTelegram_NoLead()
    {
        var preset = new ParsePreset { ContactMode = ContactMode.TelegramOnly };
        var lead = ParseEngine.BuildLead(1, new Candidate { Handle = "a", Niche = "n" }, new IgProfile(), Result("https://wa.me/79001234567", ""), preset);
        Assert.Null(lead);
    }

    [Fact]
    public void TelegramFirst_TelegramLead_MainSection()
    {
        var preset = new ParsePreset { ContactMode = ContactMode.TelegramFirst };
        var lead = ParseEngine.BuildLead(1, new Candidate { Handle = "a", Niche = "n" }, new IgProfile(), Result("", "https://t.me/sales"), preset);
        Assert.NotNull(lead);
        Assert.Equal(LeadSection.TelegramResolved, lead!.Section);
    }

    [Fact]
    public void PhoneOnly_BuildsWaLink()
    {
        var preset = new ParsePreset { ContactMode = ContactMode.WhatsAppOnly };
        var result = Result("", "");
        var lead = ParseEngine.BuildLead(1, new Candidate { Handle = "a", Niche = "n" }, new IgProfile { FullName = "Мебель Татарстан" }, result with { PublicPhone = "79001234567" }, preset);
        Assert.NotNull(lead);
        Assert.Equal("https://wa.me/79001234567", lead!.WhatsappUrl);
    }
}

public class MessageGeneratorTests
{
    [Fact]
    public void Generate_SameHandle_SameText_WithQuotedHandle()
    {
        var a = MessageGenerator.Generate("kuhni_kazan", "кухни на заказ", "Казань");
        var b = MessageGenerator.Generate("kuhni_kazan", "кухни на заказ", "Казань");
        Assert.Equal(a, b);
        Assert.Contains("«kuhni_kazan»", a);
        Assert.Contains("сайт", a, StringComparison.OrdinalIgnoreCase);
    }
}

public class CsvExporterTests
{
    [Fact]
    public void Export_HeaderAndBomAndCrlf()
    {
        var lead = new LeadRecord
        {
            Handle = "kuhni_kazan", FullName = "Кухни Казань", City = "Казань", Niche = "кухни на заказ",
            Phone = "79001234567", WhatsappUrl = "https://wa.me/79001234567", WhatsappStatus = "published",
            Section = LeadSection.WhatsappReserve, OutreachText = "=СВЯЗЬ()",
        };
        var path = Path.Combine(Path.GetTempPath(), "leadhub-test-" + Guid.NewGuid() + ".csv");
        CsvExporter.Export(new List<LeadRecord> { lead }, path);

        var text = File.ReadAllText(path);
        Assert.StartsWith("\ufeff", text);
        Assert.Contains("\"Instagram\";\"Бизнес\";\"Город\";\"Ниша\";\"Телефон\"", text);
        Assert.Contains("https://wa.me/79001234567", text);
        Assert.Contains("'=СВЯЗЬ()", text);      // формула экранирована
        Assert.Contains("\r\n", text);
        File.Delete(path);
    }
}

public class SearchMatrixTests
{
    [Fact]
    public void Matrix_HasFullNicheSet_AndAllCities()
    {
        Assert.True(SearchMatrix.Niches.Count >= 120);
        Assert.Equal(70, SearchMatrix.Cities.Count);
        Assert.Contains(SearchMatrix.Cities, c => c.Name == "Москва" && c.Tier == 1);
        Assert.Contains(SearchMatrix.Cities, c => c.Name == "Братск" && c.Tier == 3);
    }

    [Fact]
    public void BuildQueryPlan_NicheByCityByTerms()
    {
        var preset = new ParsePreset
        {
            SelectedNiches = { "кухни на заказ под ключ" },
            SelectedCities = { "Казань" },
            CityTiers = { 1, 2, 3 },
            ContactMode = ContactMode.WhatsAppFirst,
        };
        var plan = ParseEngine.BuildQueryPlan(preset);
        Assert.All(plan, t => Assert.Contains("Казань", t.Query));
        Assert.True(plan.Count >= 6); // 3 фразы × 3 термина максимум
    }
}

public class StoreAndRotationTests : IDisposable
{
    private readonly Db _db;
    private readonly string _path;

    public StoreAndRotationTests()
    {
        _path = Path.Combine(Path.GetTempPath(), "leadhub-test-" + Guid.NewGuid() + ".db");
        _db = new Db(_path);
    }

    [Fact]
    public void VpnRepository_InsertDedup_All()
    {
        var repo = new VpnRepository(_db);
        var node = VlessLink.Parse("vless://11111111-2222-3333-4444-555555555555@1.2.3.4:443?security=tls#A");
        node.CountryCode = "DE";
        node.CountryName = "Германия";
        var id1 = repo.Insert(node);
        var id2 = repo.Insert(node);
        Assert.Equal(id1, id2);   // дедуп
        Assert.Single(repo.All());
    }

    [Fact]
    public void LeadRepository_InsertConflict_Updates()
    {
        var repo = new LeadRepository(_db);
        var lead = new LeadRecord { Handle = "dup_test", Section = LeadSection.WhatsappReserve, Phone = "79001112233" };
        repo.Insert(lead);
        lead.Section = LeadSection.TelegramResolved;
        repo.Insert(lead);
        Assert.True(repo.HandleExists("DUP_TEST"));
        Assert.Single(repo.All());
    }

    [Fact]
    public void Rotator_UsesLeastUsedAccount()
    {
        var accounts = new AccountRepository(_db);
        var usage = new UsageRepository(_db);
        var vpns = new VpnRepository(_db);

        var a1 = accounts.Insert("acc_one", new byte[] { 1 }, "1");
        var a2 = accounts.Insert("acc_two", new byte[] { 2 }, "2");
        // acc_one уже использовался сегодня
        usage.Record(a1, null, 1, 500);

        var vpn = vpns.Insert(new VpnNode { Server = "1.2.3.4", Port = 443, Uuid = "11111111-2222-3333-4444-555555555555", Name = "N1", CountryCode = "DE", CountryName = "Германия", LastLatencyMs = 120 });
        vpns.UpdateLatency(repo_node(vpn));

        var rotator = new Rotator(_db);
        var pair = rotator.ChoosePair(1, 10801, dailyCap: 5000);
        Assert.NotNull(pair);
        Assert.Equal("acc_two", pair!.Account.Username); // наименее используемый
        Assert.Equal("N1", pair.VpnNode.Name);
    }

    private static VpnNode repo_node(long id) => new() { Id = id, LastLatencyMs = 120, LastCheckedAt = DateTime.UtcNow };

    [Fact]
    public void Crypto_Roundtrip()
    {
        var key = Crypto.LoadOrCreateKey(Path.Combine(Path.GetTempPath(), "lh-test.key"));
        var payload = Crypto.EncryptString("sessionid=secret", key);
        Assert.Equal("sessionid=secret", Crypto.DecryptString(payload, key));
    }

    [Fact]
    public void AccountPackage_PasswordRoundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "test.lhaccount");
        Crypto.WriteAccountPackage(path, "{\"sessionid\":\"x\"}", "пароль123");
        var json = Crypto.ReadAccountPackage(path, "пароль123");
        Assert.Contains("sessionid", json);
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => Crypto.ReadAccountPackage(path, "неверный"));
        File.Delete(path);
    }

    [Fact]
    public void CookieSession_Parses_HeaderAndJson()
    {
        var header = CookieSession.Parse("sessionid=abc; csrftoken=def; ds_user_id=12345");
        Assert.True(header.IsUsable);
        var json = CookieSession.Parse("[{\"name\":\"sessionid\",\"value\":\"xyz\"},{\"name\":\"csrftoken\",\"value\":\"ct\"}]");
        Assert.Equal("xyz", json.SessionId);
    }

    public void Dispose() => _db.Dispose();
}
