# Diskussionsrunda 1 – grafisk formgivare svarar UX-designern

Jag har läst `ux-findings.md` och bilderna i `ux-extra/` (`main-orch.png`, `main-empty.png`, `agents-missing.png`). Vi har oftast kommit fram till samma sak från två håll. Vi pekar till exempel ut samma kontrastvärde (#6B6F80) och samma tofu-tecken. Där vi är oeniga handlar det nästan alltid om att en UX-lösning tillför ytor eller kontroller i en rad som redan är tät. Där föreslår jag en visuellt lättare variant.

## Svar per UX-fynd

### UX-1 Begrepp (LLM/agent/flik/tabb) – **håller med**
Det här är rätt även visuellt. ”Agent” är kortare än ”LLM” bara i tecken, men det viktiga är att kolumnrubriken och listans ikoner (Claude, Codex …) då säger samma sak. Det stärker kopplingen mellan huvudfönstret och Inställningar → AI-agenter, som använder samma logotyper. Inga invändningar. Inget överlapp med mina fynd.

### UX-2 Vad Starta gör – **håller med, med ändring**
Problemet är verkligt och (b) och (c) är bra. Mot (a) har jag en invändning: en etikett ”öppnas” på varje rad lägger till ett femte textelement i en rad som redan har rand, handtag, kryssruta, namn, ikon, status, två ikonknappar och två comboboxar. Det blir brus. Statusen bär redan informationen visuellt: grön eller blå prick betyder att fliken lever.

Mitt förslag: låt kryssrutan på levande rader byta utseende från accentfylld till **kontur i `MutedBrush` med bock** (”ingår men är redan igång”). Den ska *inte* se inaktiv ut, eftersom den fortfarande styr nästa start. Då kan ögat räkna accentfyllda kryssrutor, och antalet blir samma siffra som på Starta. Visa dessutom Starta-knappens siffra som tal i en pill (`3`) i stället för i löptext, och lägg ”Starta om alla (3)” i sekundärstil (G-2). Då konkurrerar inte siffrorna.

### UX-3 Fel visas långt från orsaken – **håller med**
Det här överlappar **G-3**. Feltexten #E5484D på vitt har 3,9:1 och ligger dessutom på fel ställe. Vi slår ihop det så här: banderollen får en `danger`-variant av `Border.update`, med kant `DangerBrush` (ljust #C62F35, mörkt #FF6B6F), bakgrund ljust #FDF0F0 och mörkt #2A1518, samt samma ikonkolumn som uppdateringsraden (en varningstriangel). Statusraden längst ner används då bara för neutral kvittens i `MutedBrush`. ”Avslutad · fel” på raden använder `DangerBrush`-pricken som i dag.

### UX-4 Fasta kolumnbredder klipper status – **håller med, med ändring**
Det här överlappar **G-9** (”codex (sakna…” beror på att hela comboboxen är i mono, och mono är ca 15 % bredare). Lösningen ska helst inte bli bredare kolumner, utan mindre text:
- ”saknas” blir en varningsprick eller triangel i `WarningBrush` efter agentnamnet, med tooltip och tillgängligt namn. Det håller med UX-designerns förslag.
- Statuskolumnen får den plats som frigörs när orkesterkolumnerna döljs (UX-7). Jag föreslår `*` för status och fast bredd för agent (166) och modell (195).
- Ett skärmbildstest med långa svenska strängar stöder jag helt.

Vi slår ihop UX-4 med G-9 till ett fynd: ”Ingen information får försvinna genom avklippning; mono bara på värden.”

### UX-5 Tvetydiga ikonknappar – **håller med om problemet, delvis oenig om lösningen**
Att mappikonen ska betyda ”öppna i Utforskaren” i båda fönstren håller jag helt med om. Det passar in i **G-11** (en ikonfamilj). En penna för ”byt katalog” fungerar visuellt.

Mot den delade knappen ”Starta om ▾” invänder jag. På varje rad blir den ca 90–110 px bred med kant och pil. Raden får då en tredje kontroll med ram och den sekundära åtgärden väger lika mycket som comboboxarna. Mitt alternativ: behåll ↻ som ikon och byt ▷ mot en **textlänk ”Fortsätt”** i `Button.link`-stil, 12 px. Den är lika smal som en ikon och läses utan tooltip. Den syns bara när det går att fortsätta, så den tar ingen plats på andra rader. Om UX-designern vill ha split-knappen kan den ligga i radens snabbmeny (som UX-14 ändå inför).

### UX-6 Lokala modeller – **håller med**
Det här är samma fynd som **G-6**. UX-designerns tre steg (Server → Välj modell → Hämta) är en bättre struktur än min ”sektioner som i Inställningar”. Jag tar den rakt av. Mitt bidrag blir den visuella specen för stegen:
- Stegrubriker med samma numrerade cirklar som installationsguiden. Det ger igenkänning och gör guidens komponent återanvändbar.
- Rekommendationen blir en callout med `BoltIcon`.
- Vald rad markeras med kontur, inte med lila fyllning.
- Modell-id i mono 13.
- Sidfoten ligger på en egen rad utanför scrollytan.

Skälet bredvid en inaktiv *Hämta modell* skrivs i `caption` + `MutedBrush` direkt till vänster om knappen. ”Använd en enhet” instämmer jag i. Vi slår ihop till **UX-6/G-6, high, M**.

### UX-7 Tomma orkesterkolumner – **håller med, med ändring**
Det här överlappar **G-7**, som har samma iakttagelse om 240 px tom yta. Att dölja kolumnerna stöder jag. Lägesväljaren ”Av / Interaktiv / Engång” är logiskt rätt, men en segmenterad kontroll med tre delar blir ca 200 px och visuellt tyngre än dagens två växlar. Jag föreslår en kompakt combobox (130 px, samma `ComboBox.field`-höjd 34) som linjerar med Agent och Modell. Raden får då tre jämnstora fält i rad, vilket är lugnare än fält plus växlar. I `main-orch.png` ser man också att en avslagen växel (svart kontur med svart knopp) är det mest kontrastrika elementet på raden. Det försvinner med combobox-lösningen. G-7 behåller bara delarna om radrytmen (56/64 px) och ×-knappens grå ruta.

### UX-8 Live Peek – **håller med**
Samma fynd som **G-5** (tofu-glyferna). Jag lägger till två saker. I mörkt läge (`grafik-extra/main-dark-update-output.png`) syns innehållet genom popupen, och popupen saknar kant och skugga. Rubriken ”Atlas · claude · Körs” stöder jag, och den ska använda samma statusprick som raden. Om prioriteten är jag lite oenig: tomma rutor i den mest synliga förhandsvisningen ser trasigt ut och kostar förtroende, och åtgärden är S. Jag föreslår **high** för själva glyfen och popupens yta och medium för ”Gå till fliken”. Vi slår ihop till **UX-8/G-5**.

### UX-9 Knappnivåer och farliga åtgärder – **håller med, med en ändring**
Samma kärna som **G-2**. Danger-stilen för *Tvångsstäng*, att flytta den till vänster och omskrivningen av `close.confirm` till tre punkter stöder jag. Spinner per rad passar ihop med min G-3-punkt att *Stäng alla*-dialogen ska använda samma statusprickar som huvudfönstret.

Invändning: att **dölja** *Stäng alla* när inget är öppet tycker jag bara ska gälla tomt läge (`main-empty.png`, där håller jag med). I det vanliga läget, med projekt men inga öppna flikar, skulle sidfoten hoppa i sidled varje gång flikar öppnas eller stängs, och Starta-knappen sitter fast i högra kanten medan *Starta om alla* flyttar sig. En stabil placering är viktigare. Där räcker en inaktiv stil som faktiskt syns som inaktiv (G-2: transparent bakgrund, kant 50 %, `FaintBrush`-text). Vi slår ihop till **UX-9/G-2, high, M**. Jag höjer UX-9 från medium, eftersom samma knappsystem löser UX-2(d), UX-3 och UX-6.

### UX-10 Inställningarnas struktur – **håller med**
Informationsarkitekturen är främst UX-designerns område, och flyttarna verkar rimliga. Det överlappar **G-8**: om Allmänt och Utseende ändå byggs om är det rätt tillfälle att införa en gemensam sidmall (titel + ingress + sektioner) och en enda vänstermeny. ”Minne och kontext” som fliknamn får plats (ca 130 px i 14 SemiBold). Om projektlistan vid 640×400 håller jag med. Knapparna bör ligga utanför listan men inuti scrollytan. Vi slår ihop till **UX-10/G-8, medium, M**.

### UX-11 Agentöversikten – **håller med**
Visuellt har korten i dag fyra textrader med nästan samma vikt, plus en fetstil som ser ut som en rubrik. Två rader (Status och Behörigheter) passar ett tydligare kort: logotyp + namn (14 SemiBold), statusprick + ”Redo/Saknas” (13), behörighet (13 Muted), och sist en länk eller en liten sekundärknapp *Installera*. Det berör **G-14**: en gemensam logotypplatta gör korten jämnare. Jag behåller G-14 som low och separat.

### UX-12 Rådet för lokal modell – **håller med**
Resultatkortet ska vara *samma* callout-komponent som rekommendationen i UX-6/G-6, med en semantisk färg från **G-3**: Passar = `SuccessBrush`, Osäker = `WarningBrush`, Passar inte = `DangerBrush`. Färgen ligger i en 3 px kant till vänster och ikonen, inte som fyllning över hela kortet, så att texten behåller AA. Den hopfällbara sektionen för mätvärden ska använda samma Expander-stil som ”När sker rensningen?” i Allmänt.

### UX-13 Guiden visar inte om agenten finns – **håller med**
Visuellt: samma regel som i UX-4/G-9. ”(saknas)” visas inte som textsuffix i mono, utan som en varningsprick + text i `UiFont`. Att återanvända kryssrutelistan från `import.png` i steg 3 är bra, eftersom guiden och importfönstret då ser ut som samma produkt.

### UX-14 Tangentbord och kontrast – **håller med, med en ändring**
Vi har kommit fram till samma värde: `FaintBrush` → #6B6F80. Det är också min **G-10**, som går upp i UX-14. Om ta bort-knappen: opacitet 0,35 valdes för att hålla listan lugn, och det målet är rimligt. Lösningen är att låta ikonen alltid ritas i `FaintBrush` (#7F8394 ger 3,8:1 mot vitt, vilket räcker för icke-text) med full opacitet, och i `TextBrush` vid hovring och fokus. Opacitet ska inte bära tillståndet. Alt+↑/↓ och snabbmenyn stöder jag. ⋮-handtaget blir en ritad `GripIcon` (G-11) och kan då också visa fokusring.

## Omprövning av mina egna fynd

| G | Ändring | Ny prio |
|---|---|---|
| G-1 Ytor/neutraler | Kvar. Det är grunden för danger-banderollen (UX-3), callouts (UX-6/UX-12) och knappvarianterna (UX-9). Användarnyttan är indirekt, så jag sänker prioriteten men vill att det görs **först**, eftersom det är infrastruktur. | high → **medium (görs först)** |
| G-2 Knapphierarki | Slås ihop med **UX-9**. | high |
| G-3 Statusfärger | Kvar och självständigt. Behövs av UX-3, UX-9 och UX-12. Brister mot AA. | high |
| G-4 Typskala | Kvar, men ärligt talat inte användarblockerande. | high → **medium** |
| G-5 Utdatapopup | Slås ihop med **UX-8**. | high (glyf + yta) |
| G-6 Lokala modeller | Slås ihop med **UX-6**. Jag tar UX-designerns struktur. | high |
| G-7 Huvudlistans rytm | Kolumndelen går upp i **UX-7**. Kvar: radrytm och ×-knappens ruta. | **low** |
| G-8 Inställningarnas sidmall | Slås ihop med **UX-10**. | medium |
| G-9 Monospace | Slås ihop med **UX-4** (och regeln gäller UX-13). | medium |
| G-10 Faint-kontrast | Går upp i **UX-14**. Dras tillbaka som eget fynd. | – |
| G-11 Ikonografi | Kvar. Mappikonens betydelse från UX-5 läggs till, liksom ritade chevron- och griplikoner. | medium |
| G-12 Hörnradier | Kvar och oförändrad. Tas lämpligen i samma pass som G-1. | low |
| G-13 Varumärke | Kvar. | low |
| G-14 Agentlogotyper | Kvar, med koppling till UX-11. | low |

## Föreslagen gemensam lista (efter sammanslagning)
- **High:** UX-1 · UX-2 · UX-3 (+G-3 danger-banderoll) · UX-6/G-6 · UX-8/G-5 · UX-9/G-2 · G-3
- **Medium:** G-1 (görs först) · G-4 · UX-4/G-9 · UX-5 (+G-11) · UX-7 (+G-7 kolumner) · UX-10/G-8 · UX-11 · UX-12 · UX-13 · UX-14 (+G-10)
- **Low:** G-7 (radrytm) · G-12 · G-13 · G-14

## Kvarstående oenighet att avgöra
1. **UX-2(a):** etikett ”öppnas” per rad (UX) eller kryssrutor med olika utseende för levande och ej levande rader (grafik).
2. **UX-5:** delad knapp ”Starta om ▾” (UX) eller ↻-ikon + textlänken ”Fortsätt” (grafik).
3. **UX-7:** segmenterad lägesväljare (UX) eller kompakt combobox i fältraden (grafik). Vi är överens om att två oberoende växlar ska bort.
4. **UX-9:** dölja *Stäng alla* alltid när inget är öppet (UX) eller bara i tomt läge, och annars visa den som tydligt inaktiv (grafik).
5. **UX-8/G-5:** prioritet medium (UX) eller high för glyf och popupyta (grafik).
