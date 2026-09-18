using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.RpcServer.Api;

namespace StandRiseServer.RpcServer
{
    public partial class DonateBotService
    {
        private readonly ConcurrentDictionary<long, string> _adminManagedPlayerId = new();
        private readonly ConcurrentDictionary<long, string> _adminClanEditId = new();
        private readonly ConcurrentDictionary<long, string> _adminClanOldTag = new();
        private readonly ConcurrentDictionary<long, PendingRankGrant> _adminPendingRank = new();
        private readonly ConcurrentDictionary<long, string> _adminPendingMmrMode = new();

        private sealed class PendingRankGrant
        {
            public string Mode = "allies"; // allies | comp
            public int Rank;
            public string PlayerId = "";
            public float? Mmr;
            public int? Wins;
            public int? Losses;
            public int? Kills;
            public int? Deaths;
        }

        private async Task HandleAdminExtraCallback(ITelegramBotClient botClient, long chatId, long userId, string data, CancellationToken ct)
        {
            data ??= "";
            if (data.StartsWith("admin_rank:", StringComparison.OrdinalIgnoreCase)
                || data.StartsWith("admin_rank_comp:", StringComparison.OrdinalIgnoreCase))
            {
                bool isComp = data.StartsWith("admin_rank_comp:", StringComparison.OrdinalIgnoreCase);
                string num = data.Substring(isComp ? "admin_rank_comp:".Length : "admin_rank:".Length);
                if (!int.TryParse(num, out int rank))
                {
                    await botClient.SendMessage(chatId, "Неверный ранг.", cancellationToken: ct);
                    return;
                }
                await BeginRankGrantFlow(botClient, chatId, ct, isComp ? "comp" : "allies", rank);
                return;
            }

            if (data.StartsWith("admin_comp_set:", StringComparison.OrdinalIgnoreCase))
            {
                string compNum = data.Substring("admin_comp_set:".Length);
                if (!int.TryParse(compNum, out int compPlayers) || (compPlayers != 2 && compPlayers != 10))
                {
                    await botClient.SendMessage(chatId,
                        "Доступно только 2 (1v1) или 10 (5v5).",
                        cancellationToken: ct);
                    await ShowCompetitiveMatchmakingMenu(botClient, chatId, ct);
                    return;
                }
                CompetitiveMatchmakingConfig.SetRequiredPlayers(compPlayers);
                await botClient.SendMessage(chatId,
                    $"✅ Соревновательный: {CompetitiveMatchmakingConfig.ModeLabel()}",
                    cancellationToken: ct);
                await ShowCompetitiveMatchmakingMenu(botClient, chatId, ct);
                return;
            }

            if (data.StartsWith("admin_allies_set:", StringComparison.OrdinalIgnoreCase))
            {
                string num = data.Substring("admin_allies_set:".Length);
                if (!int.TryParse(num, out int players) || (players != 2 && players != 4))
                {
                    await botClient.SendMessage(chatId,
                        "Доступно только 2 (1v1) или 4 (2v2). Без ботов 1 и 3 игрока не запускают матч.",
                        cancellationToken: ct);
                    await ShowAlliesMatchmakingMenu(botClient, chatId, ct);
                    return;
                }
                AlliesMatchmakingConfig.SetRequiredPlayers(players);
                await botClient.SendMessage(chatId,
                    $"✅ Союзники: {AlliesMatchmakingConfig.ModeLabel()}",
                    cancellationToken: ct);
                await ShowAlliesMatchmakingMenu(botClient, chatId, ct);
                return;
            }

            if (data.StartsWith("admin_calib:", StringComparison.OrdinalIgnoreCase))
            {
                if (!_adminManagedPlayerId.TryGetValue(chatId, out string cpid) || string.IsNullOrEmpty(cpid))
                {
                    await botClient.SendMessage(chatId, "Сначала открой «Игрок» и укажи UID.", cancellationToken: ct);
                    return;
                }
                if (!int.TryParse(data.Substring("admin_calib:".Length), out int left) || left < 1 || left > 9)
                {
                    await botClient.SendMessage(chatId, "Осталось калибровки: 1–9.", cancellationToken: ct);
                    return;
                }
                try
                {
                    PlayerStatsManager.SetAlliesCalibrationRemaining(cpid, left);
                    await botClient.SendMessage(chatId,
                        $"Калибровка: осталось {left} игр → `{cpid}`",
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    await botClient.SendMessage(chatId, "Ошибка: " + ex.Message, cancellationToken: ct);
                }
                return;
            }

            switch (data)
            {
                case "admin_comp_mm":
                    await ShowCompetitiveMatchmakingMenu(botClient, chatId, ct);
                    break;

                case "admin_allies_mm":
                    await ShowAlliesMatchmakingMenu(botClient, chatId, ct);
                    break;

                case "admin_reset_all_calib":
                    try
                    {
                        int n = PlayerStatsManager.ResetAllAlliesToCalibration();
                        await botClient.SendMessage(chatId, $"Всем сброшено звание Allies на калибровку. Игроков: {n}.", cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        await botClient.SendMessage(chatId, "Ошибка сброса: " + ex.Message, cancellationToken: ct);
                    }
                    break;

                case "admin_db_reset":
                    await botClient.SendMessage(chatId,
                        "⚠️ Безопасный сброс Allies БД\n\n" +
                        "• Сбросит звания/статы Allies у ВСЕХ игроков\n" +
                        "• Очистит историю матчей (если осталась)\n" +
                        "• Аккаунты и инвентарь НЕ трогаются\n\n" +
                        "Подтвердить?",
                        replyMarkup: new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("✅ Да, сбросить Allies БД", "admin_db_reset_yes") },
                            new[] { InlineKeyboardButton.WithCallbackData("« Отмена", "menu_admin") },
                        }),
                        cancellationToken: ct);
                    break;

                case "admin_db_reset_yes":
                    try
                    {
                        string report = PlayerStatsManager.SafeResetAlliesDatabase(rebuildHistory: false);
                        await botClient.SendMessage(chatId, "✅ Сброс Allies БД выполнен:\n" + report, cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        await botClient.SendMessage(chatId, "Ошибка сброса БД: " + ex.Message, cancellationToken: ct);
                    }
                    break;

                case "admin_give_menu":
                    await botClient.SendMessage(chatId, "Выдачи:", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Gold Pass", "admin_give_bp"), InlineKeyboardButton.WithCallbackData("Спины", "admin_give_spins") },
                        new[] { InlineKeyboardButton.WithCallbackData("Уровни БП", "admin_give_levels"), InlineKeyboardButton.WithCallbackData("Предмет по defId", "admin_user_item") },
                        new[] { InlineKeyboardButton.WithCallbackData("Уровень аккаунта", "admin_user_level") },
                        new[] { InlineKeyboardButton.WithCallbackData("💰 Голда → единая форма", "admin_give_gold") },
                        new[] { InlineKeyboardButton.WithCallbackData("« Назад", "menu_admin") },
                    }), cancellationToken: ct);
                    break;

