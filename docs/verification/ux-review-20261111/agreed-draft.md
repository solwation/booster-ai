Godkänd av UX och grafiker

# Gemensam lista – issueutkast (UX + grafik)

Källor: `ux-findings.md` (UX-1…14), `grafik-findings.md` (G-1…14), `discussion-ux-1.md`, `discussion-grafik-1.md`, `discussion-ux-2.md`. Skärmbilder i `uxreview-evidence/`, `ux-extra/`, `grafik-extra/`.

Alla acceptanskriterier verifieras med headless `--screenshot` (se docs/DEVELOPMENT.md → Screenshots), i ljust **och** mörkt tema och på svenska om inget annat anges. Där ett kriterium inte syns i en bild anges ett test i stället. ”Kontrast” betyder WCAG 2.x-ratio mot den faktiska bakgrunden.

## Ordning och beroenden

```
D1 Tokens ─┬─ D2 Knappsystem ─┬─ D5 Starta-tydlighet
           │                  ├─ D7 Fel och återhämtning
           │                  └─ D9 Lokala modeller
           ├─ D3 Typskala ────── D11 Inställningar
           ├─ D6 Huvudlistan
           ├─ D8 Live Peek
           ├─ D10 Ikoner och radkontroller ─┬─ D12 Agentkort
           │                                └─ D11
           └─ D13 Guiden
D4 Ordlista (oberoende; tidigt, påverkar strängar i D5/D6/D13)
```

| # | Titel | Prio | Storlek | Beror på |
|---|---|---|---|---|
| D1 | Färg-, yt- och radietokens | high | M | – |
| D2 | Knappsystem: primary/secondary/danger/inaktiv + Stäng alla-flödet | high | M | D1 |
| D3 | Typografisk skala | medium | M | D1 (mjukt) |
| D4 | Enhetlig ordlista: agent, modell, flik | high | S | – |
| D5 | Gör det tydligt vad Starta öppnar | high | S | D1, D2, D4 |
| D6 | Huvudlistan: orkesterkolumn, statusbredd, ”saknas”, radrytm, flikfärger | medium | M | D1, D4 |
| D7 | Fel och återhämtning: felbanderoll med åtgärd | high | M | D1, D2 |
| D8 | Live Peek: glyfer, opak yta, rubrik | high | S | D1 |
| D9 | Lokala modeller och modellråd i steg | high | M–L | D1, D2 (D3 mjukt) |
| D10 | Ikoner och radkontroller: betydelse, ritning, tangentbord | medium | M | D1 |
| D11 | Inställningar: gemensam sidmall och ny placering av innehåll | medium | M | D3, D10 (mjukt) |
| D12 | Agentöversikten: begripligt språk, åtgärder, logotypplattor | medium | S | D1, D10 |
| D13 | Installationsguiden: agentstatus, projektval, liten ikon | medium | S | D1, D4 |

---

## D1 – Färg-, yt- och radietokens
**priority: high** – feltext och etiketter når i dag inte AA (3,9:1 och 3,8:1), och alla senare leveranser behöver tokens.

**Bakgrund:** G-1, G-3, G-10, G-12, UX-14 (kontrastdelen). `main-error.png`, `main-dark.png`, `settings-general.png`, `settings-optimization.png`. Hårdkodade färger i `ViewModels.cs` (388–393), `Converters.cs:22`, `OptimizationViewModel.cs:188–189`, `SettingsWindow.axaml:536,600`, `InstallWizardView.axaml:172`, `Styles.axaml:206`. `App.axaml` definierar bara text-, länk- och hovringsfärger.

**Spec:** Lägg till tokens i båda ThemeDictionaries i `App.axaml` och mappa Fluents nycklar till dem (fönsterbakgrund, `ButtonBackground*`, `ToolTipBackground`, `ComboBoxBackground`).

