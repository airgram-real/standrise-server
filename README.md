# StandRise Server

Private game server stack (RPC, MongoDB, Photon plugin) for StandRise / Standoff 2 client **0.17.0**.

## Layout

| Path | Description |
|------|-------------|
| `RpcServer/` | C# RPC server (matchmaking, inventory, clans, stats) |
| `PhotonPlugin/` | Photon LoadBalancing match plugin |
| `ProtoFiless/` | Protobuf definitions and stubs |
| `deploy/` | Install scripts (`INSTALL.cmd`, `config.env.example`) |
| `Web/` | Site / admin / Telegram mini-app (optional) |
| `CLAUDE.md` | Deploy and ops reference |

## Not in this repo

- `deploy/config.env`, `local.settings.json` (secrets — use `.example` files)
- `bin/`, `data/`, Mongo dumps, logs, APK/OBB, full Photon SDK (see `deploy/Create-Archive.ps1`)

## Build

```powershell
dotnet build VsCode.csproj -c Release
dotnet build PhotonPlugin\MatchmakingPlugin.csproj -c Release
```

See `CLAUDE.md` for full Windows Server deployment.
