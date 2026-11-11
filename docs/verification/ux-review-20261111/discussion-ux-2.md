# Diskussionsrunda 2 – UX bemöter grafikern

Vi skrev runda 1 parallellt och landade i nästan samma sammanslagningar. Nedan tar jag ställning till grafikerns fem öppna punkter och till mina egna tre frågor.

## Grafikerns öppna punkter

### 1. UX-2(a): etiketten ”öppnas” per rad eller en egen kryssrutestil för levande rader – **jag går över till grafikerns förslag, med två villkor**
Grafikern har rätt i att ännu en textetikett blir brus i en rad som redan är tät. En kryssruta med kontur för levande rader gör att antalet accentfyllda kryssrutor blir samma siffra som på Starta. Det är en elegant lösning.
Villkor ur användarperspektiv:
- Konturvarianten får inte se ut som inaktiv eller ”delvis vald”. Bocken behåller `TextBrush`, bara fyllningen ändras. Lägg till tooltip och tillgängligt namn: ”Atlas körs redan och startas inte om. Ingår vid nästa start när fliken är stängd.”
- Starta-knappens tooltip räknar upp vilka flikar som öppnas (UX-2c). Ett visuellt kodsystem måste kunna läras, och tooltipen är facit.

Om siffran i en pill: **jag föreslår att vi behåller texten ”Starta 3 flikar”**. Orden förklarar vad siffran räknar, och en pill utan enhet (”Starta (3)”) blir lättare att blanda ihop med ”Starta om alla (3)”. Grafikerns huvudpoäng, att siffrorna inte ska konkurrera, löses redan av att *Starta om alla* blir sekundär (G-2). Jag ser detta som en liten punkt. Om grafikern håller fast vid pillen behåller vi texten tills vidare och tar pillen som en låg följdfråga.

### 2. UX-5: delad knapp eller ↻ + textlänken ”Fortsätt” – **jag går över till grafikerns förslag**
Textlänken löser det jag var ute efter: skillnaden mellan nytt samtal och samma samtal syns utan tooltip, och raden får ingen ny inramad kontroll. Den delade knappen läggs i radens snabbmeny (som införs ändå för tangentbordsflytt). Länken ska ha ett tillgängligt namn med flikens namn (”Fortsätt senaste samtalet i Cirrus”).

### 3. UX-7: segmentkontroll eller combobox – **jag går över till combobox**
En combobox på 130 px i samma fältrad är lugnare och tar mindre plats. Då slipper vi också den avslagna växeln, som var radens mest kontrastrika element. Jag föreslår följande alternativtexter, eftersom ”Interaktiv” är oklart för den som inte känner till orkestern: **Av · Orkester · Orkester, en gång**. Kolumnrubriken blir ”Orkester”. Kolumnen visas bara när någon rad har orkester eller kan installera den. Orkesterinstallationen visas som en länk i cellen, som i dag.

### 4. UX-9: dölja *Stäng alla* – **jag går över till grafikerns förslag**
Bra argument. En sidfot som hoppar när flikar öppnas och stängs är sämre än en knapp som ibland är inaktiv. Det förutsätter att inaktiv faktiskt ser inaktiv ut (G-2), vilket vi är överens om. Därför: dölj bara i tomt läge, och visa den annars som tydligt inaktiv med tooltip ”Inga öppna flikar”. *Starta om alla* kan fortsätta att visas och döljas, eftersom den ligger längst till vänster i en högerjusterad grupp och inte flyttar de andra knapparna.

### 5. UX-8/G-5: prioritet – **överens: high**
Glyfen och popupens yta (opak, kant, skugga) är high. ”Gå till fliken” är medium och kan ligga kvar som valfri del i samma leverans, eller skjutas upp. Att innehållet syns genom popupen i mörkt läge behöver bekräftas i riktiga appen. Acceptanskriteriet skrivs så att det håller även om det är en headless-artefakt: popupens yta ska vara opak.

## Mina tre frågor från runda 1

1. **En primär per vy.** Grafikern skriver i UX-6-svaret ”*Klar* primär, *Hämta modell* primär när en modell är vald”, vilket fortfarande ger två primära knappar. **Kompromissförslag:** i fönster med en egen uppgift (Lokala modeller, Lokal modell, Hitta projekt) är uppgiftsknappen primär och *Klar*/*Avbryt* sekundära. I fönster utan uppgift (Inställningar) är *Klar* primär. Grafikerns underliggande mål, att samma ord ska se likadant ut, uppfylls fortfarande inom varje fönsterkategori. Om vi inte blir överens tar jag med det som öppen designfråga i knappsystemet.
2. **”–” i tomma orkesterceller:** löst. Grafikern stöder att kolumnen döljs (svaret på UX-7).
3. **Opacitet på ikonknappar:** löst. Grafikern föreslår själv att `FaintBrush` ritas med full opacitet och att opacitet inte ska bära tillstånd. Med det nya värdet #6B6F80 får ikonen 5,0:1. Bra.

## Övrigt
- Grafikern sänker G-7 (radrytm) till low och låter G-10 gå upp i UX-14. Jag godtar båda.
- G-13: grafikern behåller low för hela fyndet. Jag tycker fortfarande att **färgrotationen för nya flikar** är medium, eftersom den påverkar uppgiften ”vilken flik är vilken” i Windows Terminal. Kompromiss: rotationen läggs i huvudlisteleveransen (medium), och appikonen i små storlekar läggs i guideleveransen (low del).
