// Add: using System.Threading;  (at the top with your other using statements)

static void PrintGuildBannerAnimated()
{
    // Simple “fade” + torch flicker animation.
    // Works in console / C# Shell (no true alpha, so we fake fade with color stages + redraw).

    var flickerFrames = new (string L, string R)[]
    {
        ("  (  )  ", "  (  )  "),
        ("  ( * ) ", " ( * )  "),
        ("  (^^)  ", "  (^^)  "),
        ("  ( * ) ", " ( * )  "),
    };

    string[] core = new[]
    {
        "   ██████╗██╗  ██╗ █████╗ ████████╗████████╗███████╗██████╗ ",
        "  ██╔════╝██║  ██║██╔══██╗╚══██╔══╝╚══██╔══╝██╔════╝██╔══██╗",
        "  ██║     ███████║███████║   ██║      ██║   █████╗  ██████╔╝",
        "  ██║     ██╔══██║██╔══██║   ██║      ██║   ██╔══╝  ██╔══██╗",
        "  ╚██████╗██║  ██║██║  ██║   ██║      ██║   ███████╗██║  ██║",
        "   ╚═════╝╚═╝  ╚═╝╚═╝  ╚═╝   ╚═╝      ╚═╝   ╚══════╝╚═╝  ╚═╝",
        "",
        "                 ⚔  THE CHATTER'S GUILD  ⚔",
        "",
        "           O        O        O",
        "          /|\\      /|\\      /|\\",
        "          / \\      / \\      / \\",
        "",
        "     Initiator   Listener  Challenger",
        "",
        "  Gather. Speak. Listen. Challenge. Grow."
    };

    void DrawFrame(ConsoleColor titleColor, ConsoleColor artColor, int flickerIndex)
    {
        Console.Clear();

        var (L, R) = flickerFrames[flickerIndex % flickerFrames.Length];

        // Top torches (spacer lines)
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"{L}                                  {R}");
        Console.WriteLine($"{L}                                  {R}");
        Console.ResetColor();

        // Main block with side torches
        for (int i = 0; i < core.Length; i++)
        {
            bool isTitleLine = core[i].Contains("THE CHATTER'S GUILD");

            Console.ForegroundColor = isTitleLine ? titleColor : artColor;

            // Surround ONLY the title line with torches closer-in
            if (isTitleLine)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write($"{L}");
                Console.ForegroundColor = titleColor;
                Console.Write(core[i]);
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"{R}");
            }
            else
            {
                // Use lighter torch margins for the rest
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write("   |  ");
                Console.ForegroundColor = artColor;
                Console.Write(core[i]);
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("  |   ");
            }

            Console.ResetColor();
        }

        // Bottom torches
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"{L}                                  {R}");
        Console.ResetColor();
    }

    // “Fade in” by color stages (dim → medium → bright), each with a few flickers.
    var stages = new (ConsoleColor title, ConsoleColor art, int frames, int delayMs)[]
    {
        (ConsoleColor.DarkYellow, ConsoleColor.DarkGray, 3, 80),
        (ConsoleColor.Yellow,     ConsoleColor.Gray,     4, 70),
        (ConsoleColor.Yellow,     ConsoleColor.Cyan,     6, 60),
    };

    int f = 0;
    foreach (var s in stages)
    {
        for (int i = 0; i < s.frames; i++)
        {
            DrawFrame(s.title, s.art, f++);
            Thread.Sleep(s.delayMs);
        }
    }

    Console.ResetColor();
    Console.WriteLine("\nPress Enter to begin...");
    Console.ReadLine();
}