using Microsoft.EntityFrameworkCore;
using Ws.Core.Domain;

namespace Ws.Core.Data;

/// <summary>
/// Placeholder data for the Wise City demo until the salon provides real artists, prices and policies.
/// Runs only on an empty database.
/// </summary>
public static class SeedData
{
    public static async Task EnsureSeededAsync(WsDbContext db, CancellationToken ct = default)
    {
        if (await db.Salons.AnyAsync(ct)) return;

        var salon = new Salon
        {
            Name = "Wise City",
            TimeZoneId = "Europe/Warsaw", // placeholder until the salon's city is known
            Currency = "EUR",
            Address = new("1 Example Street, City", "ул. Примерная, 1, Город"),
            Phone = "+000 000 000 000",
            Instagram = "wisecity.tattoo",
            About = new(
                "Wise City is a tattoo & piercing studio: three tattoo artists, two piercers, one cozy room.",
                "Wise City: тату и пирсинг студия. Три тату-мастера, два пирсера, одна уютная комната."),
            Policies = new(PoliciesEn, PoliciesRu),
            MinAgeWithGuardian = 16,
            MinAgeSolo = 18,
            OpeningHours = Enum.GetValues<DayOfWeek>()
                .Select(d => new SalonHours { Day = d, Open = new(11, 0), Close = new(20, 0) })
                .ToList(),
            Rooms = [new Room { Name = "Main room", Workstations = 3 }],
        };
        db.Salons.Add(salon);
        await db.SaveChangesAsync(ct);

        var services = Services(salon.Id);
        db.Services.AddRange(services);

        var alex = TattooArtist(salon.Id, "Alex", 1, ["fine-line", "minimalism", "lettering", "botanical"],
            new("Delicate fine-line work, tiny symbols, lettering and botanicals.",
                "Тонкие линии, минимализм, надписи и ботаника."),
            Days(DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday));
        var mira = TattooArtist(salon.Id, "Mira", 2, ["realism", "black-and-grey", "portrait"],
            new("Black & grey realism and portraits. Loves big projects.",
                "Чёрно-серый реализм и портреты. Любит большие проекты."),
            Days(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday));
        var dan = TattooArtist(salon.Id, "Dan", 3, ["traditional", "neo-traditional", "blackwork", "color"],
            new("Bold traditional and neo-traditional, blackwork, bright color.",
                "Олдскул и нео-традишнл, блэкворк, яркий цвет."),
            Days(DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday));
        var kate = Piercer(salon.Id, "Kate", 4,
            new("Piercer with a soft touch: ears, face, curated ear projects.",
                "Пирсер с лёгкой рукой: уши, лицо, композиции на ушах."),
            Days(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday));
        var leo = Piercer(salon.Id, "Leo", 5,
            new("Experienced piercer, all body piercings including intimate ones.",
                "Опытный пирсер, любые проколы, включая интимные."),
            Days(DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday));
        Artist[] tattooists = [alex, mira, dan];
        Artist[] piercers = [kate, leo];
        db.Artists.AddRange([.. tattooists, .. piercers]);

        foreach (var service in services)
        {
            var who = service.Kind is ServiceKind.Piercing or ServiceKind.JewelryChange ? piercers : tattooists;
            foreach (var artist in who)
                db.ArtistServices.Add(new ArtistService { Artist = artist, Service = service });
        }

        await db.SaveChangesAsync(ct);
    }

    private static List<Service> Services(int salonId)
    {
        var order = 0;
        Service S(ServiceKind kind, string en, string ru, int minutes, int buffer, decimal? from, decimal? to = null,
            bool online = true, bool privateRoom = false, bool adults = false, string descEn = "", string descRu = "") => new()
        {
            SalonId = salonId,
            Kind = kind,
            Name = new(en, ru),
            Description = new(descEn, descRu),
            DurationMinutes = minutes,
            BufferMinutes = buffer,
            PriceFrom = from,
            PriceTo = to,
            BookableOnline = online,
            NeedsPrivateRoom = privateRoom,
            AdultsOnly = adults,
            SortOrder = order++,
        };

        return
        [
            S(ServiceKind.TattooConsultation, "Tattoo consultation", "Консультация по тату", 30, 0, null,
                descEn: "Free. Discuss your idea, size, placement and style; the artist estimates time and price.",
                descRu: "Бесплатно. Обсудим идею, размер, место и стиль; мастер оценит время и стоимость."),
            S(ServiceKind.TattooSession, "Tattoo session: small (up to ~10 cm)", "Тату-сеанс: маленькая (до ~10 см)", 120, 30, 80, 150, online: false),
            S(ServiceKind.TattooSession, "Tattoo session: medium (half day)", "Тату-сеанс: средняя (полдня)", 240, 30, 150, 300, online: false),
            S(ServiceKind.TattooSession, "Tattoo session: full day", "Тату-сеанс: полный день", 360, 30, 300, 500, online: false),
            S(ServiceKind.TattooTouchUp, "Tattoo touch-up", "Коррекция тату", 60, 15, null,
                descEn: "Free within 3 months after the session.", descRu: "Бесплатно в течение 3 месяцев после сеанса."),

            S(ServiceKind.Piercing, "Earlobe piercing", "Прокол мочки уха", 30, 15, 30),
            S(ServiceKind.Piercing, "Helix / cartilage piercing", "Прокол хряща (хеликс)", 30, 15, 40),
            S(ServiceKind.Piercing, "Tragus piercing", "Прокол козелка (трагус)", 30, 15, 40),
            S(ServiceKind.Piercing, "Industrial piercing", "Индастриал", 45, 15, 60),
            S(ServiceKind.Piercing, "Nostril piercing", "Прокол крыла носа", 30, 15, 40),
            S(ServiceKind.Piercing, "Septum piercing", "Прокол септума", 30, 15, 50),
            S(ServiceKind.Piercing, "Eyebrow piercing", "Прокол брови", 30, 15, 40),
            S(ServiceKind.Piercing, "Lip / labret piercing", "Прокол губы (лабрет)", 30, 15, 45),
            S(ServiceKind.Piercing, "Tongue piercing", "Прокол языка", 30, 15, 50),
            S(ServiceKind.Piercing, "Navel piercing", "Прокол пупка", 30, 15, 50),
            S(ServiceKind.Piercing, "Nipple piercing", "Прокол соска", 30, 15, 50, privateRoom: true, adults: true),
            S(ServiceKind.Piercing, "Intimate piercing", "Интимный пирсинг", 45, 15, 70, privateRoom: true, adults: true),
            S(ServiceKind.JewelryChange, "Jewelry change", "Замена украшения", 15, 5, 10),
        ];
    }