| Token | Ljust | Mörkt |
|---|---|---|
| `BackgroundBrush` | #FFFFFF | #14151B |
| `SurfaceBrush` (kort, banderoll, popup) | #F5F6F9 | #1C1E26 |
| `SurfaceStrongBrush` (sekundär knapp, spår) | #ECEEF3 | #262935 |
| `BorderBrush` | #D9DCE5 | #343846 |
| `FaintBrush` | **#6B6F80** (5,0:1 mot #FFFFFF, 4,6:1 mot `SurfaceBrush`) | **#8A8EA0** (5,1:1 mot `SurfaceBrush`; dagens #7C8091 ger bara 4,2:1 där) |
| `SuccessBrush` text / prick | #27773A / #2E9E5B | #5BC26E / #46A758 |
| `DangerBrush` text / prick | #C62F35 / #D2453F | #FF6B6F / #E5484D |
| `WarningBrush` text / prick | #9A6200 / #D08A1E | #E8A93A / #D08A1E |
| `InfoBrush` text / prick | #2F6FD0 / #3B7DD8 | #6FA4F0 / #3B7DD8 |
| `NeutralStatusBrush` | #8A8FA0 | #8A8FA0 |
| `DangerSurfaceBrush` (felbanderollens bakgrund, D7) | #FDF0F0 | #2A1518 |
| `DangerFillBrush` (danger-knappens fyllning, D2) | #C62F35 (vit text 5,4:1) | #C62F35 (samma; #E5484D ger bara 3,9:1 med vit text) |
| `RadiusSmall` / `Medium` / `Large` | 4 / 6 / 8 | samma |

`StatusBrush`, `ErrorBrush` och `StatusColorBrush` slår upp resurserna i stället för `Color.Parse`. Kort (`agent-card`, `agent-group`, `memory-stat`, `Border.update`) använder `SurfaceBrush`. `BorderBrush` är bara till för dekorativa kanter (kort, popup, sekundärknapp). Kanter på fält (TextBox, ComboBox, CheckBox) behåller en kantfärg med ≥3:1 mot bakgrunden (WCAG 1.4.11). `FaintBrush` används inte som text på `SurfaceStrongBrush` (4,3:1), utom på inaktiva kontroller. `colhead` blir 12 px Medium `MutedBrush`. Formuläretiketter blir 13 px Medium `TextBrush`, och hjälptext 13 px `MutedBrush` (den omvända hierarkin rättas).

**Acceptanskriterier:**
- [ ] Ingen hex-färg för status eller fel finns kvar i `.cs`/`.axaml` utanför `App.axaml` (grep i CI eller test).
- [ ] `--fake-status "error:…"`: feltexten har ≥4,5:1 mot bakgrunden i båda teman.
- [ ] `main.png`, `settings-general.png`: kolumnrubriker och formuläretiketter ≥4,5:1, och etiketten är minst lika framträdande som hjälptexten.
- [ ] `main-dark.png`: fönsterbakgrunden är #14151B, inte #000000. Knappar, banderoller och comboboxar har tokenfärger, inte Fluent-grå.
- [ ] Alla `CornerRadius` i `Styles.axaml`/vyerna är 4, 6 eller 8.
- [ ] Fältkanter (TextBox/ComboBox) har ≥3:1 mot bakgrunden i båda teman.

**Beroenden:** inga.

---

## D2 – Knappsystem: primary/secondary/danger/inaktiv + Stäng alla-flödet
**priority: high** – aktiva sekundärknappar (*Inställningar*, *Starta om alla*) ser i dag inaktiva ut, och den farligaste åtgärden (*Tvångsstäng*) syns minst.

**Bakgrund:** G-2, UX-9. `main.png`, `ux-extra/main-empty.png`, `close.png`, `uninstall.png`, `restart.png`, `local-models.png`. `Styles.axaml` 256–281, `Classes="accent"` (20 ställen), `close.confirm` i `Strings.sv.json`.

