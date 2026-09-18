using StandRiseServer.MongoDB.Data;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StandRiseServer.RpcServer
{
    /// <summary>
    /// Локальные настройки для переноса на Win11 — файл local.settings.json рядом с VsCode.dll.
    /// </summary>
    public static class LocalServerConfig
    {
        public sealed class MongoSettings
        {
            public string DatabaseName { get; set; } = "Main";
            public string Address { get; set; } = "127.0.0.1";
            public string Port { get; set; } = "27017";
            public string Uri { get; set; } = "";
        }

        /// <summary>Один OAuth-клиент Google (Android / iOS / Web).</summary>
        public sealed class GoogleOAuthClient
        {
            public string ClientId { get; set; } = "";
            /// <summary>Только у Web-клиента. У мобильных клиентов секрета нет.</summary>
            public string ClientSecret { get; set; } = "";
            /// <summary>Для iOS — REVERSED_CLIENT_ID + ":/oauth2redirect".</summary>
            public string RedirectUri { get; set; } = "";
        }

        /// <summary>
        /// Мульти-платформенный OAuth: у Android и iOS РАЗНЫЕ Client ID одного проекта Google.
        /// Пусто — берутся значения по умолчанию, зашитые в GoogleAuthRemoteService.
        /// </summary>
        public sealed class GoogleOAuthSettings
        {
            public GoogleOAuthClient Android { get; set; } = new GoogleOAuthClient();
            public GoogleOAuthClient Ios { get; set; } = new GoogleOAuthClient();
            public GoogleOAuthClient Web { get; set; } = new GoogleOAuthClient();
            /// <summary>Дополнительные client_id, которые тоже считаются валидной аудиторией токена.</summary>
            public string[] ExtraAcceptedClientIds { get; set; } = new string[0];
        }

        public sealed class Settings
        {
            public string PublicIp { get; set; } = "127.0.0.1";
            public int RpcPort { get; set; } = MainServerConfig.RpcPort;
            public int HttpApiPort { get; set; } = MainServerConfig.HttpApiPort;
            public int PhotonMasterPort { get; set; } = MainServerConfig.PhotonMasterPort;
            public int PhotonGamePort { get; set; } = MainServerConfig.PhotonGamePort;

            /// <summary>
            /// false (по умолчанию): onMatchmakingDone уходит игроку только ПОСЛЕ ConfirmMatch.
            /// Клиент 0.17 схлопывает окно «ИГРА НАЙДЕНА / ПОДТВЕРДИТЬ», если Done приходит
            /// сразу за Confirmation. true — старое поведение (Done вместе с Confirmation).
            /// </summary>
            public bool SendDoneWithConfirmUi { get; set; } = false;

            /// <summary>
            /// Форма ответа на прокрутку спина (ExchangeResult). Клиент 0.17 принимает
            /// только одну конкретную — подбирается опытным путём, перезапуск без пересборки.
            /// 0 — списание(qty=0) + приз + spent + push remove  (текущая)
            /// 1 — только приз + spent + push remove
            /// 2 — списание + приз, без spent, + push remove
            /// 3 — списание + приз + spent, без push
            /// 4 — только приз, без spent, без push
            /// 5 — списание(qty=1) + приз + spent + push remove
            /// </summary>
            public int SpinResultVariant { get; set; } = 0;

            /// <summary>
            /// true (по умолчанию): игрок не попадёт в матч, не нажав ПОДТВЕРДИТЬ.
            /// onMatchmakingDone не уходит неподтвердившемуся ни по одному каналу,
            /// и обычные (неранговые) режимы тоже проходят через окно подтверждения.
            /// </summary>
            public bool AlwaysRequireConfirm { get; set; } = true;

            /// <summary>
            /// true: вместе с Confirmation слать onPlayersConfirmed с составом матча.
            /// Именно это событие открывает окно «ИГРА НАЙДЕНА / ПОДТВЕРДИТЬ» в 0.17 —
            /// раньше оно прилетало только от ботов, а в матче двух живых игроков
            /// его не слал никто, и клиент оставался в бесконечном поиске.
            /// </summary>
            public bool PrimeConfirmWithPlayersConfirmed { get; set; } = true;

            /// <summary>
            /// true (по умолчанию): onPlayersConfirmed рассылается только тем, кто уже
            /// нажал ПОДТВЕРДИТЬ. Клиент 0.17 по этому событию ЗАКРЫВАЕТ окно
            /// «ИГРА НАЙДЕНА»: когда подтверждался первый игрок, второму прилетал
            /// onPlayersConfirmed со списком [первый], окно у него исчезало и в матч
            /// он уже не попадал. false — слать всем, как раньше.
            /// </summary>
            public bool OnlyNotifyConfirmedPlayers { get; set; } = true;

            /// <summary>
            /// Сколько секунд сервер ждёт подтверждения матча. Должно быть чуть больше
            /// обратного отсчёта в клиенте (~16 с): раньше стояло 45 с, и у игрока,
            /// который подтвердил, экран висел ещё полминуты после конца таймера.
            /// </summary>
            public int ConfirmTimeoutSeconds { get; set; } = 18;

            /// <summary>
            /// Пауза между «оба подтвердили» и рассылкой onGameStarted, мс (0..5000).
            /// Держим коротко: клиентский confirm ~16с, нельзя жечь секунды здесь.
            /// </summary>
            public int MatchStartHoldMs { get; set; } = 300;

            /// <summary>
            /// Пауза хост CreateRoom → гости Join, мс (0..10000).
            /// 3000 ломало Allies: Done уже закрыл UI, гость ждал 3с и клиентский
            /// таймер (~16с) успевал истечь → «впустил одного», confirm зависал.
            /// 500–700мс хватает Photon CreateRoom без проигрыша таймеру.
            /// </summary>
            public int MatchStartStaggerMs { get; set; } = 600;

            /// <summary>
            /// Сколько раз повторить onGameStarted, если канал игрока не принял событие
            /// (0..5). Раньше ретрая не было и старт просто терялся.
            /// </summary>
            public int MatchStartRetryCount { get; set; } = 2;

            /// <summary>
            /// Досылать приз спина ещё и событием onInventoryChanged(added).
            /// false по умолчанию: приз уже приходит в ExchangeResult, а лишний
            /// add-push прилетает поверх анимации колеса и ломает SpinnerRelease.
            /// </summary>
            public bool SpinPushPrizeAdded { get; set; } = false;

            /// <summary>
            /// Увеличивать счётчик спинов (валюта 200001) при прокрутке.
            /// false по умолчанию: CurrencyPlusValue вызывает CurrencyUpdate, который ПУШИТ
            /// клиенту CurrencyAmount[] с валютой 200001, а она, доходя до клиента 0.17,
            /// вызывает «ОШИБКА ЗАПРОСА» — это уже было зафиксировано в getRecipeStatus.
            /// </summary>
            public bool SpinCountCurrency200001 { get; set; } = false;

            /// <summary>
            /// Состав пула рулетки Cursed Souls:
            /// 0 — только оружейные скины коллекции (8 шт., по умолчанию)
            /// 1 — скины + брелоки + наклейки (31 шт.)
            /// </summary>
            /// <summary>
            /// Явный список предметов рулетки. Вся коллекция cursed_souls, кроме
            /// Tanto "Restless" (148000), раздаётся лентой боевого пропуска
            /// (рецепты CURSED_SOULS_SKINS / _STICKERS / _CHARMS), поэтому выводить
            /// пул из коллекции нельзя — колесо задаётся клиентом.
            /// Здесь перечисляются id призов; пусто — берётся встроенный список.
            /// </summary>
            public int[] SpinPrizeItems { get; set; } = new int[0];

            public int SpinPoolMode { get; set; } = 0;

            /// <summary>
            /// false (по умолчанию): клиент читает имена статистик буквально —
            /// ranked_2v2_calibration_match_count = сыграно калибровочных матчей,
            /// ranked_2v2_won_match_count = победы. Проверено по строковым константам
            /// в дампе клиента 0.17. Историческое предположение о «свопе» этих имён
            /// ломало экран калибровки. true — вернуть старое поведение.
            /// </summary>
            public bool ClientStatNamesSwapped { get; set; } = false;

            /// <summary>
            /// Обновлять состав в окне подтверждения у тех, кто ещё не нажал ПОДТВЕРДИТЬ —
            /// без этого игрок не видит, что напарник уже принял, и окно выглядит «мёртвым».
            /// true по умолчанию.
            ///
            /// Событие уходит с ПУСТЫМ NewConfirmedPlayers (непустой список 0.17 понимает как
            /// «фаза подтверждения закончилась» и окно закрывает) — меняются только флаги
            /// Confirmed внутри MatchGroup, из них клиент и рисует галочки.
            ///
            /// Если на живых матчах окажется, что 0.17 всё-таки ломает стейт окна у второго
            /// игрока (принял первым → матч не стартует), флаг гасится одной строкой в
            /// local.settings.json: "RefreshConfirmRosterForPending": false. Вход в комнату
            /// важнее галочек.
            /// </summary>
            /// false: не шлём повторный onPlayersConfirmed тем, кто ещё не нажал.
            /// Повторное событие в 0.17 иногда снимает кнопку «ПОДТВЕРДИТЬ» у второго игрока.
            /// Галочки в окне менее важны, чем то, что кнопка прожимается у всех.
            public bool RefreshConfirmRosterForPending { get; set; } = false;

            /// <summary>
            /// true (по умолчанию): скины продаются напрямую за голду. Цена по редкости —
            /// 1:50, 2:100, 3:200, 4:350, 5:700, 6:5000. Шкала выведена из вероятностей
            /// самой игры: кейс стоит 100 голды и даёт скин редкости 3 с шансом 0.53,
            /// 4 — 0.30, 5 — 0.15, 6 — 0.02, то есть 100/шанс.
            ///
            /// false — скины покупаются только из кейсов и на маркетплейсе, как в оригинале.
            /// </summary>
            public bool SellSkinsForGold { get; set; } = true;

            /// <summary>
            /// Номер живого сезона. Им штампуются новые матчи и история (FinishedMatch.SeasonId),
            /// им же отвечает current_season_id в статистике. Клиент 0.17 отбрасывает из списка
            /// матчи, чей сезон не совпадает с тем, который открыт во вкладке, — поэтому номер
            /// должен быть один на весь сервер и меняться здесь, без пересборки.
            /// </summary>
            public int LiveSeasonId { get; set; } = 3;

            /// <summary>
            /// false (по умолчанию): при переключении на прошлые сезоны показываются реальные
            /// текущие цифры. true — старое поведение: сервер выдумывал «историю» прошлых
            /// сезонов, домножая статы на стабильный коэффициент 0.5..0.95.
            /// </summary>
            public bool FakeSeasonHistory { get; set; } = false;

            /// <summary>
            /// Сезон, которым штампуются обычные, соревновательные и матчи союзников
            /// (FinishedMatch.SeasonId). Клиент 0.17 не спрашивает сезон у сервера — номер
            /// вкладки у него вшит, и матчи с другим сезоном он молча выбрасывает из списка.
            /// Соревновательная вкладка открывается на «СЕЗОН 1», поэтому 1.
            /// </summary>
            public int MatchSeasonRegular { get; set; } = 1;

            /// <summary>
            /// Сезон для клановых битв. Вкладка статистики клана открывается на «СЕЗОН 2».
            /// </summary>
            public int MatchSeasonClanBattle { get; set; } = 2;

            /// <summary>
            /// Сколько строк игроков держать в истории матча союзников. 0 (по умолчанию) —
            /// брать реальный размер режима из админки: 1v1 → 2 строки, 2v2 → 4. Раньше здесь
            /// была жёсткая четвёрка, и матч 1на1 дописывался двумя выдуманными ботами
            /// Player3/Player4, которых в матче не было.
            /// Число &gt; 0 задаёт размер принудительно — на случай, если клиент откажется
            /// показывать список с двумя строками.
            /// </summary>
            public int HistoryRosterAllies { get; set; } = 0;

            /// <summary>
            /// true (по умолчанию): ответ на спин строится ровно так же, как на рабочие
            /// рецепты боевого пропуска (CURSED_SOULS_SKINS и т.п.) — один ExchangeResult
            /// только с призом, без строки списания с Quantity=0 и без поля Spent.
            /// Именно эти лишние поля клиент 0.17 не разбирал и показывал «Ошибка запроса»
            /// (Common/RequestFailed), хотя предмет на сервере уже выдавался.
            /// false — старое поведение через SpinResultVariant.
            /// </summary>
            public bool SpinCleanResult { get; set; } = true;

            /// <summary>
            /// Через сколько миллисекунд после ответа дослать onInventoryChanged со списанием
            /// токена спина. 0 — не досылать вовсе. Отдельным (отложенным) событием, чтобы
            /// оно не смешивалось с разбором ответа на прокрут.
            /// </summary>
            public int SpinPushRemoveAfterMs { get; set; } = 1500;

            /// <summary>
            /// OAuth-клиенты Google по платформам. У Android и iOS разные Client ID
            /// (в GoogleService-Info.plist это ANDROID_CLIENT_ID и CLIENT_ID).
            /// </summary>
            public GoogleOAuthSettings GoogleOAuth { get; set; } = new GoogleOAuthSettings();

            public MongoSettings MongoMain { get; set; } = new MongoSettings
            {
                DatabaseName = "Main",
                Address = "127.0.0.1",
                Port = "2077",
                Uri = "mongodb://eliseyy22:eliseyy22%21@127.0.0.1:2077/?authSource=admin&authMechanism=SCRAM-SHA-256"
            };
            public MongoSettings MongoGame { get; set; } = new MongoSettings
            {
                DatabaseName = "Inventory",
                Address = "127.0.0.1",
                Port = "1337",
                Uri = "mongodb://eliseyy22:eliseyy22%21@127.0.0.1:1337/?authSource=admin&authMechanism=SCRAM-SHA-256"
            };
        }

        public static Settings Current { get; private set; } = new Settings();
        public static string LoadedFrom { get; private set; }
        public static bool IsLoaded => !string.IsNullOrEmpty(LoadedFrom);

        public static void Load()
        {
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory ?? ".", "local.settings.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "local.settings.json"),
            };

            foreach (string path in candidates)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                try
                {
                    string json = File.ReadAllText(path);
                    var parsed = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
                    if (parsed != null)
                    {
                        Current = parsed;
                        LoadedFrom = path;
                        Console.WriteLine("[boot] Local settings: " + path);
                        Console.WriteLine("[boot] PublicIp=" + Current.PublicIp);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[boot] local.settings.json warn (" + path + "): " + ex.Message);
                }
            }

            Current = new Settings();
            LoadedFrom = null;
            Console.WriteLine("[boot] local.settings.json not found — using defaults from source code");
        }

        public static BoltDatabaseInfo ToMainDatabaseInfo()
        {
            MongoSettings m = Current.MongoMain ?? new MongoSettings();
            return new BoltDatabaseInfo
            {
                DatabaseName = string.IsNullOrWhiteSpace(m.DatabaseName) ? "Main" : m.DatabaseName,
                DatabaseType = BoltDatabaseType.Main,
                Address = m.Address ?? "127.0.0.1",
                Port = m.Port ?? "2077",
                Uri = m.Uri ?? ""
            };
        }

        public static BoltDatabaseInfo ToGameDatabaseInfo()
        {
            MongoSettings m = Current.MongoGame ?? new MongoSettings();
            return new BoltDatabaseInfo
            {
                DatabaseName = string.IsNullOrWhiteSpace(m.DatabaseName) ? "Inventory" : m.DatabaseName,
                DatabaseType = BoltDatabaseType.Game,
                Address = m.Address ?? "127.0.0.1",
                Port = m.Port ?? "1337",
                Uri = m.Uri ?? ""
            };
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
