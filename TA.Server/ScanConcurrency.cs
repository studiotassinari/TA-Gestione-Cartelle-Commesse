namespace TA.Server;
// Un solo limite condiviso fra categorie: sei worker non producono sei letture simultanee.
public static class ScanConcurrency
{
    public static int Limit {get;private set;}=2;
    public static SemaphoreSlim Slots {get;private set;}=new(2,2);
    public static void Configure(int value)
    { Limit=Math.Clamp(value,1,4);Slots.Dispose();Slots=new(Limit,Limit); }
}
