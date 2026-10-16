namespace Vtsnap;

public static class Program
{
    public static int Main(string[] args)
    {
        IIo io = new ProcessIo();
        return Cli.Run(args, io);
    }
}
