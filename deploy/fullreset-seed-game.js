// Minimal game DB seed after full StandRise drop (port 1337).
var d = db.getSiblingDB("StandRise");

d.getCollection("inventory_starter_pack").deleteMany({});
d.getCollection("inventory_starter_pack").insertOne({
  items: [],
  currencies: { "101": { value: 0 }, "102": { value: 0 } }
});
print("inventory_starter_pack: empty items, 0 spins (#201 not in pack)");

var marketId = ObjectId("0000000000000000000000f1");
d.getCollection("market_admin").replaceOne(
  { _id: marketId },
  { _id: marketId, marketClosed: false, hiddenCollections: [], marketWhitelistPlayerIds: [] },
  { upsert: true }
);

var mmId = ObjectId("0000000000000000000000f2");
d.getCollection("matchmaking_admin").replaceOne(
  { _id: mmId },
  { _id: mmId, alliesRequiredPlayers: 2, competitiveRequiredPlayers: 2 },
  { upsert: true }
);

var spinId = ObjectId("0000000000000000000000f3");
d.getCollection("spin_admin").replaceOne(
  { _id: spinId },
  { _id: spinId, enabled: true, startUnixMs: NumberLong(0), endUnixMs: NumberLong(0), arcaneBoost: false },
  { upsert: true }
);

d.getCollection("match_counters").replaceOne(
  { _id: "public_match_seq" },
  { _id: "public_match_seq", seq: NumberInt(1000) },
  { upsert: true }
);

print("seed-game done. collections:");
d.getCollectionNames().forEach(function(c) {
  print("  " + c + " = " + d.getCollection(c).countDocuments({}));
});
