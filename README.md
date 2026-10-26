# BoosterStart

Starta alla dina arbetsflikar på en gång, med en AI-CLI i varje.

![BoosterStart](docs/screenshot.png)

För varje flik väljer du:

- **katalog**, där fliken öppnas; skriv sökvägen eller välj med 📁 (på Windows kan du bläddra i
  WSL via `\\wsl.localhost\…`, valet blir en Linux-sökväg);
- **LLM**: `claude`, `codex`, `agy` eller `skal` (bara ett skal, ingen AI);
- **modell**: välj i listan eller skriv ett eget modell-id; `standard` använder CLI:ns eget val;
- **Orkester**: om `/orkester` skickas som första prompt (Codex får `$orkester` när repot har en sådan skill).

Valen sparas automatiskt och finns kvar nästa gång. Bocka ur **Med** för att hoppa över en
flik, klicka på färgremsan till vänster för att byta flikfärg.

Varje flik kör CLI:n genom ett interaktivt login-skal (`bash -lic`), så dina alias och
`PATH` från `~/.bashrc` gäller. När CLI:n avslutas ligger ett vanligt skal kvar i fliken.

## Plattformar

| | Terminal | Var flikarna körs |
|---|---|---|
| Windows | Windows Terminal (`wt.exe`), en flik per projekt med färg och titel | WSL-distributionen du anger (standard `Ubuntu`); kataloger är Linux-sökvägar |
| Linux | `gnome-terminal`, `konsole`, `xfce4-terminal`, `kitty`, `wezterm` eller `xterm` (auto eller valt) | lokalt |

Inställningarna ligger i `%APPDATA%\BoosterStart\config.json` (Windows) respektive
`~/.config/BoosterStart/config.json` (Linux).

## Bygga och installera

Kräver .NET 10 SDK. Appen använder [Avalonia](https://avaloniaui.net/) och byggs som en
fristående fil utan .NET-krav på målmaskinen.

```sh
tools/publish.sh                 # dist/win-x64/BoosterStart.exe och dist/linux-x64/BoosterStart
tools/install-linux.sh           # ~/.local/bin + menypost
```

```powershell
powershell -ExecutionPolicy Bypass -File tools\install-windows.ps1   # Start-menyn + skrivbordet
```

`BoosterStart --dry-run` skriver ut kommandona som **Starta** skulle köra, utan att öppna något.

## Utveckling

Arbetet styrs av issues och [Dirigenten och orkestern](https://github.com/solwation/dirigenten-och-orkestern):
se [AGENTS.md](AGENTS.md) och [.orchestra/project.md](.orchestra/project.md). Kör testerna med
`dotnet test tests/BoosterStart.Tests`.

## Licens

[MIT](LICENSE)
