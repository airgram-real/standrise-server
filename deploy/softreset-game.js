function softResetGameDb() {
  var names = db.getMongo().getDBNames();
  if (names.indexOf("Inventory") >= 0) {
    db.getSiblingDB("Inventory").dropDatabase();
    print("dropped stray database Inventory");
  }
  var d = db.getSiblingDB("StandRise");

  ["player_match_history", "player_last_matches"].forEach(function(c) {
    if (!d.getCollectionNames().includes(c)) return;
    var before = d.getCollection(c).countDocuments({});
    var res = d.getCollection(c).deleteMany({});
    print(c + ": was " + before + ", deleted " + res.deletedCount);
  });

  if (d.getCollectionNames().includes("inventory_starter_pack")) {
    d.getCollection("inventory_starter_pack").find({}).forEach(function(p) {
      var items = (p.items || []).filter(function(x) { return x !== 201 && x !== 219; });
      d.getCollection("inventory_starter_pack").updateOne({ _id: p._id }, { $set: { items: items } });
    });
    print("inventory_starter_pack: removed #201/#219 from starter items");
  }

  if (d.getCollectionNames().includes("inventory")) {
    var n = 0;
    d.getCollection("inventory").find({}).forEach(function(doc) {
      if (!doc.InventoryItems) return;
      var items = doc.InventoryItems;
      var changed = false;
      for (var k in items) {
        if (!items.hasOwnProperty(k)) continue;
        var row = items[k];
        var def = row && row.itemDefinitionId;
        if (def === 201 || def === 219) {
          delete items[k];
          changed = true;
        }
      }
      if (changed) {
        d.getCollection("inventory").updateOne({ _id: doc._id }, { $set: { InventoryItems: items } });
        n++;
      }
    });
    print("inventory: stripped spin tokens from " + n + " player(s)");
  }
  print("softResetGameDb done");
}
softResetGameDb();