                case "admin_self_menu":
                    await botClient.SendMessage(chatId, "Себе (без голды — голда только через единую форму):", replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Gold Pass", "admin_self_bp"), InlineKeyboardButton.WithCallbackData("100 спинов", "admin_self_spins") },
                        new[] { InlineKeyboardButton.WithCallbackData("+10 ур. БП", "admin_self_levels") },
                        new[] { InlineKeyboardButton.WithCallbackData("💰 Голда → единая форма", "admin_give_gold") },
                        new[] { InlineKeyboardButton.WithCallbackData("« Назад", "menu_admin") },
                    }), cancellationToken: ct);
                    break;

                case "admin_user_hub":
                    _state[chatId] = DonateState.WaitingAdminUserUid;
                    await botClient.SendMessage(chatId,
                        "Управление игроком\nПришли UID / ник / ObjectId:",
                        cancellationToken: ct);
                    break;

                case "admin_rank_menu":
                    if (!_adminManagedPlayerId.ContainsKey(chatId))
                    {
                        _state[chatId] = DonateState.WaitingAdminUserUid;
                        await botClient.SendMessage(chatId, "Сначала укажи игрока (UID). После этого откроется список званий.", cancellationToken: ct);
                        break;
                    }
                    await ShowRankGrantKeyboard(botClient, chatId, "allies", ct);
                    break;

                case "admin_rank_comp_menu":
                    if (!_adminManagedPlayerId.ContainsKey(chatId))
                    {
                        _state[chatId] = DonateState.WaitingAdminUserUid;
                        await botClient.SendMessage(chatId, "Сначала укажи игрока (UID). После этого откроется список званий Соревновательного.", cancellationToken: ct);
                        break;
                    }
                    await ShowRankGrantKeyboard(botClient, chatId, "comp", ct);
                    break;

                case "admin_user_customid":
                    if (!RequireManagedPlayer(chatId, out string cidPid))
                    {
                        await botClient.SendMessage(chatId, "Сначала выбери игрока.", cancellationToken: ct);
                        break;
                    }
                    _state[chatId] = DonateState.WaitingAdminCustomId;
                    // Без Markdown: одиночный «_» в подсказке Telegram принимает за начало
                    // курсива, закрывающего не находит и отвечает 400 can't parse entities.
                    // Исключение всплывало из HandleCallback — кнопка выглядела мёртвой.
                    await botClient.SendMessage(chatId,
                        $"Новый Custom ID для {cidPid}\n(3-16 символов: латиница, цифры, знак подчёркивания):",
                        cancellationToken: ct);
                    break;

                case "admin_clan":
                    _state[chatId] = DonateState.WaitingAdminClanTag;
                    await botClient.SendMessage(chatId, "Введи текущий тег клана:", cancellationToken: ct);
                    break;

                case "admin_user_ban":
                    if (!RequireManagedPlayer(chatId, out _))
                    {
                        await botClient.SendMessage(chatId, "Сначала выбери игрока.", cancellationToken: ct);
                        break;
                    }
                    _state[chatId] = DonateState.WaitingAdminBanReason;
                    await botClient.SendMessage(chatId, "Причина бана (или «-»):", cancellationToken: ct);
                    break;

                case "admin_user_unban":
                    await AdminUnbanManaged(botClient, chatId, ct);
                    break;

                case "admin_user_kick":
                    await AdminKickManaged(botClient, chatId, ct);
                    break;

                case "admin_user_gold":
                    // Единая форма: ID уже выбран → только сумма.
                    if (!RequireManagedPlayer(chatId, out string gUid))
                    {
                        await botClient.SendMessage(chatId, "Сначала выбери игрока или открой «Выдать голду».", cancellationToken: ct);
                        break;
                    }
                    _adminTargetUid[chatId] = gUid;
                    _state[chatId] = DonateState.WaitingAdminGiveGoldAmount;
                    await botClient.SendMessage(chatId,
                        $"💰 Единая выдача голды\nИгрок: {gUid}\nСумма (целое > 0, без лимита):",
                        cancellationToken: ct);
                    break;

                case "admin_user_spins":
                    if (!RequireManagedPlayer(chatId, out string sUid))
                    {
                        await botClient.SendMessage(chatId, "Сначала выбери игрока.", cancellationToken: ct);
                        break;
                    }
                    _adminTargetUid[chatId] = sUid;
                    _state[chatId] = DonateState.WaitingAdminGiveSpinsAmount;
                    await botClient.SendMessage(chatId, "Сколько спинов (#201)?", cancellationToken: ct);
                    break;

                case "admin_user_bp":
                    if (!RequireManagedPlayer(chatId, out string bpUid))
                    {
                        await botClient.SendMessage(chatId, "Сначала выбери игрока.", cancellationToken: ct);
                        break;
                    }
                    try
                    {
                        GrantItem(bpUid, GoldPassItemDefinitionId);
                        await botClient.SendMessage(chatId, $"Gold Pass выдан `{bpUid}`.", parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        await botClient.SendMessage(chatId, "Ошибка: " + ex.Message, cancellationToken: ct);
                    }
                    break;

                case "admin_user_level":
                    _state[chatId] = DonateState.WaitingAdminSetLevelValue;
                    if (!RequireManagedPlayer(chatId, out _))
                    {
                        _state[chatId] = DonateState.WaitingAdminSetLevelUid;
                        await botClient.SendMessage(chatId, "UID игрока:", cancellationToken: ct);
                    }
                    else
                        await botClient.SendMessage(chatId, "Новый уровень аккаунта (1-600):", cancellationToken: ct);
                    break;

                case "admin_user_item":
                    if (!RequireManagedPlayer(chatId, out string itemUid))
                    {
                        _state[chatId] = DonateState.WaitingAdminGiveItemUid;
                        await botClient.SendMessage(chatId, "UID игрока:", cancellationToken: ct);
                    }
                    else
                    {
                        _adminTargetUid[chatId] = itemUid;
                        _state[chatId] = DonateState.WaitingAdminGiveItemDef;
                        await botClient.SendMessage(chatId, "Item definition id (например 141600):", cancellationToken: ct);
                    }
                    break;

                case "admin_user_info":
                    await ShowManagedPlayerInfo(botClient, chatId, ct);
                    break;

                case "admin_mmr_allies":
                    if (!RequireManagedPlayer(chatId, out _))
                    {
                        await botClient.SendMessage(chatId, "Сначала открой «Игрок» и укажи UID.", cancellationToken: ct);
                        break;
                    }
                    _adminPendingMmrMode[chatId] = "allies";
                    _state[chatId] = DonateState.WaitingAdminMmrAdjust;
                    await botClient.SendMessage(chatId,
                        "MMR Allies: `+100`, `-50` или абсолют `2300`.\nЗвание пересчитается по MMR, профиль обновится сразу.",
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                    break;

                case "admin_mmr_comp":
                    if (!RequireManagedPlayer(chatId, out _))
                    {
                        await botClient.SendMessage(chatId, "Сначала открой «Игрок» и укажи UID.", cancellationToken: ct);
                        break;
                    }
                    _adminPendingMmrMode[chatId] = "comp";
                    _state[chatId] = DonateState.WaitingAdminMmrAdjust;
                    await botClient.SendMessage(chatId,
                        "MMR Соревн.: `+100`, `-50` или абсолют `2300`.\nЗвание пересчитается по MMR, профиль обновится сразу.",
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                    break;

                case "admin_user_clearinv":
                    await ConfirmClearManagedInventory(botClient, chatId, ct);
                    break;

                case "admin_user_clearinv_yes":
                    await ClearManagedInventory(botClient, chatId, ct);
                    break;

                case "admin_clan_retag":
                    if (!_adminClanEditId.ContainsKey(chatId))
                    {
                        await botClient.SendMessage(chatId, "Сначала найди клан по тегу.", cancellationToken: ct);
                        break;
                    }
                    _state[chatId] = DonateState.WaitingAdminClanNewTag;
                    await botClient.SendMessage(chatId, "Новый тег клана:", cancellationToken: ct);
                    break;

                case "admin_clan_rename":
                    if (!_adminClanEditId.ContainsKey(chatId))
                    {
                        await botClient.SendMessage(chatId, "Сначала найди клан по тегу.", cancellationToken: ct);
                        break;
                    }
                    _state[chatId] = DonateState.WaitingAdminClanNewName;
                    await botClient.SendMessage(chatId, "Новое имя клана:", cancellationToken: ct);
                    break;
            }
        }

        private async Task HandleAdminExtraMessage(ITelegramBotClient botClient, long chatId, long userId, string text, DonateState state, CancellationToken ct)
        {
            switch (state)
            {
                case DonateState.WaitingAdminUserUid:
                    {
                        if (!TryResolvePlayerId(text, out string pid, out string err))
                        {
                            _state[chatId] = DonateState.WaitingAdminUserUid;
                            await botClient.SendMessage(chatId, "Не найден: " + err + "\nВведи UID ещё раз:", cancellationToken: ct);
                            return;
                        }
                        _state[chatId] = DonateState.None;
                        _adminManagedPlayerId[chatId] = pid;
                        await ShowPlayerManageMenu(botClient, chatId, pid, text.Trim(), ct);
                        break;
                    }

                case DonateState.WaitingAdminBanReason:
                    {
                        _state[chatId] = DonateState.None;
                        if (!RequireManagedPlayer(chatId, out string pid))
                        {
                            await botClient.SendMessage(chatId, "Игрок не выбран.", cancellationToken: ct);
                            return;
                        }
                        string reason = string.IsNullOrWhiteSpace(text) || text == "-" ? "Banned by admin" : text.Trim();
                        try
                        {
                            var oid = ObjectId.Parse(pid);
                            BoltMainDatabaseProvider.Instance.BanPlayer(oid, reason, 1);
                            var doc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(oid);
                            StaticClasses.KickWithBan(pid, reason, 1, doc?.uid ?? pid);
                            await botClient.SendMessage(chatId, $"Забанен `{pid}`\nПричина: {reason}", parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                        }
                        catch (Exception ex)
                        {
                            await botClient.SendMessage(chatId, "Ошибка бана: " + ex.Message, cancellationToken: ct);
                        }
                        break;
                    }

                case DonateState.WaitingAdminClanTag:
                    {
                        string tag = (text ?? "").Trim();
                        var clans = BoltMainDatabaseProvider.Instance.FindClanToTag(tag);
                        if (clans == null || clans.Length == 0)
                        {
                            // точный тег
                            clans = BoltMainDatabaseProvider.Instance.FindClanToTag("^" + System.Text.RegularExpressions.Regex.Escape(tag) + "$");
                        }
                        if (clans == null || clans.Length == 0)
                        {
                            _state[chatId] = DonateState.WaitingAdminClanTag;
                            await botClient.SendMessage(chatId, $"Клан с тегом `{tag}` не найден.\nВведи тег ещё раз:", parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                            return;
                        }
                        _state[chatId] = DonateState.None;
                        var clan = clans[0];
                        _adminClanEditId[chatId] = clan.Id;
                        _adminClanOldTag[chatId] = clan.Tag ?? tag;
                        var kb = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("Сменить тег", "admin_clan_retag"), InlineKeyboardButton.WithCallbackData("Сменить имя", "admin_clan_rename") },
                            new[] { InlineKeyboardButton.WithCallbackData("« Назад", "menu_admin") },
                        });
                        await botClient.SendMessage(chatId,
                            $"Клан найден\nID: `{clan.Id}`\nТег: `{clan.Tag}`\nИмя: {clan.Name}\nУчастников: {clan.MebersCount}/{clan.MaxMemberCount}",
                            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                            replyMarkup: kb, cancellationToken: ct);
                        break;
                    }

                case DonateState.WaitingAdminClanNewTag:
                    {
                        if (!_adminClanEditId.TryGetValue(chatId, out string clanId))
                        {
                            _state[chatId] = DonateState.None;
                            await botClient.SendMessage(chatId, "Сессия клана потеряна.", cancellationToken: ct);
                            return;
                        }
                        string newTag = (text ?? "").Trim();
                        if (newTag.Length < 2 || newTag.Length > 8)
                        {
                            _state[chatId] = DonateState.WaitingAdminClanNewTag;
                            await botClient.SendMessage(chatId, "Тег 2-8 символов. Введи ещё раз:", cancellationToken: ct);
                            return;
                        }
                        _state[chatId] = DonateState.None;
                        try
                        {
                            string oldTag = _adminClanOldTag.GetValueOrDefault(chatId, "");
                            var clan = new Axlebolt.Bolt.Protobuf.Clan { Id = clanId, Tag = oldTag, Name = oldTag };
                            // подтянуть имя если есть
                            try
                            {
                                var found = BoltMainDatabaseProvider.Instance.FindClanToTag(oldTag);
                                var match = found?.FirstOrDefault(c => c.Id == clanId) ?? found?.FirstOrDefault();
                                if (match != null)
                                {
                                    clan.Name = match.Name ?? oldTag;
                                    clan.Tag = match.Tag ?? oldTag;
                                }
                            }
                            catch { }
                            string keepName = string.IsNullOrWhiteSpace(clan.Name) ? oldTag : clan.Name;
                            BoltMainDatabaseProvider.Instance.RenameClan(clan, newTag, keepName);
                            try { BoltMainDatabaseProvider.Instance.UpdateClanLogsTag(clan.Tag ?? oldTag, newTag); } catch { }
                            _adminClanOldTag[chatId] = newTag;
                            await botClient.SendMessage(chatId, $"Тег клана: `{oldTag}` → `{newTag}`", parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                        }
                        catch (Exception ex)
                        {
                            await botClient.SendMessage(chatId, "Ошибка: " + ex.Message, cancellationToken: ct);
                        }
                        break;
                    }

                case DonateState.WaitingAdminClanNewName:
                    {
                        if (!_adminClanEditId.TryGetValue(chatId, out string clanId))
                        {
                            _state[chatId] = DonateState.None;
                            await botClient.SendMessage(chatId, "Сессия клана потеряна.", cancellationToken: ct);
                            return;
                        }
                        string newName = (text ?? "").Trim();
                        if (newName.Length < 2 || newName.Length > 32)
                        {
                            _state[chatId] = DonateState.WaitingAdminClanNewName;
                            await botClient.SendMessage(chatId, "Имя 2-32 символа. Введи ещё раз:", cancellationToken: ct);
                            return;
                        }
                        _state[chatId] = DonateState.None;
                        try
                        {
                            string tag = _adminClanOldTag.GetValueOrDefault(chatId, "TAG");
                            var clan = new Axlebolt.Bolt.Protobuf.Clan { Id = clanId, Tag = tag, Name = newName };
                            BoltMainDatabaseProvider.Instance.RenameClan(clan, tag, newName);
                            await botClient.SendMessage(chatId, $"Имя клана [{tag}] → {newName}", cancellationToken: ct);
                        }
                        catch (Exception ex)
                        {
                            await botClient.SendMessage(chatId, "Ошибка: " + ex.Message, cancellationToken: ct);
                        }
                        break;
                    }

                case DonateState.WaitingAdminSetLevelUid:
                    {
                        if (!TryResolvePlayerId(text, out string pid, out string err))
                        {
                            await botClient.SendMessage(chatId, "Не найден: " + err, cancellationToken: ct);
                            return;
                        }
                        _adminManagedPlayerId[chatId] = pid;
                        _state[chatId] = DonateState.WaitingAdminSetLevelValue;
                        await botClient.SendMessage(chatId, "Новый уровень (1-600):", cancellationToken: ct);
                        break;
                    }

                case DonateState.WaitingAdminSetLevelValue:
                    {
                        if (!int.TryParse(text, out int lvl) || lvl < 1 || lvl > 600)
                        {
                            _state[chatId] = DonateState.WaitingAdminSetLevelValue;
                            await botClient.SendMessage(chatId, "Уровень 1-600. Введи ещё раз:", cancellationToken: ct);
                            return;
                        }
                        if (!RequireManagedPlayer(chatId, out string pid))
                        {
                            _state[chatId] = DonateState.None;
                            await botClient.SendMessage(chatId, "Игрок не выбран.", cancellationToken: ct);
                            return;
                        }
                        _state[chatId] = DonateState.None;
                        BoltGameDatabaseProvider.Instance.SetPlayerStat(pid, "level_id", lvl);
                        BoltGameDatabaseProvider.Instance.SetPlayerStat(pid, "level_xp", 0);
                        try { PlayerStatsRemoteService.InvalidateStatsCachePublic(pid); } catch { }
                        await botClient.SendMessage(chatId, $"Уровень `{pid}` → {lvl}", parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                        break;
                    }

                case DonateState.WaitingAdminGiveItemUid:
                    {
                        if (!TryResolvePlayerId(text, out string pid, out string err))
                        {
                            await botClient.SendMessage(chatId, "Не найден: " + err, cancellationToken: ct);
                            return;
                        }
                        _adminTargetUid[chatId] = pid;
                        _adminManagedPlayerId[chatId] = pid;
                        _state[chatId] = DonateState.WaitingAdminGiveItemDef;
                        await botClient.SendMessage(chatId, "Item definition id:", cancellationToken: ct);
                        break;
                    }

                case DonateState.WaitingAdminGiveItemDef:
                    {
                        if (!int.TryParse(text?.Trim(), out int defId) || defId <= 0)
                        {
                            _state[chatId] = DonateState.WaitingAdminGiveItemDef;
                            await botClient.SendMessage(chatId, "Неверный defId. Введи число ещё раз:", cancellationToken: ct);
                            return;
                        }
                        _state[chatId] = DonateState.None;
                        string pid = _adminTargetUid.GetValueOrDefault(chatId) ?? _adminManagedPlayerId.GetValueOrDefault(chatId);
                        if (string.IsNullOrEmpty(pid))
                        {
                            await botClient.SendMessage(chatId, "Игрок не выбран.", cancellationToken: ct);
                            return;
                        }
                        try
                        {
                            GrantItem(pid, defId);
                            await botClient.SendMessage(chatId, $"Предмет #{defId} выдан `{pid}`.", parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                        }
                        catch (Exception ex)
                        {
                            await botClient.SendMessage(chatId, "Ошибка: " + ex.Message, cancellationToken: ct);
                        }
                        break;
                    }

                case DonateState.WaitingAdminKickUid:
                    {
                        if (!TryResolvePlayerId(text, out string pid, out string err))
                        {
                            _state[chatId] = DonateState.WaitingAdminKickUid;
                            await botClient.SendMessage(chatId, "Не найден: " + err + "\nВведи UID ещё раз:", cancellationToken: ct);
                            return;
                        }
                        _state[chatId] = DonateState.None;
                        _adminManagedPlayerId[chatId] = pid;
                        await AdminKickManaged(botClient, chatId, ct);
                        break;
                    }

                case DonateState.WaitingAdminRankMmr:
                    {
                        if (!_adminPendingRank.TryGetValue(chatId, out var pending) || pending == null)
                        {
                            _state[chatId] = DonateState.None;
                            await botClient.SendMessage(chatId, "Сессия выдачи звания потеряна.", cancellationToken: ct);
                            return;
                        }
                        string mmrText = (text ?? "").Trim();
                        if (mmrText == "-" || string.IsNullOrEmpty(mmrText))
                            pending.Mmr = null;
                        else if (float.TryParse(mmrText.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float mmrVal))
                            pending.Mmr = mmrVal;
                        else
                        {
                            await botClient.SendMessage(chatId, "MMR: число или `-` для авто.", cancellationToken: ct);
                            return;
                        }
                        _adminPendingRank[chatId] = pending;
                        _state[chatId] = DonateState.WaitingAdminRankWins;
                        await botClient.SendMessage(chatId,
                            "Сколько побед выдать? (число, по умолчанию 100)\nМожно `100 30` — побед и поражений.\nДалее спросим K/D.",
                            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                        break;
                    }

                case DonateState.WaitingAdminRankWins:
                    {
                        if (!_adminPendingRank.TryGetValue(chatId, out var pending) || pending == null)
                        {
                            _state[chatId] = DonateState.None;
                            await botClient.SendMessage(chatId, "Сессия выдачи звания потеряна.", cancellationToken: ct);
                            return;
                        }
                        string winsText = (text ?? "").Trim();
                        if (string.IsNullOrEmpty(winsText))
                        {
                            pending.Wins = 100;
                            pending.Losses = 30;
                        }
                        else
                        {
                            var parts = winsText.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (!int.TryParse(parts[0], out int w) || w < 0)
                            {
                                await botClient.SendMessage(chatId, "Победы: число >= 0.", cancellationToken: ct);
                                return;
                            }
                            pending.Wins = w;
                            pending.Losses = parts.Length > 1 && int.TryParse(parts[1], out int l) ? Math.Max(0, l) : Math.Max(0, w / 3);
                        }
                        _adminPendingRank[chatId] = pending;
                        _state[chatId] = DonateState.WaitingAdminRankKd;
                        await botClient.SendMessage(chatId,
                            "K/D — введи одним из способов:\n" +
                            "• `1.25` — коэффициент K/D (смерти ≈ победы)\n" +
                            "• `1500 1000` — убийства и смерти\n" +
                            "• `-` — не менять",
                            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                        break;
                    }

                case DonateState.WaitingAdminRankKd:
                    {
                        _state[chatId] = DonateState.None;
                        if (!_adminPendingRank.TryGetValue(chatId, out var pending) || pending == null)
                        {
                            await botClient.SendMessage(chatId, "Сессия выдачи звания потеряна.", cancellationToken: ct);
                            return;
                        }
                        string kdText = (text ?? "").Trim();
                        if (!string.IsNullOrEmpty(kdText) && kdText != "-")
                        {
                            var parts = kdText.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && int.TryParse(parts[0], out int k) && int.TryParse(parts[1], out int d) && k >= 0 && d > 0)
                            {
                                pending.Kills = k;
                                pending.Deaths = d;
                            }
                            else if (float.TryParse(kdText.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float kd) && kd > 0f)
                            {
                                int deaths = Math.Max(1, pending.Losses ?? Math.Max(1, (pending.Wins ?? 100) / 3));
                                pending.Deaths = deaths;
                                pending.Kills = Math.Max(0, (int)Math.Round(deaths * kd));
                            }
                            else
                            {
                                await botClient.SendMessage(chatId, "K/D: `1.25` или `1500 1000`, либо `-`.", parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                                _state[chatId] = DonateState.WaitingAdminRankKd;
                                return;
                            }
                        }
                        _adminPendingRank[chatId] = pending;
                        await ApplyPendingRankGrant(botClient, chatId, ct);
                        break;
                    }

                case DonateState.WaitingAdminMmrAdjust:
                    {
                        _state[chatId] = DonateState.None;
                        if (!RequireManagedPlayer(chatId, out string pid))
                        {
                            await botClient.SendMessage(chatId, "Игрок не выбран.", cancellationToken: ct);
                            break;
                        }
                        if (!_adminPendingMmrMode.TryRemove(chatId, out string mode) || string.IsNullOrEmpty(mode))
                        {
                            await botClient.SendMessage(chatId, "Сессия MMR потеряна.", cancellationToken: ct);
                            break;
                        }
                        string raw = (text ?? "").Trim().Replace(',', '.');
                        if (string.IsNullOrEmpty(raw))
                        {
                            await botClient.SendMessage(chatId, "Введи MMR: +100, -50 или 2300.", cancellationToken: ct);
                            break;
                        }
                        try
                        {
                            var db = BoltGameDatabaseProvider.Instance;
                            if (raw.StartsWith("+") || raw.StartsWith("-"))
                            {
                                if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float delta))
                                {
                                    await botClient.SendMessage(chatId, "Неверный формат delta.", cancellationToken: ct);
                                    break;
                                }
                                if (mode == "comp")
                                    PlayerStatsManager.AdjustCompetitiveMmr(pid, delta);
                                else
                                    PlayerStatsManager.AdjustAlliesMmr(pid, delta);
                            }
                            else if (float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float absMmr))
                            {
                                if (mode == "comp")
                                    PlayerStatsManager.ApplyCompetitiveMmr(pid, absMmr);
                                else
                                    PlayerStatsManager.ApplyAlliesMmr(pid, absMmr);
                            }
                            else
                            {
                                await botClient.SendMessage(chatId, "Неверный формат. Пример: +100 или 2300.", cancellationToken: ct);
                                break;
                            }

                            int mmr = mode == "comp"
                                ? (int)db.GetPlayerStat(pid, "ranked_current_mmr")
                                : (int)db.GetPlayerStat(pid, "ranked_2v2_current_mmr");
                            int rank = mode == "comp"
                                ? (int)db.GetPlayerStat(pid, "ranked_rank")
                                : (int)db.GetPlayerStat(pid, "ranked_2v2_rank");
                            string modeLabel = mode == "comp" ? "Соревновательный" : "Allies";
                            await botClient.SendMessage(chatId,
                                $"✅ {modeLabel}\nMMR: {mmr}\nЗвание: {PlayerStatsManager.GetAlliesRankName(rank)} (#{rank})\nПрофиль обновлён.",
                                cancellationToken: ct);
                        }
                        catch (Exception ex)
                        {
                            await botClient.SendMessage(chatId, "Ошибка MMR: " + ex.Message, cancellationToken: ct);
                        }
                        break;
                    }

                case DonateState.WaitingAdminCustomId:
                    {
                        _state[chatId] = DonateState.None;
                        if (!RequireManagedPlayer(chatId, out string pid))
                        {
                            await botClient.SendMessage(chatId, "Игрок не выбран.", cancellationToken: ct);
                            return;
                        }
                        string newId = (text ?? "").Trim();
                        if (newId.Length < 3 || newId.Length > 16)
                        {
                            await botClient.SendMessage(chatId, "Custom ID: 3-16 символов.", cancellationToken: ct);
                            return;
                        }
                        if (!System.Text.RegularExpressions.Regex.IsMatch(newId, "^[A-Za-z0-9_]+$"))
                        {
                            await botClient.SendMessage(chatId, "Custom ID: только латиница, цифры и знак подчёркивания.", cancellationToken: ct);
                            return;
                        }
                        bool ok = TrySetCustomId(pid, newId, out string reason);
                        // Ответ без Markdown: сам ID может содержать «_», и разметка снова ломает отправку.
                        if (ok)
                            await botClient.SendMessage(chatId, $"Custom ID изменён на {newId} для {pid}.", cancellationToken: ct);
                        else
                            await botClient.SendMessage(chatId, "Не удалось: " + reason, cancellationToken: ct);
                        break;
                    }
            }
        }

        private bool RequireManagedPlayer(long chatId, out string playerId)
        {
            return _adminManagedPlayerId.TryGetValue(chatId, out playerId) && !string.IsNullOrEmpty(playerId);
        }

        private async Task ConfirmClearManagedInventory(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            if (!RequireManagedPlayer(chatId, out string playerId))
            {
                await botClient.SendMessage(chatId, "Сначала открой «Игрок» и укажи UID.", cancellationToken: ct);
                return;
            }
            var kb = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Да, очистить всё", "admin_user_clearinv_yes") },
                new[] { InlineKeyboardButton.WithCallbackData("« Отмена", "admin_user_info") },
            });
            await botClient.SendMessage(chatId,
                $"Очистить инвентарь игрока `{playerId}`? Это удалит все предметы.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                replyMarkup: kb, cancellationToken: ct);
        }

        private async Task ClearManagedInventory(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            if (!RequireManagedPlayer(chatId, out string playerId))
            {
                await botClient.SendMessage(chatId, "Сначала открой «Игрок» и укажи UID.", cancellationToken: ct);
                return;
            }
            try
            {
                if (!ObjectId.TryParse(playerId, out ObjectId oid))
                {
                    await botClient.SendMessage(chatId, "Неверный playerId.", cancellationToken: ct);
                    return;
                }
                bool ok = InventoryRemoteEventListener.ClearPlayerAndNotify(oid);
                ModActionLog.Log(chatId, null, "clear_inventory", $"playerId={playerId}");
                await botClient.SendMessage(chatId,
                    ok ? "Инвентарь очищен." : "Документ инвентаря не найден.",
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, "Ошибка очистки: " + ex.Message, cancellationToken: ct);
            }
        }

        private async Task ShowPlayerManageMenu(ITelegramBotClient botClient, long chatId, string playerId, string input, CancellationToken ct)
        {
            string uid = input;
            try
            {
                var doc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(playerId));
                if (doc != null) uid = doc.uid ?? doc.name ?? input;
            }
            catch { }

            var kb = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Инфо", "admin_user_info"), InlineKeyboardButton.WithCallbackData("Звание Allies", "admin_rank_menu") },
                new[] { InlineKeyboardButton.WithCallbackData("MMR Allies", "admin_mmr_allies"), InlineKeyboardButton.WithCallbackData("MMR Соревн.", "admin_mmr_comp") },
                new[] { InlineKeyboardButton.WithCallbackData("Звание Соревн.", "admin_rank_comp_menu"), InlineKeyboardButton.WithCallbackData("Custom ID", "admin_user_customid") },
                new[] { InlineKeyboardButton.WithCallbackData("Бан", "admin_user_ban"), InlineKeyboardButton.WithCallbackData("Разбан", "admin_user_unban"), InlineKeyboardButton.WithCallbackData("Кик", "admin_user_kick") },
                new[] { InlineKeyboardButton.WithCallbackData("💰 Голда (единая форма)", "admin_user_gold"), InlineKeyboardButton.WithCallbackData("Спины", "admin_user_spins") },
                new[] { InlineKeyboardButton.WithCallbackData("Gold Pass", "admin_user_bp"), InlineKeyboardButton.WithCallbackData("Уровень", "admin_user_level") },
                new[] { InlineKeyboardButton.WithCallbackData("Предмет", "admin_user_item") },
                new[] { InlineKeyboardButton.WithCallbackData("🗑 Очистить инвентарь", "admin_user_clearinv") },
                new[] { InlineKeyboardButton.WithCallbackData("« Админка", "menu_admin") },
            });
            await botClient.SendMessage(chatId,
                $"Игрок: `{uid}`\nId: `{playerId}`",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                replyMarkup: kb, cancellationToken: ct);
        }

        private async Task BeginRankGrantFlow(ITelegramBotClient botClient, long chatId, CancellationToken ct, string mode, int rank)
        {
            if (!_adminManagedPlayerId.TryGetValue(chatId, out string pid) || string.IsNullOrEmpty(pid))
            {
                await botClient.SendMessage(chatId, "Сначала открой «Игрок» и укажи UID.", cancellationToken: ct);
                return;
            }
            _adminPendingRank[chatId] = new PendingRankGrant { Mode = mode, Rank = rank, PlayerId = pid };
            _state[chatId] = DonateState.WaitingAdminRankMmr;
            string modeLabel = mode == "comp" ? "Соревновательный" : "Allies";
            string rankLabel = PlayerStatsManager.GetAlliesRankName(rank);
            float suggestMmr = rank < 0 ? AlliesMmrSystem.DefaultMmr : AlliesMmrSystem.MidMmrForRank(rank);
            await botClient.SendMessage(chatId,
                $"{modeLabel}: {rankLabel} (#{rank})\n" +
                $"MMR (рекомендуется {(int)suggestMmr}, или `-` для авто).\n" +
                $"Итоговое звание = по MMR (RankFromMmr).",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
        }

        private async Task ApplyPendingRankGrant(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            if (!_adminPendingRank.TryRemove(chatId, out var pending) || pending == null)
            {
                await botClient.SendMessage(chatId, "Сессия выдачи звания потеряна.", cancellationToken: ct);
                return;
            }
            try
            {
                int wins = pending.Wins ?? 100;
                int losses = pending.Losses ?? 30;
                if (pending.Mode == "comp")
                    PlayerStatsManager.SetCompetitiveRank(pending.PlayerId, pending.Rank, pending.Mmr, wins, losses, pending.Kills, pending.Deaths);
                else
                    PlayerStatsManager.SetAlliesRank(pending.PlayerId, pending.Rank, pending.Mmr, wins, losses, pending.Kills, pending.Deaths);

                try
                {
                    string grantLog = Path.Combine(AppContext.BaseDirectory ?? ".", "bot_allies_rank_grants.log");
                    File.AppendAllText(grantLog, $"{DateTime.UtcNow:o}\t{pending.PlayerId}\t{pending.Mode}\t{pending.Rank}\tmmr={pending.Mmr}\tw={wins}\n");
                }
                catch { }

                var db = BoltGameDatabaseProvider.Instance;
                string modeLabel = pending.Mode == "comp" ? "Соревновательный" : "Allies";
                int mmrShown = pending.Mode == "comp"
                    ? (int)db.GetPlayerStat(pending.PlayerId, "ranked_current_mmr")
                    : (int)db.GetPlayerStat(pending.PlayerId, "ranked_2v2_current_mmr");
                int rankShown = pending.Mode == "comp"
                    ? (int)db.GetPlayerStat(pending.PlayerId, "ranked_rank")
                    : (int)db.GetPlayerStat(pending.PlayerId, "ranked_2v2_rank");
                string kdLine = pending.Kills.HasValue && pending.Deaths.HasValue
                    ? $"\nK/D: {pending.Kills}/{pending.Deaths} ({(pending.Deaths.Value > 0 ? (float)pending.Kills.Value / pending.Deaths.Value : 0f):0.00})"
                    : "";
                await botClient.SendMessage(chatId,
                    $"✅ {modeLabel}\n" +
                    $"Звание: {PlayerStatsManager.GetAlliesRankName(rankShown)} (#{rankShown})\n" +
                    $"MMR: {mmrShown}\n" +
                    $"Побед: {wins}{kdLine}\n" +
                    $"Игрок: `{pending.PlayerId}`",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, "Ошибка: " + ex.Message, cancellationToken: ct);
            }
        }

        private async Task ShowRankGrantKeyboard(ITelegramBotClient botClient, long chatId, string mode, CancellationToken ct)
        {
            bool isComp = mode == "comp";
            string prefix = isComp ? "admin_rank_comp:" : "admin_rank:";
            var rows = new List<InlineKeyboardButton[]>();
            for (int r = 0; r <= 16; r += 2)
            {
                var row = new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData(PlayerStatsManager.GetRankButtonLabel(r), $"{prefix}{r}")
                };
                if (r + 1 <= 16)
                    row.Add(InlineKeyboardButton.WithCallbackData(PlayerStatsManager.GetRankButtonLabel(r + 1), $"{prefix}{r + 1}"));
                rows.Add(row.ToArray());
            }
            if (!isComp)
            {
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Calib (-1)", $"{prefix}-1") });
                var calibRow = new List<InlineKeyboardButton>();
                for (int i = 1; i <= 9; i++)
                {
                    calibRow.Add(InlineKeyboardButton.WithCallbackData($"{i}", $"admin_calib:{i}"));
                    if (i == 5 || i == 9)
                    {
                        rows.Add(calibRow.ToArray());
                        calibRow = new List<InlineKeyboardButton>();
                    }
                }
            }
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("« Назад", "menu_admin") });
            string who = _adminManagedPlayerId.GetValueOrDefault(chatId, "?");
            string title = isComp ? "Соревновательный 5v5" : "Allies 2v2";
            await botClient.SendMessage(chatId,
                $"Звание ({title}) для `{who}`\nПосле выбора укажешь MMR и число побед.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowManagedPlayerInfo(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            if (!RequireManagedPlayer(chatId, out string pid))
            {
                await botClient.SendMessage(chatId, "Игрок не выбран.", cancellationToken: ct);
                return;
            }
            try
            {
                var doc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(pid));
                var db = BoltGameDatabaseProvider.Instance;
                int lvl = (int)db.GetPlayerStat(pid, "level_id");
                int w = (int)db.GetPlayerStat(pid, "ranked_2v2_calibration_match_count");
                int l = (int)db.GetPlayerStat(pid, "allies_losses");
                int k = (int)db.GetPlayerStat(pid, "allies_kills");
                int d = (int)db.GetPlayerStat(pid, "allies_deaths");
                int alliesRank = (int)db.GetPlayerStat(pid, "ranked_2v2_rank");
                int alliesMmr = (int)db.GetPlayerStat(pid, "ranked_2v2_current_mmr");
                int compRank = (int)db.GetPlayerStat(pid, "ranked_rank");
                int compMmr = (int)db.GetPlayerStat(pid, "ranked_current_mmr");
                long gold = 0;
                try
                {
                    var inv = db.GetPlayerInventoryDocument(ObjectId.Parse(pid));
                    if (inv?.Currencies != null && inv.Currencies.Contains("102"))
                        gold = inv.Currencies["102"].AsInt64;
                }
                catch { }
                await botClient.SendMessage(chatId,
                    $"UID: `{doc?.uid}`\nName: {doc?.name}\nLevel: {lvl}\nGold: {gold}\n" +
                    $"Allies: {PlayerStatsManager.GetAlliesRankName(alliesRank)} | MMR {alliesMmr} | W{w}/L{l} | KD {k}/{d}\n" +
                    $"Соревн.: {PlayerStatsManager.GetAlliesRankName(compRank)} | MMR {compMmr}\n" +
                    $"Banned: {(doc != null && doc.isBanned ? "yes" : "no")}",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, "Ошибка инфо: " + ex.Message, cancellationToken: ct);
            }
        }

        private async Task AdminUnbanManaged(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            if (!RequireManagedPlayer(chatId, out string pid))
            {
                await botClient.SendMessage(chatId, "Игрок не выбран.", cancellationToken: ct);
                return;
            }
            try
            {
                BoltMainDatabaseProvider.Instance.UnbanPlayer(ObjectId.Parse(pid));
                await botClient.SendMessage(chatId, $"Разбанен `{pid}`", parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, "Ошибка: " + ex.Message, cancellationToken: ct);
            }
        }

        private async Task AdminKickManaged(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            if (!RequireManagedPlayer(chatId, out string pid))
            {
                await botClient.SendMessage(chatId, "Игрок не выбран.", cancellationToken: ct);
                return;
            }
            try
            {
                if (StaticClasses.UserServices.TryGetValue(pid, out var userService))
                {
                    userService.ForceDisconnect();
                    await botClient.SendMessage(chatId, $"Кикнут `{pid}` (онлайн)", parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
                }
                else
                    await botClient.SendMessage(chatId, $"Игрок `{pid}` не онлайн.", parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, "Ошибка кика: " + ex.Message, cancellationToken: ct);
            }
        }

        private async Task ShowCompetitiveMatchmakingMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            CompetitiveMatchmakingConfig.Refresh();
            int current = CompetitiveMatchmakingConfig.GetRequiredPlayers();
            bool is1v1 = current == CompetitiveMatchmakingConfig.Solo;

            var rows = new List<InlineKeyboardButton[]>
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        is1v1 ? "✅ 1v1 (2 игрока)" : "1v1 (2 игрока)",
                        "admin_comp_set:2"),
                    InlineKeyboardButton.WithCallbackData(
                        !is1v1 ? "✅ 5v5 (10 игроков)" : "5v5 (10 игроков)",
                        "admin_comp_set:10")
                },
                new[] { InlineKeyboardButton.WithCallbackData("« Админка", "menu_admin") }
            };

            await botClient.SendMessage(chatId,
                "🏆 Соревновательный — матчмейкинг\n\n" +
                $"Текущий режим: {CompetitiveMatchmakingConfig.ModeLabel()}\n" +
                $"Старт матча при: {current} игроках в очереди\n\n" +
                "• 1v1 — только соло-поиск, отряды в очередь не пускаются\n" +
                "• 5v5 — до 5 в отряде, как было\n" +
                "• MMR, confirm, карты и история работают одинаково в обоих",
                replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowAlliesMatchmakingMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            AlliesMatchmakingConfig.Refresh();
            int current = AlliesMatchmakingConfig.GetRequiredPlayers();
            bool is1v1 = current == 2;

            var rows = new List<InlineKeyboardButton[]>
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        is1v1 ? "✅ 1v1 (2 игрока)" : "1v1 (2 игрока)",
                        "admin_allies_set:2"),
                    InlineKeyboardButton.WithCallbackData(
                        !is1v1 ? "✅ 2v2 (4 игрока)" : "2v2 (4 игрока)",
                        "admin_allies_set:4")
                },
                new[] { InlineKeyboardButton.WithCallbackData("« Админка", "menu_admin") }
            };

            await botClient.SendMessage(chatId,
                "⚔️ Союзники — матчмейкинг\n\n" +
                $"Текущий режим: {AlliesMatchmakingConfig.ModeLabel()}\n" +
                $"Старт матча при: {current} игроках в очереди\n\n" +
                "• 1v1 — соло-поиск, все функции (MMR, confirm, история, пауза/сдача)\n" +
                "• 2v2 — до 4 в отряде, дуо/полный стек\n" +
                "• AFK-боты отключены навсегда\n" +
                "• 1 или 3 игрока без ботов матч не начнут",
                replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }
    }
}