**Spec:**
- Klasser: **boost** (gradient, *endast* Starta, radie 6 enligt D1), **primary** (#5B5FF2, hovring #4C4FDB, vit text 4,8:1), **secondary** (standard: `SurfaceStrongBrush`, 1 px `BorderBrush`, `TextBrush`), **danger** (`DangerFillBrush` #C62F35 i båda teman, vit text 5,4:1), **inaktiv** för alla varianter (transparent bakgrund, kant med 50 % opacitet, `FaintBrush`-text).
- Regel (beslutad i runda 2): **högst en primär knapp per vy, och den är vyns uppgift.** Fönster utan egen uppgift (Inställningar) har *Klar* som primär. Fönster med uppgift (Lokala modeller → *Hämta modell*, Lokal modell → *Läs hårdvara och modelluppgifter*, Hitta projekt → *Lägg till N projekt*, Avinstallera, guiden → *Nästa*/*Installera*, omstartsdialogen → *Starta om när den är ledig*) har uppgiftsknappen som primär och *Klar*/*Avbryt* som sekundära. En dialog där det säkra valet är att vänta (*Stäng alla* under pågående stängning) har **ingen** primär: *Avbryt* är secondary och *Tvångsstäng* danger. När uppgiftsknappen är inaktiv (ingen modell vald) får *Klar* inte bli primär i stället. Vyn har då ingen aktiv primär, och skälstexten (D9) förklarar varför.
- Konsekvensregel: samma ord har samma variant inom varje fönsterkategori (inställningsfönster respektive uppgiftsfönster). *Klar* är alltså alltid primär i Inställningar och alltid sekundär i uppgiftsfönster.
- Danger är obligatorisk där en farlig och en säker väg står sida vid sida (*Tvångsstäng*), och valfri för *Avinstallera*.
- `Button.main-action`: `VerticalContentAlignment="Center"` (texten ligger i dag ca 4 px för högt).
- **Stäng alla-flödet:** *Stäng alla* döljs bara i tomt läge. Annars visas den som inaktiv med tooltip ”Inga öppna flikar”. I `CloseAllWindow` står *Tvångsstäng* (danger) till vänster, skild från *Avbryt*. Varje rad har statusprick + text (Sparar… = info med spinner, Stängd = neutral, Misslyckades = danger). `close.confirm` skrivs om till tre punkter: vad som händer, vad som kan gå förlorat och vad du gör manuellt.

**Acceptanskriterier:**
- [ ] `main.png`: *Inställningar* och *Starta om alla* är secondary, och en inaktiv *Stäng alla* skiljer sig tydligt (transparent bakgrund) i båda teman.
- [ ] `ux-extra/main-empty.png`: *Stäng alla* syns inte.
- [ ] `--window close`: *Tvångsstäng* är röd och står till vänster, och raderna har statusprickar.
- [ ] `--window local-models`, `local-model`, `import`, `uninstall`, `restart`, `setup --section *`, `settings`: exakt en primary per vy, enligt regeln. `--window close`: ingen primary.
- [ ] Text i `main-action`-knapparna är vertikalt centrerad (±1 px).
- [ ] Bekräftelsetexten före Stäng alla är högst tre punkter och under 50 ord.

**Beroenden:** D1.
**Avgjort i runda 2:** grafikern godtar UX alternativ A (uppgiftsknappen är primär, *Klar* sekundär i uppgiftsfönster). Grafikerns mål, att samma ord ska se likadant ut, uppfylls genom konsekvensregeln per fönsterkategori ovan. Formuleringen ”*Klar* primär i alla fönster” från G-6 dras tillbaka.

---

## D3 – Typografisk skala
**priority: medium** – hindrar ingen uppgift, men 16 storlekar och syntetisk Bold gör att appen ser hopsatt ut.

**Bakgrund:** G-4. Lokala `FontSize`: 11, 11,5, 12 (105 st), 12,5, 13, 13,5, 14, 15, 16, 17, 19, 20, 22, 25, 26, 30. `FontWeight="Bold"` utan Bold-fil.

**Spec:** Klasserna `display` 28 SemiBold/34, `title` 20 SemiBold/26 (alla fönster, även huvudfönstret), `subtitle` 16 SemiBold/22, `sectionhead` 14 SemiBold/20, `body` 14/20, `body-small` 13/18, `caption` 12/16. Bara vikterna Regular, Medium och SemiBold (`Bold` → `SemiBold`). Görs i ett enda pass, och alla skärmbilder förnyas samtidigt.

**Acceptanskriterier:**
- [ ] Inga lokala `FontSize` utanför skalan (grep).
- [ ] Inga `FontWeight="Bold"`.
- [ ] Rubriken har samma storlek i `main.png`, `settings-*.png`, `setup-*.png`, `local-models.png`, `restart.png` och `uninstall.png`.

**Beroenden:** D1 (mjukt, för att undvika dubbla förändringar av skärmbilderna).

---

## D4 – Enhetlig ordlista: agent, modell, flik
**priority: high** – att byta modell är en kärnuppgift, och i dag heter agentkolumnen ”LLM” medan modellen ligger i kolumnen bredvid.

**Bakgrund:** UX-1. `main*.png` (kolumnen ”LLM”, underrubriken ”Välj AI och modell”), `setup-settings.png` (”AI-CLI”), `settings-agents.png` (”AI-agenter”), `local-models.png` (”Agenten är programmet … LLM:en är modellen”). Flik och tabb blandas: ”Lägg till undertabb”, ”1 undertabbar”, ”Tabbarna stängs”.

**Spec:** Ordlista: **Agent** (claude, codex …), **Modell**, **Flik**, **Underflik**. På engelska Agent, Model, Tab, Subtab. Ändringar: `col.llm` → ”Agent”. `main.subtitle` → ”Välj agent och modell för dagens arbete och starta.” Guiden: ”AI-CLI” → ”Agent”. Alla ”tabb” → ”flik”. Pluralformerna rättas (”1 underflik”, ”2 underflikar”). Ordlistan förs in i AGENTS.md eller strängriktlinjerna.

**Acceptanskriterier:**
- [ ] `main.png`/`main-en.png`: kolumnen heter ”Agent”/”Agent”, och underrubriken är uppdaterad.
- [ ] `--window setup --section settings`: etiketten heter ”Agent”.
- [ ] Ingen svensk sträng innehåller ”tabb” (grep i `Strings.sv.json`), och ”1 undertabbar” förekommer inte.
- [ ] Ordlistan finns dokumenterad.

**Beroenden:** inga (görs tidigt, eftersom D5, D6 och D13 påverkar samma strängar).

---

## D5 – Gör det tydligt vad Starta öppnar
**priority: high** – appens huvudlöfte är ”dagens start”. I dag står det ”Starta 3 flikar” bredvid fem ibockade rader, och ”Starta om alla (3)” räknar en annan mängd.

**Bakgrund:** UX-2. `main.png`, `main-restart.png`, `ux-extra/main-orch.png`. `ViewModels.cs` `ToStartCount`, `RestartableCount`. Kryssrutans tooltip ”Öppna den här fliken” och `AutomationProperties.Name` = bara namnet.

**Spec:**
- Kryssrutan på en levande rad (`IsAlive`) ritas med kontur i `MutedBrush` och bock i `TextBrush`, utan accentfyllning. Den ska inte se inaktiv eller ”delvis vald” ut. Rader som kommer att öppnas behåller accentfyllningen. Antalet accentfyllda kryssrutor är då samma som siffran på Starta.
- Tooltip och tillgängligt namn: icke levande rad: ”Ta med {namn} vid Starta”. Levande rad: ”{namn} körs redan och startas inte om. Ingår vid nästa start när fliken är stängd.”
- Kolumnrubriken ”Med” → ”Starta”.
- Starta-knappens tooltip räknar upp flikarna: ”Öppnar Cirrus, Delta”. Etiketten behåller texten ”Starta N flikar” (ingen siffra i en pill).
- *Starta om alla* blir secondary (D2), så att siffrorna inte konkurrerar.

**Acceptanskriterier:**
- [ ] `main.png`: antalet accentfyllda kryssrutor är samma som N i ”Starta N flikar”. Kryssrutor på levande rader har kontur men är tydligt ibockade.
- [ ] Kolumnrubriken heter ”Starta”.
- [ ] Ett test visar att Starta-tooltipen och kryssrutornas `AutomationProperties.Name` följer specen.
- [ ] Gäller i båda teman och på engelska (`main-en.png`).

**Beroenden:** D1, D2, D4.

---

## D6 – Huvudlistan: orkesterkolumn, statusbredd, ”saknas”, radrytm, flikfärger
**priority: medium** – status klipps av just där det spelar roll (”CLI avslutad (…”, ”codex (sakna”), och 240 px tomma kolumner tar platsen.

**Bakgrund:** UX-4, UX-7, G-7, G-9, G-13 (färgrotationen). `main.png`, `main-setup.png`, `ux-extra/main-orch.png`. `MainWindow.axaml` 125/143 `ColumnDefinitions="4,20,36,*,200,166,195,110,130"`, `Styles.axaml` 164–170 (`ComboBox.field` i mono), `Config.cs:18`/`Catalog.cs:41` (standardfärgen #1E66F5).

**Spec:**
- **Orkester:** de två växlarna ersätts av en combobox (168 px, så att ”Orkester, en gång” får plats utan avklippning; samma höjd som `ComboBox.field` men i `UiFont`, eftersom valen är svenska ord och inte värden) med valen **Av · Orkester · Orkester, en gång**. Kolumnen och rubriken döljs (bredd 0) när ingen synlig rad har `CanOrkester` eller `CanInstallOrkester`. Inga ”–” i tomma celler.
- **Statuskolumnen** får `*`, och Agent (166), Modell (195) och Orkester (168, när kolumnen visas) har fast bredd. Statustexten ”CLI avslutad ({0})” → ”Avslutad · fel {0}” eller kortare. Ingen avklippning med standardbredd.
- **”saknas”:** inte längre ett textsuffix i comboboxen. I stället visas en varningsprick eller triangel (`WarningBrush`) efter agentnamnet, med tooltip och tillgängligt namn ”codex saknas”.
- **Mono bara på värden** (sökväg, CLI-namn, modell-id), aldrig på omgivande ord.
- **Radrytm:** undertabbens StackPanel får `IsVisible="{Binding Expanded}"`, och marginalen flyttas dit. `Border.row` får `MinHeight` 52. Alla rader får samma avstånd.
- **×-knappen** på underfliken får ingen grå ruta (`Button.icon:disabled` = transparent bakgrund).
- **Flikfärger:** nya projekt får nästa färg i paletten, i stället för att alla får #1E66F5.

**Acceptanskriterier:**
- [ ] `main.png` (svenska): ingen statustext är avklippt. Orkesterkolumnerna syns inte.
- [ ] `--fake-orchestra-idle`: orkesterkolumnen visas med en combobox på Atlas och ingenting på övriga rader.
- [ ] `--fake-setup`: agentlistan visar ”codex” + varningsmarkering, utan avklippt text.
- [ ] Avståndet mellan raderna är lika (±1 px) i `main.png` och `main-restart.png`.
- [ ] Ett skärmbildstest med de längsta svenska tillståndssträngarna visar dem utan avklippning.
- [ ] Ett test visar att två nyss tillagda projekt får olika flikfärger.

**Beroenden:** D1 (WarningBrush), D4 (strängar).

---

## D7 – Fel och återhämtning: felbanderoll med åtgärd
**priority: high** – ett fel som stoppar hela starten visas i dag som liten röd text längst ner, utan åtgärd.

**Bakgrund:** UX-3, G-3. `main-error.png` (”Kunde inte starta Windows Terminal”), raden ”CLI avslutad (1)” i `main.png`. `MainWindow.axaml` 257–259.

**Spec:**
- En ny `danger`-variant av `Border.update`: kant och ikon `DangerBrush`, bakgrund `DangerSurfaceBrush` (D1), brödtext i `TextBrush` (inte röd text), varningstriangel i ikonkolumnen, en mening om orsaken och 1–2 åtgärder (”Försök igen”, ”Visa detaljer” och, när det gäller, ”Öppna Inställningar → Miljö och terminal”) samt stängknapp.
- Statusraden längst ner används bara för neutral kvittens (`MutedBrush`).
- På raden: när statusen är avslutad med fel öppnar ett klick på statusen Peek med de sista raderna (D8), och ↻ är den primära åtgärden där.

**Acceptanskriterier:**
- [ ] `--fake-status "error:Kunde inte starta Windows Terminal"`: felet visas i en banderoll överst med en åtgärdsknapp och stängknapp, inte i sidfoten. Texten har ≥4,5:1 i båda teman.
- [ ] `--fake-status "text"`: neutral kvittens visas kvar i sidfoten i `MutedBrush`.
- [ ] Ett test visar att varje felkod som visas i banderollen har minst en åtgärd.

**Beroenden:** D1, D2.

---

## D8 – Live Peek: glyfer, opak yta, rubrik
**priority: high** – Peek är en huvudfunktion, och i dag visar den tomma rutor (tofu) och i mörkt läge syns fönstret bakom genom popupen.

**Bakgrund:** UX-8, G-5. `main-output.png`, `grafik-extra/main-dark-update-output.png`. `StatusPeekView.axaml`, `TabOutputView.cs`, `Styles.axaml` 93–115, `OutputBackgroundBrush`.

**Spec:**
- `MonoFont` med fallback-typsnitt: `…#IBM Plex Mono, Cascadia Mono, Consolas, DejaVu Sans Mono` (fallback i stället för att översätta enstaka tecken, eftersom CLI:erna använder ⎿, ⏺, ✻ m.fl.). Systemtypsnitten skiljer sig mellan Windows och Linux. Om ⎿ saknas i alla tillgängliga typsnitt på någon av plattformarna bäddas ett litet delmängdstypsnitt (OFL) med de tecknen in under `Assets/Fonts`.
- Popup: opak `SurfaceBrush`, 1 px `BorderBrush`, `RadiusLarge`, `BoxShadow="0 8 24 0 #2E000000"`. Utdatafältet `BackgroundBrush`.
- Rubrik: statusprick + ”Atlas · claude · Körs”.
- Valfritt (medium, får skjutas upp): knappen ”Gå till fliken” eller ”Kopiera”.

**Acceptanskriterier:**
- [ ] `--fake-output`: inga tomma rutor, och ⎿ visas (på både Linux och Windows).
- [ ] `--fake-output --theme dark`: inget av fönstrets innehåll syns genom popupen. Popupen har kant och skugga.
- [ ] Rubriken visar prick, fliknamn, agent och status. Detsamma gäller `--fake-output 1` (Watching).
- [ ] Bekräftas även i riktiga appen på Windows: att popupen är opak i mörkt läge (en manuell kontroll, eftersom headless ritar popupen på fönsterramen).

**Beroenden:** D1.

---

## D9 – Lokala modeller och modellråd i steg
**priority: high** – uppgiften ”byt till en lokal modell” stannar i dag på en grå knapp utan förklaring, och layouten överlappar.

**Bakgrund:** UX-6, UX-12, G-6. `local-models.png`, `local-model.png`, `grafik-extra/local-models-dark.png`. `LocalModelsWindow.axaml`, `LocalModelWindow.axaml`.

**Spec:**
- **Lokala modeller** i tre numrerade steg med samma stegcirklar som installationsguiden: **1 Server** (adress, *Kontrollera*, status ✓/✗), **2 Välj modell**, **3 Hämta** (sammanfattning och knapp).
- Rekommendationen visas som en callout: `SurfaceBrush`, `RadiusLarge`, padding 14,12, `BoltIcon` i accentfärg och modell-id i mono 13.
- Listan får en egen rad med `*`-höjd och `MinHeight` 180, och ritas aldrig över annat innehåll. Vald rad: `RowHoverBrush` + 1 px accentkontur, radie 6. Ingen lila fyllning. Modell-id i listan i `MonoFont` 13, och storlek och lämplighet i `caption` `MutedBrush`.
- Sidfoten ligger utanför scrollytan. *Hämta modell* är primär (D2). När den är inaktiv står skälet i `caption` `MutedBrush` till vänster om knappen (”Kontrollera servern först”, ”Redan installerad”).
- En enhet (GB) överallt. Dubbla rader tas bort (”Installerad med exakt denna tagg …”).
- **Lokal modell (råd):** ett resultatkort överst med samma callout-komponent och en 3 px kant till vänster i `SuccessBrush`/`WarningBrush`/`DangerBrush` plus ikon (Passar / Osäker / Passar inte) och en mening om varför. Sedan indata (modell, adress). Sist en hopfällbar sektion ”Mätvärden och begränsningar” (samma Expander-stil som i Allmänt). Kryssrutan ”Jag har kontrollerat …” formuleras om så att effekten står i texten, eller tas bort om den inte påverkar rådet.

**Acceptanskriterier:**
- [ ] `--window local-models` (och `--section cpu|unknown|missing|download`): inget innehåll överlappar eller klipps vid standardstorleken. Serverfältet och *Kontrollera* syns helt.
- [ ] *Hämta modell* är inaktiv bara tillsammans med en synlig skälstext.
- [ ] Ingen ”GiB” finns i fönstren.
- [ ] `--window local-model --section fits|limited|remote`: resultatkortet ligger överst med rätt semantisk färg och ikon, och mätvärdena är hopfällda som standard.
- [ ] Båda teman.

**Beroenden:** D1, D2 (D3 mjukt).

---

## D10 – Ikoner och radkontroller: betydelse, ritning, tangentbord
**priority: medium** – fel ikon kan byta projektkatalog eller starta ett nytt samtal i stället för att återuppta. Kugghjulet läses som temaväxling, och flikordningen går bara att ändra med musen.

**Bakgrund:** UX-5, UX-14 (resten), G-11, G-7 (grip). `main.png`, `settings-projects.png`. `App.axaml` `GearIcon`, `MainWindow.axaml` 145, 155–158, 164–196, `Styles.axaml` 189–194 (`Button.remove` opacitet 0,35), `MainWindow.axaml.cs` (dra och släpp utan tangentbordsalternativ).

**Spec:**
- **Betydelse:** mappikonen = ”Öppna i Utforskaren” i både huvudfönstret och Inställningar. Byte av katalog = penna eller textknappen ”Byt…”. ↗ används inte för mappar.
- ▷ ersätts av textlänken **”Fortsätt”** (`Button.link`, 12 px), som bara visas när det går att fortsätta. Tillgängligt namn: ”Fortsätt senaste samtalet i {namn}”. ↻ behålls som ikon.
- **Ritning:** ett riktigt kugghjul (t.ex. Lucide ”settings”), plus `ChevronRightIcon`, `ChevronDownIcon` och `GripIcon` som vektorer i stället för textglyferna ▸ ▾ ⋮. ↳ före underflikar tas bort. Underfliken markeras i stället med 20 px indrag och en 1 px vertikal linje i `BorderBrush` från förälderns rand. Två storlekar: 16 px i knappar och 12 px inline, med 1,5 px streck.
- **Tillstånd utan opacitet:** ikonknappar ritas med full opacitet i `FaintBrush` (≥4,5:1 efter D1) och i `TextBrush` vid hovring och fokus. Låg opacitet används bara för inaktiv.
- **Tangentbord:** Alt+↑/↓ flyttar fokuserad rad. En snabbmeny på raden: Flytta upp / Flytta ned / Starta om / Fortsätt samtal / Öppna i Utforskaren. Grip-ikonen visar fokusring.

**Acceptanskriterier:**
- [ ] `main.png` och `settings-projects.png`: mappikonen har samma betydelse i båda (kontrolleras via tooltip och tillgängligt namn i ett test).
- [ ] `--fake-restartable 2`: rader där man kan fortsätta visar ↻ + ”Fortsätt” som text. Inget ▷.
- [ ] `main.png`: inga textglyfer ▸ ▾ ⋮ ↳. Kugghjulet har kuggar.
- [ ] Ta bort-knappar (`settings-projects.png`, underflikens ×) har ≥3:1 utan hovring.
- [ ] Ett test: Alt+↓ på första raden flyttar den ett steg och sparar ordningen.

**Beroenden:** D1.

---

## D11 – Inställningar: gemensam sidmall och ny placering av innehåll
**priority: medium** – användaren hittar inte inställningar där uppgiften hör hemma (auto-clear jämfört med auto-compact, basmappen långt från *Hitta projekt*), och flikarna ser olika ut.

**Bakgrund:** UX-10, G-8. `settings-*.png`, `settings-min.png`. `SettingsWindow.axaml` 19, 179, 527, `Styles.axaml` 27–48.

**Spec:**
- **Sidmall** för alla flikar: titel (`title`) + ingress (`body-small` muted) + sektioner. Allmänt och Utseende får också en titel.
- **En SideNav-stil** för Optimering och AI-agenter: 168 px kolumn, objekt 36 px, 13 px Medium. Valt = `RowHoverBrush` + 3 px accentstreck till vänster + SemiBold. Innehållet börjar på samma x i båda.
- Huvudflikar: ovalda Regular `MutedBrush`, valda SemiBold `TextBrush` + 2 px accentlinje.
- **Ny placering av innehåll:** *Automatisk rensning* flyttas till Optimering → ny sektion ”Kontext” bredvid Auto-compact, som ett val: Av / Rensa vid inaktivitet / Sammanfatta. Basmappen för *Hitta projekt* flyttas till fliken Projekt, intill knappen. ”Optimering” byter namn till ”Minne och kontext”.
- Vid 640×400 har projektlistan en `MinHeight` som visar minst tre rader. Knapparna ligger under listan inuti scrollytan.

**Acceptanskriterier:**
- [ ] `--window settings --tab *`: alla fem flikar börjar med en titel på samma y-position.
- [ ] Optimering och AI-agenter har identisk undernavigering, och innehållet börjar på samma x.
- [ ] `--tab general` innehåller inte auto-clear eller basmappen. `--tab optimization --section …` visar Kontext med de tre valen. `--tab projects` visar basmappen.
- [ ] `--width 640 --height 400`: minst tre projektrader syns.

**Beroenden:** D3, D10 (mjukt).

---

## D12 – Agentöversikten: begripligt språk, åtgärder, logotypplattor
**priority: medium** – översikten svarar inte på ”är agenten redo?” och erbjuder ingen nästa åtgärd för en agent som saknas.

**Bakgrund:** UX-11, G-14. `settings-agents.png`, `ux-extra/agents-missing.png`, `grafik-extra/settings-agents-dark.png`. `AgentIcon.cs`, `assets/agents/*`.

**Spec:**
- Kortet: logotyp + namn (14 SemiBold), statusprick + **Redo / Saknas / Behöver uppdateras** (13), **Behörigheter**: ”Frågar före verktyg” / ”Godkänner automatiskt” (13 muted), sist en länk ”Lokal modell: få råd →” eller en sekundärknapp *Installera* när agenten saknas.
- ”Inget alias”, ”Egen skalfunktion”, ”Auto Approve” och ”Inte verifierat” flyttas till detaljsidan.
- Logotypplatta i korten: 28×28, `SurfaceStrongBrush`, radie 6, logotypen 16 px. Interpreter får en fylld variant (inte en tom cirkel). I agentlistan i huvudfönstret läggs ingen platta förrän den har provats i 14 px.

**Acceptanskriterier:**
- [ ] `--tab agents` och `--tab agents --section missing`: varje kort har exakt raderna Status och Behörigheter. Saknade agenter har *Installera*.
- [ ] Ingen fet text som inte är en rubrik.
- [ ] Interpreters logotyp går inte att förväxla med en radioknapp (fylld).
- [ ] Båda teman.

**Beroenden:** D1, D10.

---

## D13 – Installationsguiden: agentstatus, projektval, liten ikon
**priority: medium** – första körningen kan sluta med ”agent saknas” direkt efter *Öppna BoosterAI*, och projekten går inte att välja bort i guiden.

**Bakgrund:** UX-13, G-9 (mono), G-13 (ikon ≤32 px). `setup-settings.png`, `setup-projects.png`, `setup-install.png`, `uninstall.png`, `import.png`. `InstallWizardView.axaml` 139–150.

**Spec:**
- Agentlistan (steg 2) visar status med prick + text i UiFont: ”claude · installerad” / ”codex · saknas, kan installeras efter start”. Förvald är en agent som redan är installerad.
- Sammanfattningen (steg 4) nämner saknade agenter.
- Steg 3: listan med projekt blir kryssrutor med samma komponent som `import.png`.
- Sökvägar i sammanfattningen och i avinstallationsdialogen visas i mono 13 utan att brytas mitt i ord (avkortning eller horisontell scroll).
- Rubrikikonen i guiden (28 px) använder `boosterai-small` (bara blixten).
- Låg del: ikonen gäller även andra visningar ≤32 px.

**Acceptanskriterier:**
- [ ] `--window setup --section settings`: varje agent visar installationsstatus.
- [ ] `--section projects`: projekten har kryssrutor och ser ut som `import.png`.
- [ ] `--section install`: saknade agenter nämns och sökvägarna är i mono.
- [ ] `--window uninstall`: ingen sökväg är bruten mitt i ett ord.
- [ ] Guidens rubrikikon saknar texten ”ai”.

**Beroenden:** D1, D4.

---

### Inte med som egna utkast
- G-7 radrytm (low) ingår i D6. G-12 radier ingår i D1. G-13 ingår i D6 (färger) och D13 (ikon). G-10 ingår i D1.
- Inga fynd är helt tillbakadragna.

---

## Ändringar i runda 2 av grafikern
- **D2, öppen designfråga avgjord:** alternativ A (UX). Regeln skärptes till ”högst en primär”: *Stäng alla*-dialogen har ingen primär, och en inaktiv uppgiftsknapp gör inte *Klar* primär. En konsekvensregel per fönsterkategori lades till. Omstartsdialogen och Lokal modell lades till i listan över uppgiftsfönster.
- **D1/D2, kontrastfel i mitt eget förslag:** vit text på #E5484D ger bara 3,9:1, så danger-knappen i mörkt läge följde inte AA. Den nya tokenen `DangerFillBrush` är #C62F35 i båda teman (5,4:1).
- **D1:** mörkt `FaintBrush` är nu fastställt till #8A8EA0. #7C8091 ger 4,2:1 mot den nya `SurfaceBrush` #1C1E26. Ljust #6B6F80 kontrollerades: 5,0:1 mot vitt och 4,6:1 mot `SurfaceBrush`. Lade till `DangerSurfaceBrush`. Förtydligade att `BorderBrush` (1,4:1) bara är dekorativ, att fältkanter måste ha ≥3:1, och lade till ett kriterium för det.
- **D6:** orkestercomboboxen ökades från 130 till 168 px (”Orkester, en gång” får annars inte plats) och använder `UiFont` i stället för mono, enligt mono-regeln.
- **D7:** banderollens brödtext är `TextBrush`. Bara kant och ikon är röda.
- **D8:** tog bort `monospace` ur fallback-listan (det är inget Avalonia-familjenamn). Lade till en reservplan med ett inbäddat delmängdstypsnitt och verifiering på båda plattformarna.
- **D9:** modell-id i listan visas i mono 13, så att Lokala modeller följer samma mono-regel som huvudfönstret.
- **D10:** angav vad som ersätter ↳ (kriteriet krävde att den togs bort utan att säga vad som kommer i stället).
- **Prioriteter:** inga ändringar. D1 som high godtas med UX:s motivering (AA-brister). D3 medium och D6 medium med färgrotationen står kvar.
- **Ingen kvarstående oenighet.**
