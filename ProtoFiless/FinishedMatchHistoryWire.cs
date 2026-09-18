using pb = global::Google.Protobuf;

namespace Axlebolt.Bolt.Protobuf
{
    public sealed partial class FinishedMatch
    {
        private bool _includeTeamGroupsForDetail;

        public void EnsureViewerPlayerRow(string viewerId, string viewerUid, string viewerName, string avatar)
        {
            EnsureViewerPlayerRow(viewerId, viewerUid, viewerName);
        }

        public void AddPlayerRow(string id, string name, System.Collections.Generic.List<StatValueRow> stats,
            System.Collections.Generic.List<DroppedItem> drops, string uid, string avatar)
        {
            AddPlayerRow(id, name, stats, drops, uid);
        }

        public static string SanitizeAvatarId(string avatar)
        {
            if (string.IsNullOrWhiteSpace(avatar)) return "0";
            avatar = avatar.Trim();
            return avatar.Length > 32 ? avatar.Substring(0, 32) : avatar;
        }

        public void UpsertOverallIntPublic(string name, int value)
        {
            RemoveOverallStat(name);
            AddOverallStat(new StatValueRow { Name = name, Type = 0, IntValue = value, LongValue = value });
        }

        public void UpsertOverallSigned(string name, int value)
        {
            UpsertOverallIntPublic(name, value);
        }

        public void UpsertOverallStringPublic(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) return;
            RemoveOverallStat(name);
            AddOverallStat(new StatValueRow { Name = name, Type = 2, StringValue = value ?? "" });
        }

        public bool IsClientCanceled =>
            GetOverallInt("is_give_up") != 0
            || GetOverallInt("canceled") != 0
            || string.Equals(GetOverallString("status"), "canceled", System.StringComparison.OrdinalIgnoreCase);

        public void CompactForHistoryList()
        {
            EnsureMapLevelForClient();
            UseStructuredWire();
            NormalizeClientDates();
        }

        public void UpsertOverallXp(long xp)
        {
            RemoveOverallStat("xp");
            AddOverallStat(new StatValueRow { Name = "xp", Type = 4, LongValue = xp, IntValue = (int)System.Math.Clamp(xp, int.MinValue, int.MaxValue) });
        }

        public void AttachViewerDrop(string playerId, int itemDefinitionId)
        {
            if (itemDefinitionId <= 0) return;
            for (int i = 0; i < _playerRowIds.Count; i++)
            {
                if (!string.Equals(_playerRowIds[i], playerId, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                while (_playerRowDrops.Count <= i)
                    _playerRowDrops.Add(new System.Collections.Generic.List<DroppedItem>());
                _playerRowDrops[i].Add(new DroppedItem
                {
                    ItemId = itemDefinitionId.ToString(),
                    Count = 1
                });
                return;
            }
            AddOverallStat(new StatValueRow
            {
                Name = "drop_item",
                Type = 5,
                StringValue = itemDefinitionId.ToString()
            });
        }

        public void IncludeTeamGroupsForDetail()
        {
            _includeTeamGroupsForDetail = true;
        }

        public void CopyRewardsOnto(FinishedMatch target)
        {
            if (target == null) return;
            foreach (var name in new[] { "gold", "silver", "xp", "drop_item", "win", "result" })
            {
                int iv = GetOverallInt(name);
                if (iv != 0) target.UpsertOverallIntPublic(name, iv);
                string sv = GetOverallString(name);
                if (!string.IsNullOrEmpty(sv))
                    target.AddOverallStat(new StatValueRow { Name = name, Type = 2, StringValue = sv });
            }
        }

        public void BakeWirePayload()
        {
            UseStructuredWire();
            using (var ms = new System.IO.MemoryStream())
            {
                using (var output = new pb::CodedOutputStream(ms, true))
                {
                    WriteInnerTo(output);
                    output.Flush();
                }
                WirePayload = ms.ToArray();
            }
        }

        private void RemoveOverallStat(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            for (int i = _overallStats.Count - 1; i >= 0; i--)
            {
                var s = _overallStats[i];
                if (s != null && string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    _overallStats.RemoveAt(i);
            }
        }

        public sealed class PlayerRowSnapshot
        {
            public string Id;
            public string Uid;
            public string Name;
            public string Avatar;
            private readonly System.Collections.Generic.List<StatValueRow> _stats;

            public PlayerRowSnapshot(string id, string uid, string name, string avatar,
                System.Collections.Generic.List<StatValueRow> stats)
            {
                Id = id;
                Uid = uid;
                Name = name;
                Avatar = avatar;
                _stats = stats ?? new System.Collections.Generic.List<StatValueRow>();
            }

            public int GetStat(string name)
            {
                if (string.IsNullOrEmpty(name)) return 0;
                foreach (var s in _stats)
                {
                    if (s == null || string.IsNullOrEmpty(s.Name)) continue;
                    if (!string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (s.IntValue != 0) return s.IntValue;
                    if (s.LongValue != 0) return (int)System.Math.Clamp(s.LongValue, int.MinValue, int.MaxValue);
                }
                return 0;
            }
        }

        public System.Collections.Generic.IEnumerable<PlayerRowSnapshot> SnapshotPlayerRows()
        {
            for (int i = 0; i < _playerRowIds.Count; i++)
            {
                yield return new PlayerRowSnapshot(
                    _playerRowIds[i],
                    i < _playerRowUids.Count ? _playerRowUids[i] : "",
                    i < _playerRowNames.Count ? _playerRowNames[i] : "",
                    "0",
                    i < _playerRowStats.Count ? _playerRowStats[i] : null);
            }
        }
    }
}
