using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Services;

// Prepara la grafica dello studio ricevuta dal server: stile, logo, copertina e simbolo a quadranti delle macro categorie.
// Il materiale resta nella cartella indicata sul server: nel programma non c'è alcun logo.
public static class CurriculumGraphicsKit
{
    private const int Side = 240, Gap = 14;
    private const string Off = "D0D6DB";

    public static CurriculumKit Create(CurriculumGraphics? graphics)
    {
        var files = (graphics?.Files ?? []).GroupBy(f => f.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Data);
        string main = "135F6E", dark = "192845", titleFont = "Segoe UI", textFont = "Segoe UI", footer = "Curriculum dello studio";
        if (files.TryGetValue("stile.json", out var style))
        {
            try
            {
                // Il Blocco note salva spesso con il segno iniziale UTF-8, che il lettore JSON non accetta.
                int skip = style.Length >= 3 && style[0] == 0xEF && style[1] == 0xBB && style[2] == 0xBF ? 3 : 0;
                using var json = JsonDocument.Parse(style.AsMemory(skip), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                string Text(string name, string fallback) => json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Trim().Length is > 0 and <= 80 ? text.Trim() : fallback;
                main = Colour(Text("ColorePrincipale", main), main); dark = Colour(Text("ColoreScuro", dark), dark);
                titleFont = Text("CarattereTitoli", titleFont); textFont = Text("CarattereTesto", textFont); footer = Text("Intestazione", footer);
            }
            catch (JsonException ex) { throw new InvalidDataException("Il file stile.json della grafica del curriculum non è leggibile: " + ex.Message); }
        }
        CurriculumImage? Picture(params string[] names)
        {
            foreach (var name in names)
                if (files.TryGetValue(name, out var data) && Kind(data) is { } type)
                {
                    try
                    {
                        using var stream = new MemoryStream(data);
                        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                        return new CurriculumImage(data, type, frame.PixelWidth, frame.PixelHeight);
                    }
                    catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException) { throw new InvalidDataException("Immagine della grafica del curriculum non leggibile: " + name); }
                }
            return null;
        }
        var icons = CurriculumSectors.All.Select(sector => Picture(sector.ToLowerInvariant() + ".png")).ToArray();
        Func<string[], CurriculumImage?> quadrant = _ => null;
        if (icons.All(icon => icon != null))
        {
            var masks = icons.Select(icon => Mask(icon!.Data)).ToArray();
            var cache = new Dictionary<string, CurriculumImage>();
            quadrant = sectors =>
            {
                var active = CurriculumSectors.All.Select(sector => sectors.Contains(sector, StringComparer.OrdinalIgnoreCase)).ToArray();
                // Nessuna macro categoria spuntata: nessun simbolo, invece di quattro icone spente.
                if (!active.Any(on => on)) return null;
                string key = string.Concat(active.Select(on => on ? '1' : '0'));
                lock (cache) return cache.TryGetValue(key, out var known) ? known : (cache[key] = Compose(masks, active, main));
            };
        }
        return new CurriculumKit
        {
            Main = main, Dark = dark, TitleFont = titleFont, TextFont = textFont, Footer = footer, Logo = Picture("logo.png"), LogoLight = Picture("logo-chiaro.png"),
            Cover = Picture("copertina.jpg", "copertina.png"), Icons = icons.All(icon => icon != null) ? icons.Select(icon => icon!).ToArray() : Array.Empty<CurriculumImage>(), Quadrant = quadrant
        };
    }
    private static string Colour(string text, string fallback)
    {
        text = text.TrimStart('#').ToUpperInvariant();
        return text.Length == 6 && text.All(Uri.IsHexDigit) ? text : fallback;
    }
    private static string? Kind(byte[] data) => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF ? "jpg"
        : data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 ? "png" : null;

    // Sagoma dell'icona: la trasparenza se c'è, altrimenti il tratto scuro su fondo chiaro.
    private static byte[] Mask(byte[] data)
    {
        using var stream = new MemoryStream(data);
        BitmapSource source = new FormatConvertedBitmap(BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        source = new TransformedBitmap(source, new ScaleTransform(Side / (double)source.PixelWidth, Side / (double)source.PixelHeight));
        int width = source.PixelWidth, height = source.PixelHeight; var pixels = new byte[width * height * 4]; source.CopyPixels(pixels, width * 4, 0);
        bool opaque = true;
        for (int i = 3; i < pixels.Length && opaque; i += 4) opaque = pixels[i] == 255;
        var mask = new byte[Side * Side];
        for (int y = 0; y < Math.Min(height, Side); y++)
            for (int x = 0; x < Math.Min(width, Side); x++)
            {
                int p = (y * width + x) * 4;
                mask[y * Side + x] = opaque ? (byte)(255 - (pixels[p] * 11 + pixels[p + 1] * 59 + pixels[p + 2] * 30) / 100) : pixels[p + 3];
            }
        return mask;
    }
    // Quattro icone in un riquadro 2×2: colore principale per le macro categorie presenti, grigio chiaro per le altre.
    private static CurriculumImage Compose(byte[][] masks, bool[] active, string main)
    {
        int size = 2 * Side + Gap; var pixels = new byte[size * size * 4];
        for (int i = 0; i < masks.Length; i++)
        {
            string colour = active[i] ? main : Off;
            byte red = Convert.ToByte(colour[..2], 16), green = Convert.ToByte(colour.Substring(2, 2), 16), blue = Convert.ToByte(colour.Substring(4, 2), 16);
            int left = i % 2 * (Side + Gap), top = i / 2 * (Side + Gap);
            for (int y = 0; y < Side; y++)
                for (int x = 0; x < Side; x++)
                {
                    byte alpha = masks[i][y * Side + x];
                    if (alpha == 0) continue;
                    int p = ((top + y) * size + left + x) * 4;
                    pixels[p] = blue; pixels[p + 1] = green; pixels[p + 2] = red; pixels[p + 3] = alpha;
                }
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output);
        return new CurriculumImage(output.ToArray(), "png", size, size);
    }
}
