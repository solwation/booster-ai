# UX-granskning BoosterAI 0.36.0 – interaktionsdesign

Granskare: UX (interaktionsdesign, IA, användbarhet, tillgänglighet)
Underlag: alla `*.png` i `uxreview-evidence/`, extra headless-renderingar i `uxreview-evidence/ux-extra/` (`main-empty.png`, `main-orch.png`, `agents-missing.png`), samt `src/BoosterAI/*.axaml`, `ViewModels.cs`, `CloseAll.cs`, `App.axaml`, `Styles.axaml`, `Resources/Strings.sv.json`.

Helhetsintryck: huvudflödet (bocka i, välj agent och modell, Starta) är tydligt och snabbt. Starta-knappen är tydligt primär, tomt läge och installationsguiden fungerar bra, och omstartsdialogen (”Starta om när den är ledig”) är ett mycket bra mönster. De största problemen gäller **begrepp** (agent/LLM/CLI/flik/tabb), **vad Starta faktiskt gör**, **fel och återhämtning** och **de nya fönstren för lokala modeller**, som har en helt annan densitet och ton än resten av appen.

Prioritet: high / medium / low. Storlek: S (timmar), M (1–3 dagar), L (mer än så).

---

### UX-1 – Kolumnen ”LLM” innehåller agenter, inte LLM:er. Begreppen skiljer sig mellan fönstren
- **Skärm/fil:** `main*.png` (kolumnrubriken ”LLM” med claude/codex/agy), `setup-settings.png` (”AI-CLI”), `settings-agents.png` (”AI-agenter”), `local-models.png` (”Agenten är programmet … LLM:en är modellen den använder”). `Strings.sv.json`: `col.llm`, `main.subtitle` (”Välj AI och modell”). Samma sak med flik/tabb: ”Lägg till undertabb”, ”1 undertabbar · samma mapp” (fel böjning), ”Tabbarna stängs” i `close.confirm` men ”flik” överallt annars.
- **Problem:** Appen definierar själv agent och LLM som olika saker, men huvudfönstret kallar agentkolumnen ”LLM” och har bredvid den en kolumn för den verkliga modellen. Samma sak heter alltså ”LLM”, ”AI”, ”AI-CLI” och ”AI-agent” i fyra olika fönster.
- **Varför:** Att byta modell är en kärnuppgift. Med två kolumner som båda låter som ”modell” är det oklart vilken man ska ändra, och nya användare kan inte koppla ihop huvudfönstret med Inställningar → AI-agenter.
- **Förslag:** Bestäm en ordlista och använd den överallt: **Agent** (claude, codex …), **Modell** (LLM), **Flik** och **Underflik**. Byt namn på kolumnen till ”Agent”, underrubriken till ”Välj agent och modell för dagens arbete” och guidens ”AI-CLI” till ”Agent”. Rätta pluralformen (”1 underflik”). Lägg in ordlistan i styrdokumentet för strängar.
- **Prioritet:** high · **Storlek:** S

### UX-2 – Det går inte att se vad Starta kommer att öppna
- **Skärm/fil:** `main.png` (fem rader ibockade, men knappen säger ”Starta 3 flikar” och bredvid står ”Starta om alla (3)”), `main-restart.png` (”Starta 5 flikar” och ”Starta om alla (5)”). `ViewModels.cs` `ToStartCount` (ibockade och inte levande), `RestartableCount`. Rubriken ”Med”, och kryssrutans tooltip säger ”Öppna den här fliken”.
- **Problem:** Siffran på Starta följer en dold regel: ibockade flikar som inte redan körs. ”Starta om alla (n)” räknar en annan mängd flikar, men siffrorna ser likadana ut. Kryssrutan på en flik som redan körs påverkar ingenting just nu, men ser ut precis som en kryssruta som gör det.
- **Varför:** ”Dagens start” är appens huvudlöfte. Den som undrar vilka flikar som öppnas och om de som redan körs startas om kan avbryta pågående agentarbete eller tro att något saknas.
- **Förslag:** (a) Visa på varje rad vad som händer vid Starta, till exempel en diskret etikett ”öppnas” på de rader som räknas, eller nedtonad kryssruta med tooltip ”Körs redan – startas inte om” på levande rader. (b) Byt rubriken ”Med” mot ”Starta” och sätt `AutomationProperties.Name` till ”Ta med {namn} vid Starta”. (c) Låt Starta-knappens tooltip räkna upp flikarna (”Öppnar Cirrus, Delta”). (d) Se till att ”Starta om alla” inte konkurrerar visuellt, se UX-9.
- **Prioritet:** high · **Storlek:** M