    private static Artist TattooArtist(int salonId, string name, int order, List<string> styles, LocalizedText bio, List<WorkingHours> hours) =>
        new() { SalonId = salonId, Name = name, Specialty = ArtistSpecialty.Tattoo, Styles = styles, Bio = bio, WorkingHours = hours, SortOrder = order };

    private static Artist Piercer(int salonId, string name, int order, LocalizedText bio, List<WorkingHours> hours) =>
        new() { SalonId = salonId, Name = name, Specialty = ArtistSpecialty.Piercing, Styles = ["piercing"], Bio = bio, WorkingHours = hours, SortOrder = order };

    private static List<WorkingHours> Days(params DayOfWeek[] days) =>
        days.Select(d => new WorkingHours { Day = d, Start = new(11, 0), End = new(20, 0) }).ToList();

    private const string PoliciesEn = """
        ## Age and ID
        - 18+ can book on their own.
        - 16-17 only with a parent or legal guardian present in person (both bring ID).
        - Under 16: we don't tattoo or pierce.
        - Nipple and intimate piercings: 18+ only.
        - Bring a photo ID to every appointment.

        ## Booking and deposits
        - Tattoos start with a free consultation. The artist then sizes the work and books the session with you.
        - Piercings, touch-ups and jewelry changes can be booked directly.
        - Every booking is confirmed by the artist; you'll get a message once it's approved.
        - No online deposits for now.

        ## Cancellation
        - Please cancel or reschedule at least 24 hours in advance.
        - More than 15 minutes late may mean we need to reschedule.

        ## Before the appointment
        - Eat well, sleep, no alcohol 24 hours before.
        - Not during illness, pregnancy (piercings/tattoos), or on blood thinners without a doctor's OK.

        ## Pain and healing
        - Pain is individual; most clients describe it as scratching or a sting. Bony areas hurt more.
        - Tattoos: surface heals in 2-3 weeks, fully in about a month.
        - Piercings: earlobe 6-8 weeks, cartilage 6-12 months, navel 6-12 months, tongue 4-6 weeks.

        ## Aftercare
        - Tattoo: keep the film on as instructed, wash gently, thin layer of healing cream, no sun, pool or sauna for 2 weeks.
        - Piercing: saline spray twice a day, don't twist or touch, don't change jewelry until healed.
        - Anything unusual (strong redness, swelling, heat, pus): contact us.
        """;

    private const string PoliciesRu = """
        ## Возраст и документы
        - С 18 лет можно записаться самостоятельно.
        - 16-17 лет: только в присутствии родителя или законного представителя (оба с документами).
        - До 16 лет: не делаем ни тату, ни пирсинг.
        - Прокол сосков и интимный пирсинг: только 18+.
        - На каждый сеанс берите документ с фото.

        ## Запись и предоплата
        - Тату начинается с бесплатной консультации, после неё мастер оценивает работу и записывает на сеанс.
        - На пирсинг, коррекцию и замену украшения можно записаться сразу.
        - Каждую запись подтверждает мастер, после подтверждения придёт сообщение.
        - Онлайн-предоплаты пока нет.

        ## Отмена
        - Пожалуйста, отменяйте или переносите запись минимум за 24 часа.
        - При опоздании больше чем на 15 минут сеанс может потребоваться перенести.

        ## Перед сеансом
        - Хорошо поесть, выспаться, не пить алкоголь за 24 часа.
        - Не во время болезни, беременности или приёма разжижающих кровь без согласия врача.

        ## Боль и заживление
        - Боль индивидуальна, обычно это ощущается как царапание или укол. На костях больнее.
        - Тату: поверхностно заживает за 2-3 недели, полностью примерно за месяц.
        - Пирсинг: мочка 6-8 недель, хрящ 6-12 месяцев, пупок 6-12 месяцев, язык 4-6 недель.

        ## Уход
        - Тату: плёнку держать по инструкции мастера, мягко промывать, тонкий слой заживляющего крема, без солнца, бассейна и сауны 2 недели.
        - Пирсинг: солевой спрей 2 раза в день, не крутить и не трогать, не менять украшение до заживления.
        - Если что-то необычное (сильное покраснение, отёк, жар, гной), напишите нам.
        """;
}
