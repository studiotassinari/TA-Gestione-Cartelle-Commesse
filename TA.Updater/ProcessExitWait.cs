using System.Diagnostics;
using System.IO;
namespace TA.Updater;
public static class ProcessExitWait
{
    public static void Wait(Process process, TimeSpan timeout)
    {
        if(!process.WaitForExit((int)Math.Clamp(timeout.TotalMilliseconds,0,int.MaxValue)))
            throw new IOException("Il processo del servizio sta ancora terminando. Nessun file sostituito; riprovare tra qualche minuto.");
    }
}
