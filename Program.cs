using System;

internal static class Program
{
    private static void Main()
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            if (attempt == 2)
            {
                SimulatedConfiguration.ShouldThrow = false;
                Console.WriteLine("\nConfiguration corrected: initialization would now succeed.");
            }

            Console.WriteLine("Access attempt {0}:", attempt);
            bool failed = false;

            try
            {
                Console.WriteLine("IsConfigured = {0}", MeterConfiguration.IsConfigured);
            }
            catch (TypeInitializationException exception)
            {
                failed = true;
                Console.WriteLine("  {0}", exception.GetType().Name);
                Console.WriteLine("  Inner: {0}: {1}",
                    exception.InnerException.GetType().Name,
                    exception.InnerException.Message);

                if (!(exception.InnerException is InvalidOperationException))
                {
                    throw;
                }
            }

            if (!failed || SimulatedConfiguration.StaticConstructorCalls != 1)
            {
                throw new InvalidOperationException("Static constructor poisoning was not reproduced.");
            }
        }

        Console.WriteLine("\nStatic constructor calls: {0}",
            SimulatedConfiguration.StaticConstructorCalls);
        Console.WriteLine("PASS: all 3 accesses failed, even after correcting configuration.");
    }
}

internal static class MeterConfiguration
{
    public static bool IsConfigured { get; private set; }

    static MeterConfiguration()
    {
        // Keep the counter outside this type so it remains readable after failure.
        SimulatedConfiguration.StaticConstructorCalls++;
        Console.WriteLine("  Static constructor entered.");
        SimulatedConfiguration.Configure();
        IsConfigured = true;
    }
}

internal static class SimulatedConfiguration
{
    public static bool ShouldThrow = true;
    public static int StaticConstructorCalls;

    public static void Configure()
    {
        if (ShouldThrow)
        {
            throw new InvalidOperationException("Deliberate initialization failure.");
        }
    }
}
