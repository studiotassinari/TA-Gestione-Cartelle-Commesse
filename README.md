# TA-Gestione Cartelle Commesse

Applicazione Windows WPF, .NET 10 e architettura MVVM per consultare commesse e gare e produrre report delle non conformità nei nomi delle cartelle.

## Scarica e installa

[![Scarica installatore Windows](https://img.shields.io/badge/Scarica_installatore-Windows_x64-135F6E?style=for-the-badge&logo=windows)](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/latest/download/TA-Gestione-Cartelle-Commesse-Setup.exe)

**[Scarica installatore Windows (.exe)](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases/latest/download/TA-Gestione-Cartelle-Commesse-Setup.exe)** · [Tutte le versioni e impronte SHA256](https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse/releases)

1. Accedi a GitHub con un account autorizzato a questo repository privato.
2. Scarica ed esegui `TA-Gestione-Cartelle-Commesse-Setup.exe`.
3. Segui la procedura guidata in italiano; il collegamento sul desktop è facoltativo.
4. Avvia il programma dal menu Start e premi **Aggiorna elenco**.

Il pacchetto include il runtime .NET 10 e si installa per il solo utente corrente, senza richiedere privilegi di amministratore. È destinato a Windows x64. Il programma necessita di accesso alla rete aziendale per leggere i percorsi selezionati.

L’installatore non è firmato digitalmente: Windows può mostrare un avviso di autore sconosciuto. L’impronta SHA256 è disponibile nella stessa release.

Per aggiornare, chiudi il programma e installa la nuova versione nella stessa cartella. Per disinstallare, apri **Impostazioni Windows → App → App installate → TA-Gestione Cartelle Commesse → Disinstalla**. Le cartelle di lavoro e i report salvati esternamente non vengono rimossi.

## Novità della versione

Interfaccia coordinata con l’identità aziendale, menu Apri ed Export e accesso diretto alle cartelle. Le risorse grafiche sono incluse nel programma e non richiedono accesso alla cartella dei loghi in rete.

## Funzioni

- Commesse da `Z:\lavori`: solo cartelle annuali principali e loro sottocartelle immediate; nessuna discesa nei livelli successivi.
- Gare da `Z:\gare`, in una sezione separata.
- Lettura di codice, anno, progressivo, cliente e argomento; ricerca e segnalazioni di non conformità.
- Menu **Export** per generare un report HTML ordinato e stampabile, con elenco e anomalie.
- Apertura della cartella selezionata in Esplora file con doppio clic, menu contestuale o menu **Apri**.
- Grafica Tassinari Associati: logo originale, font Acherus Grotesque e colori ottanio e notte. Logo e font sono incorporati anche nel report HTML.
- Consultazione in sola lettura: il programma non rinomina né modifica le cartelle.

Formato previsto: `CXXYYY - Cliente - Argomento` per le commesse, `GXXYYY - Cliente - Argomento` per le gare.

## Compilazione e rilascio

Aprire `TA-Gestione Cartelle Commesse.slnx` in Visual Studio con supporto .NET 10 e sviluppo desktop WPF.

Il workflow **Installer Windows** compila la versione Release con runtime incluso, crea il pacchetto Inno Setup, verifica installazione/avvio/disinstallazione su un runner Windows e pubblica una release con eseguibile e SHA256. Parte sui cambiamenti al programma o all’installatore nel ramo `master`, oppure manualmente da **Actions → Installer Windows → Run workflow**. La versione è `1.0.<numero esecuzione>`.

Il pacchetto distribuito contiene il programma e il runtime; non include dati clienti, cartelle di rete o report generati.
