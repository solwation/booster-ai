# Diskussionsrunda 1 – UX svarar grafikern

Jag har läst `grafik-findings.md` och tittat på bilderna i `grafik-extra/`. Kort sagt håller jag med om nästan allt i sak. Det var också lugnande att vi oberoende av varandra kom fram till samma nya värde för `FaintBrush` (#6B6F80), samma lösning för tofu-tecknen och samma förslag om att dölja orkesterkolumnerna. Där jag är oenig gäller det främst **prioritet**, eftersom jag väger hur mycket fyndet hindrar en uppgift. På två punkter är jag oenig i sak: *Klar* som primär knapp överallt, och ”–” i tomma kolumner.

## Svar per G-fynd

### G-1 Ytor och neutraler – **håller med, med ändring (prio high → medium)**
Fyndet stämmer, och yt-tokens är rätt grund. Ur användarens perspektiv hindrar dock rent svart och lånad grå ingen uppgift. Det är ett estetiskt problem och ett underhållsproblem. Jag föreslår medium som eget fynd, men att det **görs först** eftersom G-2, G-3 och G-10 bygger på det. Prioriteten ska spegla vad användaren drabbas av, och ordningen hanteras i genomförandeplanen.

### G-2 Knapphierarki – **håller med (high)**
Det här är grafikerns viktigaste fynd för användbarheten. Att *Inställningar* och *Starta om alla* ser inaktiva ut är ett riktigt upptäckbarhetsproblem, och jag pekade på samma sak i UX-9. Två justeringar:
- **Danger på *Avinstallera*: delvis oenig.** Användaren har redan valt avinstallation i Windows, så dialogen bekräftar ett uttalat val, och konfiguration behålls. En röd knapp skadar inte, men behovet är störst för *Tvångsstäng*, där osparat arbete kan försvinna och där den farliga knappen står bredvid en säker. Gör danger obligatorisk där en farlig och en säker väg står sida vid sida. För *Avinstallera* är den valfri.
- **Inaktiv = transparent bakgrund + Faint-text:** bra, men kontrollera att en inaktiv knapp inte förväxlas med en länk eller en ghost-knapp. Behåll en kantlinje (som förslaget gör).

### G-3 Statusfärger – **håller med (high)**
Tokenarbetet är rätt. Det överlappar UX-3 men löser bara halva problemet: med en feltext i AA-kontrast sitter felet fortfarande på fel ställe och saknar åtgärd. Förslag till sammanslagning: **G-3 äger färgtokens och prickar** (även i *Stäng alla*), **UX-3 äger felmönstret** (banner med åtgärd, väg från raden till utdata). Gör dem tillsammans, eftersom bannern ska använda `DangerBrush`.

### G-4 Typografisk skala – **håller med, med ändring (prio high → medium)**
Skalan är bra och värd att införa. Men halvpunktssteg och 20 jämfört med 22 i rubrik hindrar ingen användare. Det är konsekvens och ett intryck av omsorg. Medium. Det syntetiserade Bold-snittet är mest synligt i agentkorten, där texten i sak ändå ska bort (se UX-11), så det försvinner delvis av sig självt. Varning: ett svep över 105 lokala `FontSize` påverkar alla skärmbildstester. Gör det i ett pass, inte vy för vy.

### G-5 Utdatapopupen – **håller med (high)**
Slås ihop med **UX-8**, och jag höjer min egen prioritet från medium till high. Peek är en huvudfunktion i README, och om popupen verkligen är genomskinlig i mörkt läge (`main-dark-update-output.png`, där rader och comboboxar syns genom utdatan) går den knappt att läsa. Kontrollera det i riktiga appen innan ändringen görs. Headless-läget ritar popupen ovanpå fönsterramen enligt DEVELOPMENT.md, så det kan vara en artefakt. Om det bara är en artefakt blir prioriteten medium.
- Typsnitt: jag föredrar **fallback-typsnitt** framför att bara översätta ⎿ → └. CLI:erna använder fler tecken (⏺, ✻, ⎿ och codex/agys egna), så en översättning åtgärdar bara det fall vi råkar se.
- Rubriken ”● Körs · Atlas”: samma förslag som i UX-8. Vi formulerar det gemensamt.

### G-6 Lokala modeller – **håller med, med ändring (high)**
Slås ihop med **UX-6 och UX-12** till ett fynd om båda fönstren för lokala modeller. Grafikern har bättre konkreta förslag för listan, den valda raden och rekommendationen, och jag bidrar med flödet (Server → Välj → Hämta), förklaringen till varför ”Hämta modell” är inaktiv och resultatkortet i `local-model.png`.
- **Oenig: ”*Klar* primär i alla fönster”.** I Lokala modeller är *Hämta modell* uppgiften, och *Klar* betyder bara stäng. Om båda är primära finns det inte längre någon primär knapp. Regeln bör vara **en primär per vy, och det är vyns uppgift**. I Inställningar är *Klar* primär (fönstret har ingen annan åtgärd). I Lokala modeller är *Hämta modell* primär när en modell är vald, och *Klar* är sekundär. Grafikern föreslår själv att *Hämta* blir primär, så det är troligen bara formuleringen ”alla fönster” som bör strykas.
- Storleken blir M–L tillsammans.

### G-7 Huvudlistans rytm – **håller med, med ändring (medium)**
- Ojämn radhöjd: håller med. Det är litet men syns i appens viktigaste vy.
- Tomma kolumner: slås ihop med **UX-7**. **Oenig med alternativet ”–” i varje cell.** Ett streck på varje rad säger ”här skulle det finnas något” och lockar användaren att leta efter en orkester som inte finns. Dölj kolumnerna (bredd 0) och lämna platsen till status (UX-4/G-9). Lägesväljaren Av/Interaktiv/Engång från UX-7 följer med när kolumnerna visas.
- ×-knappen: håller med om den grå rutan. **Men se upp med ”opaciteten på ikonen, som för `Button.remove`”.** I UX-14 kritiserar jag just att `Button.remove` ligger på 0,35 även när den är *aktiv*, vilket gör den osynlig för den som inte använder mus. Låg opacitet är rätt för inaktiv, men aktiva ikonknappar måste ha minst 3:1. Låt oss formulera regeln gemensamt.

### G-8 Inställningarnas sidmall – **håller med (medium)**
Slås ihop med **UX-10**. Grafikern äger mallen (titel, ingress, en gemensam SideNav, x-led), och UX äger var innehållet ligger (auto-clear till Optimering → Kontext, basmappen till Projekt, nytt namn på Optimering). Med samma SideNav i båda flikarna löser vi också min invändning om att två flikar har undernavigering och två inte har det. Om Allmänt får titel och sektioner i samma mall kan det räcka. Om vi samtidigt flyttar ut auto-clear och basmappen blir Allmänt kort nog att inte behöva en egen undernavigering.

### G-9 Monospace – **håller med (medium)**
Slås ihop med **UX-4**: det avklippta ”codex (sakna” är både ett typsnittsfel (mono på ett svenskt ord) och ett breddproblem. Gemensamt förslag: ”saknas” blir en varningsikon med tooltip och tillgängligt namn (inte text i comboboxen), statuskolumnen får växa när orkesterkolumnerna är dolda, och regeln ”mono bara på värdet” införs. Sökvägar som bryts i avinstallationsdialogen: håller med, låg vikt i sig.

### G-10 Kontrast i etiketter – **håller med (medium)**
Slås ihop med kontrastdelen av **UX-14** (samma värde #6B6F80). Iakttagelsen om omvänd hierarki (etiketten svagare än hjälptexten) är bra, och den missade jag. UX-14 behåller resten: tangentbordsalternativ för att ändra ordning, synliga ta bort-knappar och tillgängliga namn på kryssrutorna.

### G-11 Ikonografi – **håller med (medium)**
Kugghjulet som ser ut som en sol är ett riktigt UX-problem: i en app med temaval läses det som ”byt ljust/mörkt”, och det är appens enda väg till Inställningar. Slås ihop med **UX-5** till ett fynd om ikonsystemet: grafikern äger ritningen (kugghjul, chevroner, grip, två storlekar, 1,5 px streck), UX äger betydelsen (mappikonen ska betyda öppna i Utforskaren i båda fönstren, ↗ ska inte betyda öppna mapp, och ▷ ”fortsätt” får text eller delad knapp).

### G-12 Hörnradier – **håller med (low)**
Inga invändningar. Gör det i samma tokenpass som G-1.

### G-13 Varumärke och flikfärger – **håller med, med ändring (low → medium för färgrotationen)**
Färgrotationen har större användarvärde än fyndet antyder: hela poängen med flikfärger är att användaren ska skilja flikarna åt i Windows Terminal, och i dag är alla nya flikar likadana. Rotationen är S och påverkar själva uppgiften (”vilken flik är vilken?”), så jag föreslår medium för den delen. Ikonen i små storlekar: low, håller med.

### G-14 Agentlogotyper – **håller med, med ändring (low)**
Tom cirkel = radioknapp är ett riktigt förväxlingsproblem, särskilt i agentlistan i huvudfönstret, där den står bredvid en kryssruta. Plattorna är rimliga i korten. **I comboboxen** (14 px i en 32 px hög kontroll) riskerar en platta att göra logotypen ännu mindre. Prova där innan regeln görs generell. Low.

## Omprövning av mina egna fynd

| UX | Ändring |
|---|---|
| UX-1 begrepp | Oförändrad, high. Grafikern berör det inte, men ”Välj AI och modell” och ”LLM”-kolumnen syns i alla huvudfönsterbilder. |
| UX-2 vad Starta gör | Oförändrad, high. Delvis beroende av G-2: när *Starta om alla* blir en riktig sekundärknapp behövs tydligare etiketter ännu mer. |
| UX-3 fel och återhämtning | Oförändrad, high. Görs ihop med G-3. |
| UX-4 avklippt status | **Uppgår i G-9** (medium). |
| UX-5 ikonbetydelser | **Uppgår i G-11** (medium). |
| UX-6 lokala modeller | **Uppgår i G-6** tillsammans med UX-12 (high, M–L). |
| UX-7 tomma orkesterkolumner | **Uppgår i G-7** (medium). Lägesväljaren följer med. |
| UX-8 Peek | **Uppgår i G-5**. Höjs till **high** om genomskinligheten i mörkt läge bekräftas. |
| UX-9 stängningsflödet | **Knappdelen uppgår i G-2.** Kvar som eget fynd: dölj *Stäng alla* när inget är öppet, förlopp per rad, kortare text före Stäng alla. **Sänks till low–medium**, för knappstilarna var den viktigaste delen. |
| UX-10 inställningarnas IA | **Paras med G-8** (medium). |
| UX-11 agentkortens språk | Oförändrad, medium. Löser också delvis G-4:s Bold-problem. |
| UX-12 lokal modell-råd | **Uppgår i G-6** (se ovan). |
| UX-13 guidens agentval | Oförändrad, medium. |
| UX-14 tangentbord och kontrast | **Kontrastdelen uppgår i G-10.** Kvar: tangentbordsalternativ för att ändra ordning, ta bort-knappar ≥3:1, tillgängliga namn. Medium. |

Jag drar inte tillbaka något fynd helt. Fem av mina fynd blir delar av grafikerns, och ett sänks.

## Förslag på sammanslagen lista (för koordinatorn)

| Nytt id | Innehåll | Källor | Prio | Storlek |
|---|---|---|---|---|
| A | Begrepp och ordlista (agent/modell/flik) | UX-1 | high | S |
| B | Vad Starta öppnar, ”Med”-kolumnen | UX-2 | high | M |
| C | Fel och återhämtning + semantiska statusfärger | UX-3 + G-3 | high | M |
| D | Knappsystem (primary/secondary/danger/inaktiv) | G-2 + UX-9 (knappdelen) | high | M |
| E | Lokala modeller och modellråd | G-6 + UX-6 + UX-12 | high | M–L |
| F | Live Peek: glyfer, yta, rubrik | G-5 + UX-8 | high (medium om genomskinligheten är en artefakt) | S |
| G | Yt-tokens och radier | G-1 + G-12 | medium (görs först) | M |
| H | Huvudlistan: rytm, dolda orkesterkolumner, lägesväljare | G-7 + UX-7 | medium | S |
| I | Monospace-regel och avklippt status/”saknas” | G-9 + UX-4 | medium | S |
| J | Ikonsystem: ritning och betydelse | G-11 + UX-5 | medium | M |
| K | Inställningar: sidmall och var innehållet ligger | G-8 + UX-10 | medium | M |
| L | Kontrast i etiketter och kolumnrubriker | G-10 + UX-14 (kontrast) | medium | S |
| M | Typografisk skala | G-4 | medium | M |
| N | Tangentbord och skärmläsare | UX-14 (rest) | medium | S |
| O | Agentkortens språk och åtgärder | UX-11 | medium | S |
| P | Guidens agentval och projektlista | UX-13 | medium | S |
| Q | Flikfärgsrotation (medium) och appikon i små storlekar (low) | G-13 | medium/low | S |
| R | Stängningsflödet: förlopp, text, dölj när inget är öppet | UX-9 (rest) | low–medium | S |
| S | Agentlogotyper | G-14 | low | S |

Öppna frågor till grafikern:
1. Kan vi enas om regeln **en primär knapp per vy = vyns uppgift**, i stället för ”Klar primär överallt”?
2. Kan vi stryka alternativet med ”–” i G-7 och bara dölja kolumnerna?
3. En gemensam regel för ikonknappar: inaktiv får låg opacitet, men aktiv har alltid minst 3:1, även ta bort-knappar som bara framhävs vid hover.
