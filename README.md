# Dizzy Clothesline

A [Sailwind](https://store.steampowered.com/app/1764530/Sailwind/) mod for [BepInEx 5](https://thunderstore.io/c/sailwind/p/BepInEx/BepInExPack/) that ties a line between two lamp hooks, so you can hang things along it.

## Use

### Tie a line
- Look at a lamp hook that is up on a wall and press **L**. A rope follows your aim.
- Look at a second lamp hook and press **L** again to tie the line.
- Press **L** on the first hook again, or on nothing, to cancel.
- Both hooks must be on the same boat and no more than 6 m apart. A hook can hold several lines, and still takes a lantern of its own.

### Hang things
- Hold anything that can hang on a lamp hook and click the line. It hangs where you clicked.
- That includes lanterns, the bundles from [Dizzy Bundle Up](https://github.com/foxyv/DizzyFirewoodBundle), and the extra items [HooksHangMore](https://github.com/bryon82/SailwindHooksHangMore) lets you hang. Food dries on a line just as it does on a hook with HooksHangMore.
- Items keep a little space between them. Click where there is room.
- Click a hanging item to take it down.

### Untie
- Look at the line and press **L**. Anything on it falls.
- Picking up either hook also unties its lines.

Lines and what hangs on them are saved with your game.

### Co-op
- With the Sailwind Coop mod, every player sees the same lines. Tying, untying, hanging and taking things down are shared.
- Every player needs this mod. A player without it sees an item hung on a line hanging from the line's first hook instead.
- The host's save keeps the lines. A guest who joins gets the host's lines, not the ones in their own world, and their own world keeps its lines for when they play alone again.

## Config

`BepInEx/config/com.dizzy.sailwind.clothesline.cfg`

| Setting | Default | |
|---|---|---|
| Controls / Tie Key | L | Start, tie, cancel and untie lines. |
| Line / Max Length | 6 | Longest line, in metres. |
| Line / Sag | 0.04 | How far the middle hangs, as a share of the length. |
| Line / Thickness | 0.02 | Rope thickness, in metres. |
| Line / Color | tan | Rope color. |
| Hanging / Item Spacing | 0.2 | Closest two items can hang, in metres. |
| Hanging / Hang Drop | 0.02 | How far below the rope an item hangs, in metres. |

A changed default only reaches a new config file. Edit an existing file, or use ConfigurationManager, to change a setting you already have.

## Without the mod

Lines are saved in the game's mod data (`Dizzy.Clothesline`). If the mod is removed, the lines are gone and anything that hung on them drops to the deck when the game loads. The hooks and items themselves are untouched.

## Not yet

- VR: the tie key uses the desktop pointer.

## Build

```powershell
py -3 "C:/Users/dutya/.cursor/skills/sailwind-modding/scripts/modcheck/modcheck.py" .
dotnet build src/Dizzy.Clothesline/Dizzy.Clothesline.csproj -c Release -p:DeployOnBuild=false
```

Set `-p:SailwindDir=...` if Sailwind is not in `D:\SteamLibrary\steamapps\common\Sailwind`.
