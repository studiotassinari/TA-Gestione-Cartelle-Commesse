# Curriculum dello studio

Modulo per raccogliere i lavori svolti, da usare come referenze e nelle gare. Si apre dal menu **Gestione > Curriculum dello studio…** oppure, da una riga di commesse, gare o archivio, con il tasto destro e **Curriculum: lavori collegati…**.

## Scheda del lavoro

Titolo, committente, luogo, date di inizio e fine, prestazioni svolte, ruolo dello studio, importo dei lavori, importo del servizio, macro categorie di prestazioni, categorie delle opere (ID opere, importo, descrizione e grado, una per riga), riferimenti del committente, descrizione, parole chiave e fotografie con didascalia.

I lavori si inseriscono a mano. Ogni lavoro può essere collegato a una o più commesse o gare tramite il **codice** (per esempio `C24031`, `G23012`): il codice resta lo stesso quando la cartella passa dalle commesse attive all'archivio, quindi il collegamento non si perde. La scheda indica dove si trova oggi ogni cartella collegata e permette di mostrarla nell'elenco; al contrario, da una commessa si aprono i lavori del curriculum che la citano e se ne può creare uno nuovo già precompilato.

La ricerca considera titolo, committente, luogo, descrizione, prestazioni, parole chiave, macro categorie, categorie delle opere, didascalie, anni e codici collegati, senza distinzione di maiuscole e accenti.

## Chi può modificare

Tutti gli utenti autorizzati al programma possono consultare e aggiornare il curriculum. Ogni salvataggio registra autore e data; il registro delle modifiche conserva la versione precedente. Se due persone modificano la stessa scheda, il secondo salvataggio viene rifiutato con l'invito ad aggiornare: nessuna modifica viene sovrascritta in silenzio.

## Dove sono i dati

I lavori sono nello stesso archivio SQLite del gestionale (`gestionale.sqlite`, sul disco locale del server), quindi rientrano nel backup verificato già esistente. Le fotografie sono file nella cartella `curriculum-foto` accanto al database e vengono aggiunte al backup. Il client le riduce a 1800 punti sul lato maggiore prima dell'invio; il server accetta solo JPEG e PNG, fino a 8 MB e 30 fotografie per lavoro. Le cartelle delle commesse non vengono mai lette né modificate da questo modulo.

## Esportazione in Word

**Esporta in Word…** produce un documento con i lavori mostrati in quel momento dalla ricerca. Prima del salvataggio si scelgono formato e contenuti:

- **Schede complete**: una scheda per lavoro con simbolo delle macro categorie, fotografia grande, griglia dei dati, prestazioni, descrizione, tabella delle categorie delle opere, altre fotografie affiancate e parole chiave. Con molte fotografie la scheda prosegue sulla pagina successiva.
- **Schede sintetiche**: più lavori per pagina, con una fotografia, i dati essenziali e il primo paragrafo della descrizione.
- **Elenco per gare**: tabella orizzontale con una riga per categoria di opere e, in fondo, i totali per ID opere e il totale complessivo.

Si possono togliere copertina, fotografie, importo del servizio, riferimenti del committente e parole chiave, e indicare titolo e sottotitolo della copertina. L'impaginazione è dinamica: ogni blocco compare solo se la scheda lo contiene, e le fotografie vengono ritagliate al centro per riempire il riquadro senza deformarsi (al massimo cinque per lavoro nelle schede complete).

### Macro categorie e simbolo a quadranti

Sulla scheda si spuntano le macro categorie di prestazioni: Strutture, Architettura, Impianti, Consulenza. Nell'esportazione il simbolo a quadranti riporta le quattro icone: quelle spuntate nel colore principale, le altre in grigio chiaro. Senza alcuna spunta il simbolo non compare.

### Grafica dello studio

Logo, copertina, icone e stile **non fanno parte del programma**: restano in una cartella indicata nel **Gestore server > Configura** («Cartella della grafica del curriculum»), che il servizio legge soltanto. La cartella deve essere raggiungibile dal servizio e contenere i file con questi nomi (tutti facoltativi, al massimo 2 MB ciascuno):

| File | Uso |
| --- | --- |
| `logo.png` | intestazione di ogni pagina |
| `logo-chiaro.png` | logo sopra la fascia di copertina |
| `copertina.jpg` oppure `copertina.png` | fascia in alto sulla copertina, a tutta larghezza |
| `strutture.png`, `architettura.png`, `impianti.png`, `consulenza.png` | icone delle macro categorie, a fondo trasparente; servono tutte e quattro |
| `stile.json` | colori, caratteri e intestazione |

Esempio di `stile.json`:

```json
{
  "ColorePrincipale": "135F6E",
  "ColoreScuro": "192845",
  "CarattereTitoli": "Nome del carattere dei titoli",
  "CarattereTesto": "Nome del carattere del testo",
  "Intestazione": "Curriculum dello studio"
}
```

I caratteri indicati devono essere installati sui computer che aprono o stampano il documento, altrimenti Word li sostituisce. Senza cartella della grafica l'esportazione esce senza immagini, con i colori predefiniti e il carattere Segoe UI.

### Modello Word (facoltativo)

Se nel Gestore server è indicato anche un modello Word (`.docx` o `.dotx`), tra i formati compare «Modello Word dello studio»: testo semplice inserito nel modello, di cui restano intestazione, piè di pagina, margini e stili (*Titolo*, *Titolo 1*, *Titolo 2*, *Didascalia*, *Normale*).