### UX-3 – Fel visas långt från orsaken och utan väg vidare
- **Skärm/fil:** `main-error.png` (”Kunde inte starta Windows Terminal” i liten röd text längst ner till vänster, ovanför versionsnumret). `MainWindow.axaml` rad 257–259. Raden ”CLI avslutad (1)” i `main.png`.
- **Problem:** Ett fel som stoppar hela arbetsdagen visas som en statusrad i 12–13 px, utan förklaring, åtgärd eller stängknapp. Den röda texten konkurrerar med versionsnumret i stället för med det användaren gjorde. På radnivå visar ”CLI avslutad (1)” en rå exitkod utan tolkning och utan direkt väg till utdata eller logg.
- **Varför:** Återhämtning är ett av de flöden användaren oftast hamnar i, till exempel när WT saknas, WSL inte har startat eller agenten kraschar. Utan nästa steg gissar användaren eller ger upp.
- **Förslag:** Visa blockerande fel i samma bannerkomponent som uppdaterings- och setup-raden (`Border.update`), med ikon, en mening om orsaken och 1–2 åtgärder (”Försök igen”, ”Visa detaljer”, ”Öppna Inställningar → Miljö och terminal”) samt stäng. Behåll statusraden för neutral kvittens. På raden: visa ”Avslutad · fel” och gör ett klick på statusen till ett sätt att se de sista utdataraderna (Peek finns redan, se UX-8) med ”Starta om” som primär åtgärd.
- **Prioritet:** high · **Storlek:** M

