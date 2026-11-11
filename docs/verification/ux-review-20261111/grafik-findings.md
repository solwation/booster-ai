# BoosterAI – grafisk granskning (visuell design)

Underlag: alla PNG i `uxreview-evidence/` i full upplösning, plus fyra extra headless-renderingar i `uxreview-evidence/grafik-extra/` (mörkt läge: huvudfönster med uppdateringsrad och utdatapopup, AI-agenter, Optimering, Lokala modeller). Kod: `src/BoosterAI/App.axaml`, `Styles.axaml`, `*.axaml`, samt hårdkodade färger i `ViewModels.cs`, `Converters.cs`, `OptimizationViewModel.cs` och `Catalog.cs`. Kontrastvärden är WCAG-ratio, uträknade från tokenvärden och pixelprover.

## Helhetsintryck

Grunden är bra. IBM Plex Sans/Mono är ett genomtänkt par, accenten (#5B5FF2) kommer från ikonens gradient, och den enda gradientknappen (*Starta N flikar*) är en tydlig huvudåtgärd. RAM-vyn i Optimering är det mest genomarbetade gränssnittet i appen. Problemen sitter i **systemet runt omkring**. Appen har egna textfärger, men ytor och knappar kommer orörda från Fluent (rent grå och rent svart). Det finns 16 typsnittsstorlekar och tre olika röda färger. Fönstren har vuxit fram vart för sig: Lokala modeller, inställningsflikarna och dialogerna följer var sin stil. Fynden nedan är sorterade efter prioritet.

| id | Område | Prio | Storlek |
|---|---|---|---|
| G-1 | Ytor och neutraler: Fluents rena grå/svart krockar med appens blågrå tokens | high | M |
| G-2 | Knapphierarki: sekundär ser inaktiv ut, tre olika primära, ingen farlig-variant | high | M |
| G-3 | Statusfärger är hårdkodade, spridda och inte temaanpassade; feltext under AA | high | S–M |
| G-4 | Typografisk skala: 16 storlekar, Bold utan Bold-fil | high | M |
| G-5 | Utdatapopupen: tofu-glyfer, grått på grått, genomskinlig i mörkt läge | high | S |
| G-6 | Lokala modeller: sämsta hierarkin, överlapp och klippning | high | M |
| G-7 | Huvudlistans rytm: ojämn radhöjd, tomma kolumner, avvikande ×-knapp | medium | S |
| G-8 | Inställningsflikarna saknar gemensam sidmall och har två undernavigeringar | medium | M |
| G-9 | Monospace används utan tydlig regel | medium | S |
| G-10 | Svag kontrast och omvänd hierarki i etiketter (Faint, kolumnrubriker) | medium | S |
| G-11 | Ikonografi: kugghjulet ser ut som en sol, textglyfer blandas med vektorikoner | medium | S |
| G-12 | Hörnradier utan skala (2/3/4/5/6/8/10) | low | S |
| G-13 | Varumärke: standardflikfärgen är accentblå, appikonens ”ai” blir oläsligt i små storlekar | low | S |
| G-14 | Agentlogotyper har olika optisk vikt och form | low | S |

---

## G-1 – Ytor och neutraler: Fluents grå/svart krockar med appens blågrå tokens
**Skärm/fil:** alla fönster, tydligast i `main-dark.png`, `settings-dark.png` och `grafik-extra/*-dark.png`. `App.axaml` (ThemeDictionaries definierar bara text-, länk- och hovringsfärger).

**Problem:** Textfärgerna är blågrå (`TextBrush` #1B1D26/#F2F3F7, `MutedBrush` #5A5E6E/#A9ADBC). Alla ytor kommer däremot orörda från Fluent i neutral grå utan färgton. I ljust läge är fönstret #FFFFFF, banderollen #F3F3F3 och knapparna #CCCCCC. Mörkt läge använder rent **#000000** som fönsterbakgrund och #333333 för knapparna. Kort och rader använder `RowHoverBrush`, som är ett genomskinligt lager (#0C000000/#14FFFFFF), och blir därför olika gråa beroende på vad som ligger under.

**Varför:** Rent svart med nästan vit text (#F2F3F7) ger hög halo och ett ”terminalt” snarare än ett designat intryck. Den varma grå knappen mot blåa textfärger gör att knapparna ser lånade ut. Utan en yt-token går det inte att justera kort, banderoller, popupen och inaktiva tillstånd i takt.

**Förslag:** Lägg till yt-tokens i båda ThemeDictionaries och mappa Fluents nycklar (`SystemRegionBrush`/fönsterbakgrund, `ButtonBackground*`, `ToolTipBackground`, `ComboBoxBackground`) till dem:

| Token | Ljust | Mörkt |
|---|---|---|
| `BackgroundBrush` | #FFFFFF | #14151B |
| `SurfaceBrush` (kort, banderoll, popup) | #F5F6F9 | #1C1E26 |
| `SurfaceStrongBrush` (sekundär knapp, spår) | #ECEEF3 | #262935 |
| `BorderBrush` | #D9DCE5 | #343846 |

Behåll `RowHoverBrush` för hovring, men låt kort (`agent-card`, `agent-group`, `memory-stat`, `Border.update`) använda `SurfaceBrush`.

**Prio:** high · **Storlek:** M

## G-2 – Knapphierarki: sekundär ser inaktiv ut, tre olika primära, ingen farlig-variant
**Skärm/fil:** `main.png` (*Starta om alla* / *Stäng alla* / *Inställningar*), `uninstall.png`, `close.png`, `restart.png`, `local-models.png`, `local-model.png`, `setup-*.png`. `Styles.axaml` rad 256–281 (`Button.main-action`, `Button.boost`), `Classes="accent"` (20 ställen).

**Problem:**
1. **Sekundär och inaktiv knapp ser likadana ut.** I `main.png` har *Starta om alla (3)* (aktiv) och *Stäng alla* (inaktiv) samma fyllning, #CCCCCC. Bara textfärgen skiljer dem åt. *Inställningar* uppe till höger ser därför också inaktiv ut.
2. **Det finns tre olika primära knappar:** gradienten med radie 10 (Starta), platt accent med radie 4 (Klar, Nästa, Uppdatera, Installera) och grå sekundär (*Klar* i `local-models.png` och `local-model.png`, där samma ord är primärt i Inställningar).
3. **Ingen variant för farliga åtgärder.** *Avinstallera* får vänlig accentfärg, och *Tvångsstäng* (`close.png`) ser ut precis som *Avbryt*.
4. Texten i `main-action`-knapparna ligger ca 4 px för högt (`Height` binds till Starta-knappens höjd i `MainWindow.axaml` rad 262–264, men innehållet centreras inte vertikalt).

**Varför:** Användaren läser knapparnas vikt innan texten. När den aktiva sekundära knappen ser inaktiv ut blir *Starta om alla* svår att hitta, och den farligaste åtgärden i appen syns minst.

**Förslag:** Fyra varianter som klasser i `Styles.axaml`:
- **boost** – gradienten, *endast* Starta (som i dag).
- **primary** (ersätter Fluents `accent`) – fyllning #5B5FF2, hovring #4C4FDB, vit text (4,8:1), radie 6.
- **secondary** (standard) – `SurfaceStrongBrush`, 1 px `BorderBrush`, `TextBrush`, radie 6. Hovring `RowHoverBrush` ovanpå.
- **danger** – ljust #C62F35, mörkt #E5484D, vit text. Används för *Avinstallera*, *Tvångsstäng* och *Ta bort projekt*.
- **inaktiv** (alla varianter) – transparent bakgrund, kantlinje 50 % och text i `FaintBrush`, så att den tydligt skiljer sig från secondary.

Ge också `Button.main-action` `VerticalContentAlignment="Center"`. Gör *Klar* primär i alla fönster som har den knappen.

**Prio:** high · **Storlek:** M

## G-3 – Statusfärger är hårdkodade, spridda och inte temaanpassade
**Skärm/fil:** `main-error.png`, `settings-optimization.png` (”24–32 GB”), `settings-agents.png` (gröna prickar), `setup-*` (felrad). Hårdkodade värden:
- röd: `Styles.axaml:206` #E5484D, `Converters.cs:22` #E5484D, `OptimizationViewModel.cs:188` #E5484D, `ViewModels.cs:393` #D2453F, `InstallWizardView.axaml:172` #D20F39
- grön: `ViewModels.cs:389` #2E9E5B, `OptimizationViewModel.cs:189` #46A758, `SettingsWindow.axaml:536` #28A56A, `RecommendedBrush` #2F8A42/#5BC26E
- gul/orange: `ViewModels.cs:392` #D08A1E, `SettingsWindow.axaml:600` #20DD7536
- blå: `ViewModels.cs:388` #3B7DD8

**Problem:** Samma betydelse har tre eller fyra nyanser, och ingen av dem byter värde mellan ljust och mörkt läge. Feltexten *Kunde inte starta Windows Terminal* (#E5484D på vitt) har **3,9:1**, under AA för 14 px. Grön text #28A56A har 3,1:1 och #2E9E5B 3,4:1 på vitt. Dialogen *Stäng alla* (`close.png`) visar status som ren text utan färgprickar, fast huvudfönstret lärt användaren att läsa just prickarna.

**Varför:** Statusfärgerna är appens viktigaste signal (Körs/Bevakar/Stängd/Avslutad). När de inte kommer från samma källa glider de isär för varje ny vy.

**Förslag:** Semantiska tokens i ThemeDictionaries, med en textvariant (AA på bakgrunden) och en prickvariant:

| Token | Ljust text / prick | Mörkt text / prick |
|---|---|---|
| `SuccessBrush` | #2F8A42 (4,3:1 → använd #27773A för text, 5,4:1) / #2E9E5B | #5BC26E / #46A758 |
| `DangerBrush` | #C62F35 (5,6:1) / #D2453F | #FF6B6F / #E5484D |
| `WarningBrush` | #9A6200 / #D08A1E | #E8A93A / #D08A1E |
| `InfoBrush` | #2F6FD0 / #3B7DD8 | #6FA4F0 / #3B7DD8 |
| `NeutralStatusBrush` | #8A8FA0 | #8A8FA0 |

Låt `StatusBrush`, `ErrorBrush` och `StatusColorBrush` slå upp resurserna i stället för `Color.Parse`. Återanvänd prick + text i `CloseAllWindow` (Sparar… = info, Stängd = neutral, Misslyckades = danger).

**Prio:** high · **Storlek:** S–M

## G-4 – Typografisk skala: 16 storlekar, Bold utan Bold-fil
**Skärm/fil:** alla fönster. `Styles.axaml` (`title` 22, `sectionhead` 14, `name` 15, `path` 12,5, `colhead` 12), samt lokala `FontSize`: 11, 11,5, 12 (105 ställen), 12,5, 13, 13,5, 14, 15, 16, 17, 19, 20, 22, 25, 26 och 30. `assets/fonts` innehåller Plex Sans Regular/Medium/SemiBold och Plex Mono Regular.

**Problem:**
- Rubrikerna varierar mellan fönster: huvudfönstret och Inställningar 22, dialoger och installationsguiden 20 (lokal override). Guidens steg-rubriker är `sectionhead` med override till 17, medan `sectionhead` annars är 14. Banderoller använder 13,5 och versionsraden 11,5.
- `FontWeight="Bold"` (SettingsWindow, t.ex. minnesvärdet på rad 308 och ”Lokalt: välj modell för ett råd” i agentkorten) har ingen Bold-fil och syntetiseras eller faller tillbaka. Därför ser den ut som en annan vikt än SemiBold bredvid.
- Mono finns bara i Regular, men `ComboBox.field` och `TextBox.name` blandar ibland in SemiBold-kontexter.

**Varför:** Halvpunktssteg (12/12,5/13/13,5) syns inte som hierarki, bara som oro. När rubrikstorlekarna skiljer sig mellan fönster känns appen hopsatt.

**Förslag:** Definiera en skala som resurser i `App.axaml` och klasser i `Styles.axaml`. Ta bort alla lokala `FontSize` som inte finns i den.

| Klass | Storlek / vikt / radhöjd | Används för |
|---|---|---|
| `display` | 28 SemiBold / 34 | RAM-siffran (i dag 30), budgetvärdet (26) |
| `title` | 20 SemiBold / 26 | fönster- och sidtitlar (alla, även huvudfönstret) |
| `subtitle` | 16 SemiBold / 22 | guidens steg-rubriker, stat-värden (i dag 19) |
| `sectionhead` | 14 SemiBold / 20 | sektionsrubriker |
| `body` | 14 Regular / 20 | standard |
| `body-small` | 13 Regular / 18 | banderoller, hjälptexter (ersätter 12,5 och 13,5) |
| `caption` | 12 Regular eller Medium / 16 | kolumnrubriker, metadata, version (ersätter 11 och 11,5) |

Använd endast vikterna Regular, Medium och SemiBold (byt `Bold` → `SemiBold`).

**Prio:** high · **Storlek:** M

## G-5 – Utdatapopupen: tofu-glyfer, grått på grått, genomskinlig i mörkt läge
**Skärm/fil:** `main-output.png`, `grafik-extra/main-dark-update-output.png`. `StatusPeekView.axaml`, `TabOutputView`, `Styles.axaml` rad 93–115, `OutputBackgroundBrush`.

**Problem:**
- Claudes tecken ⎿ (U+23BF) saknas i IBM Plex Mono och renderas som **tomma rutor** framför ”Read 142 lines”, ”Tests: …” och ”Updated …”. Det ser trasigt ut i appens mest använda förhandsvisning.
- Ljust läge: popupens yta #F2F2F2 med utdatafält #E9E9E9 ger tre gråtoner som knappt går att skilja, och popupen saknar både kant och skugga. Rubriken ”Körs” är vanlig text utan statusprick.
- Mörkt läge (headless): fönstrets innehåll (rader, comboboxar) syns tydligt **genom** popupen. Kontrollera i riktiga appen. Gäller det också där är det en allvarlig läsbarhetsbrist.

**Förslag:**
- Ange fallback: `MonoFont` = `avares://BoosterAI/Assets/Fonts#IBM Plex Mono, Cascadia Mono, Consolas, DejaVu Sans Mono, monospace`, eller översätt ⎿ till └ (U+2514, finns i Plex Mono) i `TerminalText`.
- Popup: opak `SurfaceBrush`, 1 px `BorderBrush`, radie 8, `BoxShadow="0 8 24 0 #2E000000"`. Utdatafältet `BackgroundBrush`, inte en tredje grå.
- Rubrik: samma prick + statustext + projektnamn som i raden (`● Körs · Atlas`).

**Prio:** high · **Storlek:** S

## G-6 – Lokala modeller: sämsta hierarkin, överlapp och klippning
**Skärm/fil:** `local-models.png`, `local-model.png`, `grafik-extra/local-models-dark.png`. `LocalModelsWindow.axaml` (14 × `FontSize="12"`), `LocalModelWindow.axaml`.

**Problem:**
- Nästan all text är 12 px, och rekommendationen (”Börja med qwen3.5:9b …”) är ett fetstilt textstycke utan behållare. Ögat hittar ingen startpunkt.
- Vid standardstorleken överlappar modellistan texten under den (raden ”qwen3.5:9b…” skärs mitt i, och ”Text, kod och verktygsanvändning” ligger direkt mot den). Fältet `http://127.0.0.1:11434` och knappen *Kontrollera* klipps nedtill.
- Den valda raden har en tung lila fyllning (#B6B8F3), till skillnad från resten av appen där valt visas med kontur (temachipsen i Utseende).
- Modell-id:n (`qwen2.5-coder:1.5b-instruct-q4_K_M`) är proportionella i SemiBold 12, medan samma sorts värde är mono i huvudfönstret.
- *Klar* är grå sekundär, medan samma knapp är primär i Inställningar.

**Förslag:**
- Struktur som i Inställningar: titel 20, ingress 13 muted, sedan sektioner med `sectionhead` och 20 px mellanrum.
- Lägg rekommendationen i en callout: `agent-group`-stil (`SurfaceBrush`, radie 8, padding 14,12) med `BoltIcon` i accentfärg och modell-id:t i mono.
- Ge listan en egen `Grid`-rad med `*`-höjd och `MinHeight` 180, så att den aldrig ritas ovanpå efterföljande innehåll. Låt fönstrets yttre `ScrollViewer` ta överskottet.
- Vald rad: `RowHoverBrush` + 1 px accentkontur, radie 6. Modell-id i `MonoFont` 13, metadata `caption`.
- *Klar* primär, *Hämta modell* primär när en modell är vald, *Avbryt* sekundär.

**Prio:** high · **Storlek:** M

## G-7 – Huvudlistans rytm: ojämn radhöjd, tomma kolumner, avvikande ×-knapp
**Skärm/fil:** `main.png`, `main-restart.png`. `MainWindow.axaml` rad 122 och 132–232 (`project-columns`, `Border.row`, undertabbsraden `Margin="62,6,0,2"`).

**Problem:**
- Radavståndet varierar: Atlas-api → Boreal 56 px, Boreal → Cirrus → Delta 64 px. Projektrader har alltid en tom underrad (StackPanel med marginal 6+2 px) även när de är ihopfällda. Därför sitter rand, kryssruta och namn inte vertikalt centrerade i hovringsytan.
- Kolumnerna *Orkester* och *Engångskörning* har rubriker men är tomma i alla skärmbilder, totalt 240 px. Kompositionen blir vänstertung, och knappen *Inställningar* ligger i linje med en tom yta.
- Undertabbens ×-knapp har en grå 32 × 32-ruta (inaktiverad `Button.icon` får Fluents inaktiva bakgrund). Ingen annan ikonknapp har en sådan ruta, så den ser ut som en främmande kontroll.

**Förslag:**
- Sätt `IsVisible="{Binding Expanded}"` på den yttre undertabbs-StackPanel och flytta marginalen dit. Ge `Border.row` `MinHeight="52"` och ta bort den undre marginalen. Alla rader får då samma rytm (52 + 2).
- Dölj kolumnrubriken och kolumnbredden för Orkester/Engångskörning när ingen synlig rad har `CanOrkester` (sätt bredd 0), så att tabellen slutar vid Modell. Alternativt: visa ett svagt ”–” i varje cell så att kolumnen inte ser trasig ut.
- Ge `Button.icon:disabled` en transparent bakgrund och lägg opaciteten på ikonen, som för `Button.remove`.

**Prio:** medium · **Storlek:** S

## G-8 – Inställningsflikarna saknar gemensam sidmall
**Skärm/fil:** `settings-projects.png`, `settings-general.png`, `settings-optimization.png`, `settings-appearance.png`, `settings-agents.png`. `Styles.axaml` rad 27–48. `SettingsWindow.axaml` rad 19, 179, 527.

**Problem:**
- *Projekt*, *Optimering* och *AI-agenter* börjar med en sidtitel (22 SemiBold). *Allmänt* och *Utseende* börjar direkt med 14 px sektionsrubriker. Titelns höjd hoppar alltså när man byter flik.
- Det finns två olika undernavigeringar. *Optimering* använder en lodrät `TabControl` (150 px, valt = 2 px accentstreck till vänster, innehåll börjar x = 218). *AI-agenter* använder `Button.agent-nav` (130 px, valt = grå fyllning och SemiBold, innehåll börjar x = 180).
- Den valda huvudfliken markeras med en understrykning i accentfärg som är tunn (ca 2 px) och dessutom lika lång som texten. Det är okej, men vikten ändras inte (alla flikar är redan SemiBold), så valt läge syns svagt.

**Förslag:**
- En sidmall: titel (`title` 20) + ingress (`body-small` muted) + sektioner. Ge även *Allmänt* (”Allmänt”) och *Utseende* (”Utseende”) en titel.
- En `SideNav`-stil för båda: kolumn 168 px, objekt 36 px högt, 13 px Medium, valt = `RowHoverBrush` + 3 px accentstreck till vänster + SemiBold, och innehållet börjar på samma x (196) i båda flikarna.
- Huvudflikar: ovalda Regular `MutedBrush`, valda SemiBold `TextBrush` med 2 px accentlinje.

**Prio:** medium · **Storlek:** M

## G-9 – Monospace används utan tydlig regel
**Skärm/fil:** `main-setup.png` (”codex (sakna…”), `setup-install.png`, `uninstall.png`, `local-models.png`, `settings-appearance.png`. `Styles.axaml` rad 164–170 (`ComboBox.field` sätter `MonoFont` på hela kontrollen), `Converters.cs` (”(saknas)” läggs i samma sträng).

**Problem:**
- Huvudfönstrets comboboxar är mono rakt igenom, så det svenska ordet ”(saknas)” sätts i mono och klipps av (`codex (sakna`).
- Installationsguiden visar sökvägar (`C:\Users\me\AppData\…`) proportionellt i punktlistan. Avinstallationsdialogen visar samma sorts sökvägar i mono, och där bryts `Booster AI` mitt i ordet.
- Modell-id:n är mono i huvudfönstret men proportionella i Lokala modeller.

**Varför:** Kommentaren i `App.axaml` säger att mono är till för ”paths, models and commands”. Regeln är bra men tillämpas per kontroll i stället för per värde.

**Förslag:** Mono **bara på själva värdet** (sökväg, kommando, CLI-namn, modell-/tagg-id), aldrig på omgivande ord.
- LLM-mallen: lägg ”saknas” i en separat `TextBlock` i `UiFont` 12 `WarningBrush`, eller ersätt med en varningsprick plus tooltip.
- Guidens sammanfattning: sökvägar i `Run` med `MonoFont` 13.
- Avinstallationslistan: `TextWrapping="NoWrap"` + `TextTrimming="PathEllipsis"`-liknande avkortning (eller horisontell rullning), så att sökvägar inte bryts mitt i.

**Prio:** medium · **Storlek:** S

## G-10 – Svag kontrast och omvänd hierarki i etiketter
**Skärm/fil:** `main.png` (kolumnrubriker), `settings-general.png`, `settings-appearance.png`, `setup-settings.png` (etiketterna ”Efter Starta”, ”Utseende”, ”Språk”, ”AI-CLI”). `App.axaml` `FaintBrush` (#7F8394 ljust), `Styles.axaml` `TextBlock.colhead` (12 px Faint, 24 användningar).

**Problem:** `FaintBrush` på vitt ger 3,77:1 och på banderollens #F3F3F3 3,4:1, under AA för 12 px. I formulären är etiketten till vänster (12 px Faint) *svagare* än hjälptexten till höger (14 px Muted), så hierarkin blir omvänd: förklaringen syns före det den förklarar.

**Förslag:**
- `FaintBrush` ljust → #6B6F80 (≈ 4,9:1). Mörkt #7C8091 klarar 5,3:1 på svart men bör kontrolleras mot den nya bakgrunden #14151B (G-1); höj till #8A8EA0 om det behövs.
- Formuläretiketter: 13 px Medium `TextBrush`. Hjälptexten till höger: 13 px `MutedBrush`.
- `colhead`: 12 px Medium `MutedBrush` i stället för Faint.

**Prio:** medium · **Storlek:** S

## G-11 – Ikonografi: kugghjulet ser ut som en sol, textglyfer blandas med vektorikoner
**Skärm/fil:** `main.png` (*Inställningar*), huvudlistan (▸ ▾ ⋮ ↳). `App.axaml` `GearIcon`, `MainWindow.axaml` rad 145, 155–158.

**Problem:**
- `GearIcon` är en cirkel med åtta raka strålar, och det är exakt så ”ljust läge/sol” brukar ritas. Inställningar innehåller dessutom ett temaval, så ikonen läses som ”Utseende”.
- Expandera-pilen (▸/▾), dragmarkören (⋮) och undertabbspilen (↳) är textglyfer i UiFont. Ikonerna bredvid är vektorer med 1,5 px streck och rundade ändar, så de får en annan tjocklek och baslinje. Glyferna ▸/▾ är dessutom svarta och fyllda, och de tyngsta elementen i raden efter namnet.
- Ikonstorlekarna varierar mellan 9, 12, 14 och 15 px utan system.

**Förslag:**
- Ersätt `GearIcon` med ett riktigt kugghjul, t.ex. Lucides ”settings” (24-rutnät, skala 0,667), eller ett enkelt sexkuggat hjul. Använd ”sliders” om det ska läsas som inställningar snarare än system.
- Lägg till `ChevronRightIcon` `M6,4 L10,8 L6,12`, `ChevronDownIcon` `M4,6 L8,10 L12,6` och `GripIcon` (2 × 3 prickar, r = 1) i `App.axaml`, och rita dem med samma `Button.icon Path`-stil.
- Två ikonstorlekar: 16 px i knappar och 12 px inline, alltid med 1,5 px streck.

**Prio:** medium · **Storlek:** S

## G-12 – Hörnradier utan skala
**Skärm/fil:** `Styles.axaml` (rader 8, kort 8, uppdateringsrad 8, stats 5, spår 4, rand 2, boost/main-action 10) och lokala `CornerRadius` 2, 3, 4, 5, 6. Fluent-knappar och fält 4.

**Problem:** Sju olika radier. I huvudfönstret står knappar med radie 10 bredvid comboboxar med radie 4, och i Optimering ligger stat-kort med radie 5 inuti en sida där andra kort har 8.

**Förslag:** Tre värden som resurser: `RadiusSmall` 4 (fält, kryssrutor, spår, ränder), `RadiusMedium` 6 (alla knappar inklusive boost, chips, valda listrader) och `RadiusLarge` 8 (kort, banderoller, popuper, dialogytor).

**Prio:** low · **Storlek:** S

## G-13 – Varumärke: standardflikfärgen är accentblå, appikonens ”ai” blir oläsligt i små storlekar
**Skärm/fil:** `main.png`, `settings-projects.png` (alla ränder #1E66F5), `setup-*.png` (ikon 28 px). `Config.cs:18`, `Catalog.cs:41`, `InstallWizardView.axaml` rad ~40, `assets/boosterai*.svg`.

**Problem:**
- Standardfärgen för en ny flik är #1E66F5, alltså gradientens startfärg. Bredvid den accentfyllda kryssrutan bildar randen ett blått dubbelstreck på varje rad, och färgkodningen (en egenskap som README lyfter fram) syns inte förrän användaren byter färg.
- Appikonen med ”ai” visas i 40 px (huvudfönstret) och 28 px (guiden). I 28 px blir ”ai” ca 5 px högt och ser ut som smuts. `boosterai-small.svg` finns just för små storlekar men används bara i 16–24 px ICO-lager.

**Förslag:**
- Låt nya projekt få nästa färg i paletten (rotera från index 1: #2E9E44, #E8A317 …) i stället för #1E66F5. Alternativt: flytta #1E66F5 sist i Standard-paletten.
- Använd `boosterai-small` (bara blixten) för alla visningar ≤ 32 px, alltså guidens rubrik. Behåll ”ai”-varianten från 40 px och uppåt.
- Positivt: ikonens gradient, radie (56/240 ≈ 23 %) och accentfärgen är konsekventa och bör behållas. Gradienten hör bara hemma på Starta och i ikonen.

**Prio:** low · **Storlek:** S

## G-14 – Agentlogotyper har olika optisk vikt och form
**Skärm/fil:** `settings-agents.png`, `grafik-extra/settings-agents-dark.png`, huvudfönstrets LLM-combobox. `assets/agents/*`, `AgentIcon.cs`.

**Problem:** Claudes stjärna är tunn och orange, Codex en svart klump, Aider ett fyllt svart block, Open Interpreter en **tom cirkel** (läses som en radioknapp eller ett tomt tillstånd) och OpenHands färgade emoji-händer med mycket mindre optisk storlek. I sidonavigeringen och i korten står de bredvid varandra och ser ut som olika ikonfamiljer.

**Förslag:** Varumärkena ska inte ritas om, men ge dem en gemensam bärare: en 20 × 20 platta (`SurfaceStrongBrush`, radie 5) med logotypen centrerad i 14 px (16 px i korten med en 28 × 28 platta). Använd en fylld variant för Interpreter (fylld cirkel med vit text eller symbol), så att den inte ser ut som en kontroll.

**Prio:** low · **Storlek:** S

---

### Föreslagen ordning
1. G-1 + G-3 + G-12 i ett pass: yt-, status- och radietokens i `App.axaml`. Det mesta av G-2 och G-10 följer då nästan av sig självt.
2. G-2 (knappvarianter) och G-4 (typskala). De påverkar varje fönster och bör göras innan enskilda vyer finjusteras.
3. G-5 och G-6, de synligaste enskilda bristerna.
4. G-7, G-8, G-9, G-11, G-13, G-14.
