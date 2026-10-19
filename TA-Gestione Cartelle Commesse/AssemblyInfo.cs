// Importa gli attributi WPF relativi ai dizionari di risorse dei temi.
using System.Windows;

// Configura a livello di assembly la ricerca delle risorse dei controlli WPF.
[assembly: ThemeInfo(
    // Indica che non sono presenti dizionari separati specifici per ciascun tema.
    ResourceDictionaryLocation.None,            // Nessun dizionario specifico del tema.
                                                // Ricerca successiva alle risorse della pagina,
                                                // e a quelle definite a livello applicazione.
    // Cerca il dizionario generico delle risorse nello stesso assembly dell'applicazione.
    ResourceDictionaryLocation.SourceAssembly   // Risorse generiche nello stesso assembly.
                                                // Ricerca successiva alle risorse della pagina,
                                                // dellʼapplicazione e degli eventuali temi.
// Conclude l'attributo ThemeInfo applicato all'assembly.
)]