### UX-4 – Fasta kolumnbredder klipper av viktig status
- **Skärm/fil:** `main.png` (”CLI avslutad (…”), `main-setup.png` (”codex (sakna”, ”agy (saknas” i agentlistan), jämför `main-en.png` där ”CLI exited (1)” får plats. `MainWindow.axaml` rad 125/143 `ColumnDefinitions="4,20,36,*,200,166,195,110,130"`.
- **Problem:** Status- och agentkolumnerna har fasta pixelbredder anpassade för engelska. På svenska klipps just den information som skiljer tillstånden åt: exitkoden och ”(saknas)”.
- **Varför:** Den avklippta delen är skälet till att användaren behöver agera. ”codex (sakna” är dessutom svårt att läsa.
- **Förslag:** Låt statuskolumnen växa på bekostnad av den ofta tomma orkesterytan (UX-7). Visa ”saknas” som en varningsikon med tooltip och tillgängligt namn i stället för som textsuffix i listan. Lägg till ett skärmbildstest på svenska med de längsta tillståndssträngarna.
- **Prioritet:** medium · **Storlek:** S

### UX-5 – Radernas ikonknappar är tvetydiga och inkonsekventa mellan fönstren
- **Skärm/fil:** `main.png`: ↗ betyder ”öppna katalogen i Utforskaren”, ↻ betyder ”starta om”, ▷ betyder ”fortsätt senaste konversationen”, ⋮ betyder ”dra för att ändra ordning”. `settings-projects.png`: mappikonen betyder ”välj/byt katalog” och ↗ betyder ”öppna”. `Strings.sv.json`: `folder.open.tip`, `restart.continue.tip`.
- **Problem:** En mappikon betyder i alla filhanterare ”öppna mapp”, men här byter den katalog. ↗ ser ut som en extern länk. ▷ (play) bredvid ↻ läses som ”starta”, inte ”fortsätt samtalet”. Skillnaden (nytt samtal eller samma samtal) är viktig men syns bara i tooltip.
- **Varför:** Fel klick kan byta projektkatalog eller starta ett nytt samtal i stället för att återuppta. Tooltips nås inte vid touch och upptäcks sent.
- **Förslag:** Använd mappikonen för ”Öppna i Utforskaren” i båda fönstren, och en penna eller textknappen ”Byt…” för att byta katalog. Ersätt ↻/▷ med en delad knapp ”Starta om ▾” med valen ”Nytt samtal” och ”Fortsätt samtal”, eller låt ▷ få en kort textetikett (”Fortsätt”). Håll ikonbetydelserna konsekventa i alla fönster.
- **Prioritet:** medium · **Storlek:** M

### UX-6 – Lokala modeller: layouten överlappar och huvudknappen är grå utan förklaring
- **Skärm/fil:** `local-models.png`. Fältet ”Lokal Ollama-server” och knappen ”Kontrollera” klipps under sammanfattningsraden. Modellistan klipps mitt i en rad. ”Hämta modell” är inaktiv trots att en modell är vald. Storlekar visas omväxlande i ”GB” och ”GiB”. `LocalModelsWindow.axaml`.
- **Problem:** Fönstret har en scrollyta, en sticky sammanfattning och en knappyta som ritas ovanpå varandra. Användaren ser inte vad som blockerar nedladdningen (servern okontrollerad? redan installerad?). Texten ”Installerad med exakt denna tagg på vald server” står två gånger.
- **Varför:** Uppgiften ”byt till en lokal modell” stannar på en grå knapp. Överlappande fält ser trasiga ut och minskar förtroendet för hela funktionen.
- **Förslag:** Gör om fönstret till tre tydliga steg: **1 Server** (adress, Kontrollera, status ✓/✗), **2 Välj modell** (listan med rekommendation markerad), **3 Hämta** (sammanfattning och knapp). Ge sidfoten en egen rad utanför scrollytan. Skriv skälet bredvid en inaktiv ”Hämta modell” (”Redan installerad” eller ”Kontrollera servern först”). Använd en enhet (GB) överallt.
- **Prioritet:** high · **Storlek:** M

### UX-7 – Orkester- och engångskolumnerna tar plats även när de är tomma
- **Skärm/fil:** `main.png` och `main-dark.png` (rubrikerna ”Orkester” och ”Engångskörning” utan innehåll, cirka 240 px), `ux-extra/main-orch.png` (bara en rad har reglage). `ViewModels.cs` `CanOrkester`. `once.tip` säger ”Kräver att orkestern är på”.
- **Problem:** Avancerade kolumner visas för alla, även när inget projekt har orkester. ”Engångskörning” är ett beroende läge av Orkester men presenteras som en jämbördig, fristående växel.
- **Varför:** Tomma rubriker får användaren att leta efter något som saknas, och platsen behövs för status (UX-4). Två växlar där den ena bara gäller när den andra är på ger ogiltiga kombinationer.
- **Förslag:** Dölj båda kolumnerna när ingen rad har `CanOrkester` eller `CanInstallOrkester`. Där de visas: slå ihop dem till en lägesväljare ”Orkester: Av / Interaktiv / Engång”.
- **Prioritet:** medium · **Storlek:** S

### UX-8 – Live Peek: trasiga tecken och ingen uppgift om vilken flik som visas
- **Skärm/fil:** `main-output.png`. Rutor (□) där Claude ritar `⎿`, och rubriken säger bara ”Körs”. Popupen täcker raderna under och deras kontroller. `StatusPeekView.axaml`, `TabOutputView.cs`.
- **Problem:** Typsnittet saknar glyfer för boxtecken som CLI:erna använder. Popupen säger inte vilken flik eller agent den gäller, vilket gör den mindre användbar när man går igenom flera rader med tangentbordet.
- **Varför:** Peek finns för att användaren ska kunna kontrollera status utan att byta fönster. Trasiga tecken och okänd källa gör det svårare.
- **Förslag:** Ange ett monospace-typsnitt med reservtypsnitt som har boxtecken (Cascadia Mono, DejaVu Sans Mono). Skriv rubriken som ”Atlas · claude · Körs”. Lägg till knappen ”Gå till fliken” om det går att fokusera WT-fliken, annars ”Kopiera”.
- **Prioritet:** medium · **Storlek:** S

### UX-9 – Knappnivåerna i sidfoten och i stängningsflödet skiljer inte på farligt och ofarligt
- **Skärm/fil:** `main.png` (”Starta om alla (3)” är grå, ”Stäng alla” är inaktiv men ser nästan likadan ut), `ux-extra/main-empty.png` (inaktiv ”Stäng alla” visas fast det inte finns några projekt), `close.png` (”Tvångsstäng” ser ut precis som ”Avbryt”, och raderna saknar förloppsindikator), `close.confirm` i `Strings.sv.json` (cirka 70 ord i ett stycke).
- **Problem:** Inaktiv och sekundär knapp går knappt att skilja åt. Den destruktiva ”Tvångsstäng” saknar varningsfärg. Bekräftelsen före Stäng alla är en textvägg där det viktiga (pågående turer avbryts, osparat arbete kan försvinna) försvinner.
- **Varför:** Vid arbetsdagens slut kan ett felklick kosta osparat arbete. Användaren behöver se direkt vad som är riskabelt och om stängningen går framåt.
- **Förslag:** Dölj ”Stäng alla” när inget är öppet i stället för att visa den inaktiv. Ge ”Tvångsstäng” en danger-stil och lägg den till vänster, skild från den säkra vägen. Visa en spinner och förfluten tid per rad med status ”Sparar…”. Skriv om `close.confirm` till 3 punkter: vad som händer, vad som kan gå förlorat, vad du gör manuellt.
- **Prioritet:** medium · **Storlek:** S

### UX-10 – Inställningar: Allmänt är en blandning och strukturen skiljer sig mellan flikarna
- **Skärm/fil:** `settings-general.png` (fönsterbeteende, basmapp för Hitta projekt, Claudes auto-clear och uppdateringar i en lång scroll), `settings-optimization.png` och `settings-agents.png` (vänstermeny), `settings-min.png` (bara cirka 1,5 projektrad syns vid 640×400).
- **Problem:** Auto-clear handlar om kontexthantering, men ligger i Allmänt med en länk till Optimering → Auto-compact som gör nästan samma sak. Basmappen för Hitta projekt ligger i Allmänt, medan knappen Hitta projekt ligger under Projekt. Två flikar har undermeny och två har det inte. ”Optimering” avslöjar inte att fliken innehåller RAM och WSL.
- **Varför:** Användaren hittar inte inställningen där uppgiften hör hemma (”var stänger jag av att Claude rensas?”), och dubbla platser för samma sak skapar osäkerhet.
- **Förslag:** Flytta Automatisk rensning till en sektion ”Kontext” i Optimering bredvid Auto-compact, som ett val: Av / Rensa / Sammanfatta. Flytta basmappen till fliken Projekt, intill ”Hitta projekt”. Byt namn på Optimering till ”Minne och kontext”. Ge projektlistan en minsta höjd vid minsta fönsterstorlek och låt knapparna scrolla med.
- **Prioritet:** medium · **Storlek:** M

### UX-11 – Agentöversikten använder internt språk och saknar åtgärder
- **Skärm/fil:** `settings-agents.png`, `ux-extra/agents-missing.png`: ”Auto Approve”, ”Inget alias”, ”Egen skalfunktion”, ”Inte verifierat”, ”Lokalt: välj modell för ett råd”, ”Ej installerad —” utan installationsknapp.
- **Problem:** Korten visar implementationsdetaljer (alias, skalfunktion) i stället för vad användaren bryr sig om: är agenten redo, och kör den med eller utan frågor? Ett saknat program erbjuder ingen nästa åtgärd. Den fetade ”Lokalt: …” ser ut som en rubrik men fungerar som en dold länk.
- **Varför:** Översikten är den naturliga platsen att svara på frågan ”varför kan jag inte välja OpenHands?”. Nu måste användaren klicka in på varje kort.
- **Förslag:** Två rader per kort: **Status** (Redo / Saknas → [Installera] / Behöver uppdateras) och **Behörigheter** (”Frågar före verktyg” / ”Godkänner automatiskt”). Gör ”Lokal modell: få råd →” till en tydlig länk. Flytta alias och skalfunktion till detaljsidan.
- **Prioritet:** medium · **Storlek:** S

### UX-12 – Lokal modell-rådet: slutsatsen ligger mitt i en textvägg och man måste intyga något i en kryssruta
- **Skärm/fil:** `local-model.png` (`LocalModelWindow.axaml`): rubriken ”Val när rådfönstret öppnades: …”, fem stycken förbehåll, slutsatsen (”Minnesutrymmet verkar lämpligt …”) i en indragen textblock utan visuell status, och kryssrutan ”Jag har kontrollerat att agenten använder denna server …”.
- **Problem:** Svaret på frågan ”räcker min dator?” har samma vikt som brödtexten. Förbehållen kommer före indata. Kryssrutan lägger ansvaret på användaren utan att förklara vad som ändras om den kryssas i.
- **Varför:** Användaren vill ha ett ja, nej eller kanske och vad hen ska göra. Nu måste hen läsa cirka 200 ord för att hitta det.
- **Förslag:** Ett resultatkort överst med ikon och färg (Passar / Osäker / Passar inte) och en mening om varför. Under det: indata (modell, adress). Allra sist: en hopfällbar sektion ”Mätvärden och begränsningar”. Formulera om kryssrutan som ett val med förklarad effekt, eller ta bort den om den inte ändrar rådet.
- **Prioritet:** medium · **Storlek:** M

### UX-13 – Guiden låter användaren välja en agent utan att visa om den är installerad
- **Skärm/fil:** `setup-settings.png` (AI-CLI: claude, utan status), `setup-install.png` (”3 projekt läggs till med claude”), sedan `main-setup.png` där första vyn efter installationen kan vara en gul rad om att agenter saknas. `setup-projects.png`: listan ”3 projekt läggs till” är skrivskyddad och kan bara ändras genom att söka om.
- **Problem:** Första körningen kan bli ”installera, öppna, upptäck att agenten saknas”. Projektlistan i guiden går inte att justera på plats.
- **Varför:** Onboarding är det första intrycket. Ett fel direkt efter ”Klart” upplevs som att installationen misslyckades.
- **Förslag:** Visa ”(installerad)” eller ”(saknas – kan installeras efter start)” i agentlistan och i sammanfattningen före Installera, och föreslå som standard en agent som redan är installerad. Gör projektlistan i steg 3 till kryssrutor, samma komponent som i `import.png`.
- **Prioritet:** medium · **Storlek:** S

### UX-14 – Tangentbord och kontrast: dragordning, nästan osynliga ta bort-knappar och svag rubriktext
- **Skärm/fil:** `MainWindow.axaml` rad 145 (⋮-handtag, ordningen ändras bara genom att dra, `MainWindow.axaml.cs` saknar tangentbordsalternativ). `Styles.axaml` rad 189–194 (`Button.remove` har opacitet 0,35 tills muspekaren är över). `App.axaml` rad 35 `FaintBrush #7F8394` på vitt ger cirka 3,8:1 för kolumnrubriker i 12 px (`TextBlock.colhead`). Kryssrutornas `AutomationProperties.Name` är bara flikens namn.
- **Problem:** Flikordningen går inte att ändra med tangentbord eller skärmläsare. Ta bort-knappar med 35 % opacitet syns inte för den som inte använder mus, och klarar inte 3:1 för icke-textelement. Kolumnrubrikerna når inte WCAG AA 4,5:1 i ljust tema. En skärmläsare läser ”Atlas, kryssruta” utan att säga vad kryssrutan gör.
- **Varför:** Grundläggande tillgänglighet. Det gäller också vanliga användare på laptopskärmar i starkt ljus.
- **Förslag:** Lägg till Alt+↑/↓ (eller ”Flytta upp/ned” i en snabbmeny) på raden. Visa ta bort-knappar med minst 3:1 kontrast hela tiden, och mer framträdande vid hover. Mörka `FaintBrush` i ljust tema till cirka `#6B6F80` (≥4,5:1). Sätt beskrivande tillgängliga namn (se UX-2b). Lägg till en kontrastkontroll i skärmbildstesterna.
- **Prioritet:** medium · **Storlek:** S

---

## Sammanfattning per prioritet
- **High:** UX-1 begrepp, UX-2 vad Starta gör, UX-3 fel och återhämtning, UX-6 lokala modeller (layout och blockerad knapp)
- **Medium:** UX-4, UX-5, UX-7, UX-8, UX-9, UX-10, UX-11, UX-12, UX-13, UX-14
- **Low:** inga separata fynd. Mindre saker är inbakade ovan, till exempel pluralformen ”1 undertabbar” i UX-1 och dubbel text i UX-6.

## Observerat som fungerar bra (behåll)
- Omstartsdialogen med ”Starta om när den är ledig” som primärt val (`restart.png`).
- Tomt läge med tydlig primär åtgärd ”Hitta projekt” (`ux-extra/main-empty.png`).
- Projekt som inte kan tas bort förklaras på raden (”Stäng först alla flikar …”, `settings-projects.png`).
- Guidens stegindikator och sidan ”Det här händer när du klickar på Installera” (`setup-install.png`), samt avinstallationsdialogen som räknar upp vad som tas bort och vad som behålls.
- Statusfärgerna kombineras alltid med text, så information förmedlas inte bara med färg.
