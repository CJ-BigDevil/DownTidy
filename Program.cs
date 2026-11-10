namespace DownTidy;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            return SelfTest.Run();

        using var mutex = new Mutex(true, @"Local\DownTidy.SingleInstance", out var created);
        if (!created) return 0;

        var app = new App();
        app.Run();
        return 0;
    }
}
