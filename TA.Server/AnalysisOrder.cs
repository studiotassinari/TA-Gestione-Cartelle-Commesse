namespace TA.Server;
public static class AnalysisOrder
{
    public static int Number(string code)=>code.Length>=6&&int.TryParse(code.AsSpan(3,3),out var n)?n:-1;
    public static int Year(string year,string code)
    {
        if(int.TryParse(year,out var n)&&n is >=1900 and <=2199)return n;
        if(code.Length>=3&&int.TryParse(code.AsSpan(1,2),out n))return 2000+n;
        return 0;
    }
}
