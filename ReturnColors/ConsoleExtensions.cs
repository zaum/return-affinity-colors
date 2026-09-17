namespace ReturnColors;

internal static class ConsoleExtensions
{
    extension(Console)
    {
        public static void GreenLine(string? value) => Log.Success(value);

        public static void RedLine(string? value) => Log.Error(value);

        public static void YellowLine(string? value) => Log.Warning(value);
    }
}
